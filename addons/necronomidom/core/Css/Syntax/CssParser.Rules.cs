using System;
using System.Collections.Generic;

namespace NecronomiDom.Css.Syntax
{
    public static partial class CssParser
    {
        private static IReadOnlyList<RawRule> Rules(IReadOnlyList<ComponentValue> values, bool topLevel, IList<WebDiagnostic>? diagnostics, string file)
        {
            var result = new List<RawRule>();
            var p = 0;
            while (p < values.Count)
            {
                if (Is(values[p], CssTokenKind.Whitespace) || (topLevel && (Is(values[p], CssTokenKind.Cdo) || Is(values[p], CssTokenKind.Cdc)))) { p++; continue; }
                var rule = ConsumeRule(values, ref p, diagnostics, file);
                if (rule != null) result.Add(rule);
            }
            return result;
        }
        private static RawRule? ConsumeRule(IReadOnlyList<ComponentValue> values, ref int p, IList<WebDiagnostic>? diagnostics, string file, bool nested = false)
        {
            if (p >= values.Count) return null;
            var startValue = values[p];
            var at = startValue is PreservedToken token && token.Token.Kind == CssTokenKind.AtKeyword ? token.Token.Value : null;
            if (at != null) p++;
            var start = p;
            while (p < values.Count)
            {
                if (values[p] is SimpleBlock block && block.Opener == CssTokenKind.LeftBrace)
                {
                    var prelude = Trim(Slice(values, start, p - start));
                    p++;
                    if (at != null) return new RawAtRule(at, prelude, block);
                    if (prelude.Count == 0 || (nested && LooksLikeCustomProperty(prelude)))
                    { Error(diagnostics, file, startValue, "Invalid qualified rule prelude."); return null; }
                    return new RawQualifiedRule(prelude, block);
                }
                if (Is(values[p], CssTokenKind.RightBrace) || (Is(values[p], CssTokenKind.Semicolon) && (at != null || nested)))
                {
                    var prelude = Trim(Slice(values, start, p - start));
                    p++;
                    if (at != null) return new RawAtRule(at, prelude, null);
                    Error(diagnostics, file, startValue, "Declaration or nested rule could not be parsed.");
                    return null;
                }
                p++;
            }
            if (at != null) return new RawAtRule(at, Trim(Slice(values, start, p - start)), null);
            Error(diagnostics, file, startValue, "Qualified rule has no block.");
            return null;
        }
        private static bool LooksLikeCustomProperty(IReadOnlyList<ComponentValue> values)
        {
            if (values.Count < 2 || values[0] is not PreservedToken token || token.Token.Kind != CssTokenKind.Ident || !token.Token.Value.StartsWith("--", StringComparison.Ordinal)) return false;
            var p = 1;
            while (p < values.Count && Is(values[p], CssTokenKind.Whitespace)) p++;
            return p < values.Count && Is(values[p], CssTokenKind.Colon);
        }
        private static RawDeclaration? Declaration(IReadOnlyList<ComponentValue> values, IList<WebDiagnostic>? diagnostics, string file)
        {
            if (values.Count == 0) return null;
            if (values[0] is not PreservedToken name || name.Token.Kind != CssTokenKind.Ident)
            { Error(diagnostics, file, values[0], "Declaration must begin with an identifier."); return null; }
            var p = 1;
            while (p < values.Count && Is(values[p], CssTokenKind.Whitespace)) p++;
            if (p >= values.Count || !Is(values[p], CssTokenKind.Colon))
            { Error(diagnostics, file, values[0], "Declaration is missing its colon."); return null; }
            var value = Trim(Slice(values, p + 1, values.Count - p - 1));
            var end = value.Count;
            var important = false;
            if (end > 0 && IsIdent(value[end - 1], "important"))
            {
                var bang = end - 2;
                while (bang >= 0 && Is(value[bang], CssTokenKind.Whitespace)) bang--;
                if (bang >= 0 && IsDelim(value[bang], '!')) { important = true; end = bang; }
            }
            value = Trim(Slice(value, 0, end));
            var custom = name.Token.Value.StartsWith("--", StringComparison.Ordinal);
            foreach (var component in value)
            {
                if (Is(component, CssTokenKind.Semicolon) || Is(component, CssTokenKind.BadString) || Is(component, CssTokenKind.BadUrl) ||
                    Is(component, CssTokenKind.RightBrace) || Is(component, CssTokenKind.RightBracket) || Is(component, CssTokenKind.RightParen) ||
                    (!custom && component is SimpleBlock block && block.Opener == CssTokenKind.LeftBrace))
                { Error(diagnostics, file, component, "Invalid declaration value."); return null; }
                if (InvalidComponent(component)) { Error(diagnostics, file, component, "Invalid token within declaration value."); return null; }
            }
            return new RawDeclaration(custom ? name.Token.Value : Dom.Names.AsciiLower(name.Token.Value), value, important, name.Line, name.Column);
        }
        private static bool InvalidComponent(ComponentValue value)
        {
            if (value is PreservedToken token)
                return token.Token.Kind is CssTokenKind.BadString or CssTokenKind.BadUrl or CssTokenKind.RightBrace or CssTokenKind.RightBracket or CssTokenKind.RightParen;
            var children = value is FunctionValue function ? function.Arguments : value is SimpleBlock block ? block.Value : null;
            if (children != null) foreach (var child in children) if (InvalidComponent(child)) return true;
            return false;
        }
        private static void Error(IList<WebDiagnostic>? diagnostics, string file, ComponentValue value, string message) =>
            diagnostics?.Add(new WebDiagnostic(WebDiagnosticSeverity.Warning, file, value.Line, value.Column, "css-parse-error", message));
    }
}
