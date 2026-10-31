using System;
using System.Collections.Generic;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values;

namespace NecronomiDom.Css.Cascade
{
    /// <summary>CSS Custom Properties 1 § 2.3 cycle detection and § 3 var() substitution, including fallback dependencies.</summary>
    internal sealed class CustomPropertyResolver
    {
        private readonly IReadOnlyDictionary<string, IReadOnlyList<ComponentValue>> _specified;
        private readonly Dictionary<string, IReadOnlyList<ComponentValue>?> _resolved = new(StringComparer.Ordinal);
        private readonly HashSet<string> _cyclic = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _visited = new(StringComparer.Ordinal);
        private readonly List<string> _path = new();
        private Func<FunctionValue, IReadOnlyList<ComponentValue>?>? _substitution;
        private readonly HashSet<string> _tainted = new(StringComparer.Ordinal);
        private Func<string, IReadOnlyList<ComponentValue>?, IReadOnlyList<ComponentValue>?>? _registeredValue;
        private IReadOnlyDictionary<string, CssValue>? _computedInherited;
        private bool _retainTokens;

        internal CustomPropertyResolver(IReadOnlyDictionary<string, IReadOnlyList<ComponentValue>> specified,
            Func<FunctionValue, IReadOnlyList<ComponentValue>?>? substitution = null,
            Func<string, IReadOnlyList<ComponentValue>?, IReadOnlyList<ComponentValue>?>? registeredValue = null,
            IReadOnlyDictionary<string, CssValue>? computedInherited = null, bool retainTokens = true)
        {
            _specified = specified;
            Reset(substitution, registeredValue, computedInherited, retainTokens);
        }

        /// <summary>Reuse this entry's scratch after its specified values change; never during active resolution.
        /// Inherited computed values have already substituted every dependency in the parent's scope.</summary>
        internal void Reset(Func<FunctionValue, IReadOnlyList<ComponentValue>?>? substitution = null,
            Func<string, IReadOnlyList<ComponentValue>?, IReadOnlyList<ComponentValue>?>? registeredValue = null,
            IReadOnlyDictionary<string, CssValue>? computedInherited = null, bool retainTokens = true)
        {
            _substitution = substitution; _registeredValue = registeredValue;
            _computedInherited = computedInherited; _retainTokens = retainTokens;
            _resolved.Clear(); _cyclic.Clear(); _visited.Clear(); _path.Clear(); _tainted.Clear();
            if (retainTokens && _specified is Dictionary<string, IReadOnlyList<ComponentValue>> dictionary)
            {
                foreach (string name in dictionary.Keys) FindCycles(name);
            }
            else foreach (string name in _specified.Keys) FindCycles(name);
        }

        internal IReadOnlyList<ComponentValue>? Resolve(string name)
        {
            if (_computedInherited?.ContainsKey(name) == true) return _specified.GetValueOrDefault(name);
            if (_resolved.TryGetValue(name, out var cached)) return cached;
            IReadOnlyList<ComponentValue>? value = null;
            bool tainted = false;
            if (!_cyclic.Contains(name) && _specified.TryGetValue(name, out var tokens)) value = Substitute(tokens, out tainted);
            if (_registeredValue != null) value = _registeredValue(name, value);
            if (tainted) _tainted.Add(name);
            _resolved[name] = value;
            return value;
        }

        internal IReadOnlyList<ComponentValue>? Substitute(IReadOnlyList<ComponentValue> tokens) => Substitute(tokens, out _);

        internal IReadOnlyList<ComponentValue>? Substitute(IReadOnlyList<ComponentValue> tokens, out bool tainted)
        {
            tainted = false;
            List<ComponentValue>? output = _retainTokens ? null : new(tokens.Count);
            for (int i = 0; i < tokens.Count; i++)
            {
                ComponentValue token = tokens[i], next = token;
                IReadOnlyList<ComponentValue>? expanded = null;
                if (token is FunctionValue function)
                {
                    if (function.Name.Equals("var", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!SplitVar(function.Arguments, out string name, out var fallback)) return null;
                        expanded = Resolve(name);
                        bool fallbackTaint = false;
                        if (expanded == null && fallback != null) expanded = Substitute(fallback, out fallbackTaint);
                        tainted |= fallbackTaint || _tainted.Contains(name);
                        if (expanded == null) return null;
                    }
                    else if (function.Name.Equals("attr", StringComparison.OrdinalIgnoreCase) || function.Name.Equals("env", StringComparison.OrdinalIgnoreCase))
                    {
                        var arguments = Substitute(function.Arguments, out bool argumentTaint);
                        if (arguments == null) return null;
                        expanded = _substitution?.Invoke(_retainTokens && ReferenceEquals(arguments, function.Arguments)
                            ? function : new FunctionValue(function.NameToken, arguments));
                        if (expanded == null) return null;
                        tainted |= argumentTaint || function.Name.Equals("attr", StringComparison.OrdinalIgnoreCase);
                    }
                    else
                    {
                        var arguments = Substitute(function.Arguments, out bool argumentTaint);
                        tainted |= argumentTaint;
                        if (arguments == null) return null;
                        if (!_retainTokens || !ReferenceEquals(arguments, function.Arguments))
                            next = new FunctionValue(function.NameToken, arguments);
                    }
                }
                else if (token is SimpleBlock block)
                {
                    var children = Substitute(block.Value, out bool blockTaint);
                    tainted |= blockTaint;
                    if (children == null) return null;
                    if (!_retainTokens || !ReferenceEquals(children, block.Value))
                        next = new SimpleBlock(block.OpenerToken, children);
                }
                if (expanded != null)
                {
                    if (_retainTokens && tokens.Count == 1) return expanded;
                    output ??= CopyPrefix(tokens, i);
                    for (int j = 0; j < expanded.Count; j++) output.Add(expanded[j]);
                }
                else
                {
                    if (!ReferenceEquals(next, token)) output ??= CopyPrefix(tokens, i);
                    output?.Add(next);
                }
            }
            return output ?? tokens;
        }
        private static List<ComponentValue> CopyPrefix(IReadOnlyList<ComponentValue> tokens, int length)
        {
            var output = new List<ComponentValue>(tokens.Count);
            for (int i = 0; i < length; i++) output.Add(tokens[i]);
            return output;
        }

        private void FindCycles(string name)
        {
            if (_computedInherited?.ContainsKey(name) == true) return;
            if (_visited.TryGetValue(name, out int state))
            {
                if (state == 1)
                {
                    int start = _path.IndexOf(name);
                    for (int i = start; i < _path.Count; i++) _cyclic.Add(_path[i]);
                }
                return;
            }
            _visited[name] = 1;
            _path.Add(name);
            if (_specified.TryGetValue(name, out var tokens)) VisitDependencies(tokens);
            _path.RemoveAt(_path.Count - 1);
            _visited[name] = 2;
        }

        private void VisitDependencies(IReadOnlyList<ComponentValue> tokens)
        {
            if (_retainTokens)
            {
                for (int i = 0; i < tokens.Count; i++) VisitDependency(tokens[i]);
            }
            else foreach (ComponentValue token in tokens) VisitDependency(token);
        }
        private void VisitDependency(ComponentValue token)
        {
            if (token is FunctionValue function)
            {
                if (function.Name.Equals("var", StringComparison.OrdinalIgnoreCase)
                    && SplitVar(function.Arguments, out string name, out _)) FindCycles(name);
                VisitDependencies(function.Arguments);
            }
            else if (token is SimpleBlock block) VisitDependencies(block.Value);
        }

        private static bool SplitVar(IReadOnlyList<ComponentValue> arguments, out string name,
            out IReadOnlyList<ComponentValue>? fallback)
        {
            name = string.Empty;
            fallback = null;
            int i = 0;
            while (i < arguments.Count && IsWhitespace(arguments[i])) i++;
            if (i == arguments.Count || arguments[i] is not PreservedToken ident
                || ident.Token.Kind != CssTokenKind.Ident || !ident.Token.Value.StartsWith("--", StringComparison.Ordinal)) return false;
            name = ident.Token.Value;
            i++;
            while (i < arguments.Count && IsWhitespace(arguments[i])) i++;
            if (i == arguments.Count) return true;
            if (arguments[i] is not PreservedToken comma || comma.Token.Kind != CssTokenKind.Comma) return false;
            var rest = new List<ComponentValue>(arguments.Count - i - 1);
            for (i++; i < arguments.Count; i++) rest.Add(arguments[i]);
            fallback = rest;
            return true;
        }

        private static bool IsWhitespace(ComponentValue token) => token is PreservedToken t && t.Token.Kind == CssTokenKind.Whitespace;
    }
}
