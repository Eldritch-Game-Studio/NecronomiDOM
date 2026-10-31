using System;
using System.Collections.Generic;
using System.Text;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values;

namespace NecronomiDom.Css.Om
{
    public sealed partial class CSSStyleDeclaration
    {
        private static readonly Lazy<IReadOnlyList<PropertyDefinition>> Shorthands = new Lazy<IReadOnlyList<PropertyDefinition>>(() =>
        {
            var definitions = new List<PropertyDefinition>();
            foreach (var name in CssPropertyRegistry.SupportedNames)
                if (CssPropertyRegistry.Get(name) is { IsShorthand: true } definition) definitions.Add(definition);
            return definitions;
        });
        public string CssText
        {
            get
            {
                Read();
                if (IsComputed) return "";
                var result = new StringBuilder();
                var serialized = new HashSet<string>(StringComparer.Ordinal);
                foreach (var declaration in _declarations)
                {
                    if (serialized.Contains(declaration.Name)) continue;
                    var name = declaration.Name;
                    var value = declaration.Value.Serialize();
                    IReadOnlyList<string>? bestLonghands = null;
                    if (_propertyGrammar && !name.StartsWith("--", StringComparison.Ordinal))
                    {
                        foreach (var candidate in Shorthands.Value)
                        {
                            var candidateLonghands = Longhands(candidate);
                            if (bestLonghands != null && candidateLonghands.Count <= bestLonghands.Count) continue;
                            var includes = false;
                            var available = true;
                            foreach (var longhand in candidateLonghands)
                            {
                                includes |= longhand == declaration.Name;
                                if (serialized.Contains(longhand)) { available = false; break; }
                            }
                            if (!available || !includes || !CanSerializeShorthand(candidateLonghands) || !SamePriority(candidateLonghands, out _) || !ShorthandSerializer.TrySerialize(candidate.Name, longhand => Find(longhand)?.Value, out var shorthand)) continue;
                            bestLonghands = candidateLonghands;
                            name = candidate.Name;
                            value = shorthand;
                        }
                    }
                    if (bestLonghands != null) foreach (var longhand in bestLonghands) serialized.Add(longhand);
                    else serialized.Add(name);
                    if (result.Length > 0) result.Append(' ');
                    result.Append(CssSerialization.SerializeIdentifier(name)).Append(": ").Append(value);
                    if (declaration.Important) result.Append(" !important");
                    result.Append(';');
                }
                return result.ToString();
            }
            set
            {
                EnsureWritable();
                var replacement = _descriptorRule == null ? new CSSStyleDeclaration(ParentRule) : new CSSStyleDeclaration(ParentRule!, _descriptorRule);
                replacement.Load(CssParser.ParseBlockContents(value));
                if (CssText == replacement.CssText) return;
                BeforeChange?.Invoke();
                _computedIndex = null;
                _declarations.Clear();
                _declarations.AddRange(replacement._declarations);
                Changed?.Invoke();
            }
        }
        private bool CanSerializeShorthand(IReadOnlyList<string> longhands)
        {
            var first = _declarations.Count;
            var last = -1;
            for (var i = 0; i < _declarations.Count; i++)
                foreach (var longhand in longhands)
                    if (_declarations[i].Name == longhand) { first = System.Math.Min(first, i); last = i; break; }
            for (var i = first; i <= last; i++)
            {
                var included = false;
                foreach (var longhand in longhands) if (_declarations[i].Name == longhand) { included = true; break; }
                if (included) continue;
                foreach (var longhand in longhands)
                    if (MappingConflicts(longhand, _declarations[i].Name)) return false;
            }
            return true;
        }
    }
}
