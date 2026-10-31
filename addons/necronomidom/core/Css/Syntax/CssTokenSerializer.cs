using System;
using System.Collections.Generic;
using System.Text;

namespace NecronomiDom.Css.Syntax
{
    /// <summary>CSS Syntax 3 § 9 component-value serialization, preserving token boundaries with comments.</summary>
    public static class CssTokenSerializer
    {
        public static string Serialize(IReadOnlyList<ComponentValue> values) => SerializeValues(values, false);
        /// <summary>CSS Variables 1 § 3 token-stream serialization, retaining authored strings in pending/custom values.</summary>
        public static string SerializeOriginal(IReadOnlyList<ComponentValue> values) => SerializeValues(values, true);
        private static string SerializeValues(IReadOnlyList<ComponentValue> values, bool originalStrings)
        {
            var result = new StringBuilder();
            AppendValues(result, values, originalStrings);
            return result.ToString();
        }
        public static string Serialize(ComponentValue value)
        {
            var result = new StringBuilder();
            AppendValue(result, value);
            return result.ToString();
        }
        public static string Serialize(CssToken token) => SerializeToken(token, false);
        private static string SerializeToken(CssToken token, bool originalStrings)
        {
            switch (token.Kind)
            {
                case CssTokenKind.Ident: return CssSerialization.SerializeIdentifier(token.Value);
                case CssTokenKind.Function: return CssSerialization.SerializeIdentifier(token.Value) + "(";
                case CssTokenKind.AtKeyword: return "@" + CssSerialization.SerializeIdentifier(token.Value);
                case CssTokenKind.Hash: return "#" + (token.IsIdHash ? CssSerialization.SerializeIdentifier(token.Value) : EscapeName(token.Value));
                case CssTokenKind.String: return originalStrings && token.Representation.Length > 0 ? token.Representation : CssSerialization.SerializeString(token.Value);
                case CssTokenKind.BadString: return "\"\n";
                case CssTokenKind.Url: return "url(" + EscapeUrl(token.Value) + ")";
                case CssTokenKind.BadUrl: return "url(bad\"bad)";
                case CssTokenKind.Number: return Number(token);
                case CssTokenKind.Percentage: return Number(token) + "%";
                case CssTokenKind.Dimension:
                    var unit = CssSerialization.SerializeIdentifier(token.Unit);
                    if (unit.Length > 0 && (unit[0] == 'e' || unit[0] == 'E') && unit.Length > 1 && (char.IsDigit(unit[1]) || unit[1] == '-'))
                        unit = "\\65 " + unit.Substring(1);
                    return Number(token) + unit;
                case CssTokenKind.Whitespace: return " ";
                case CssTokenKind.Cdo: return "<!--";
                case CssTokenKind.Cdc: return "-->";
                case CssTokenKind.Colon: return ":";
                case CssTokenKind.Semicolon: return ";";
                case CssTokenKind.Comma: return ",";
                case CssTokenKind.LeftBracket: return "[";
                case CssTokenKind.RightBracket: return "]";
                case CssTokenKind.LeftParen: return "(";
                case CssTokenKind.RightParen: return ")";
                case CssTokenKind.LeftBrace: return "{";
                case CssTokenKind.RightBrace: return "}";
                case CssTokenKind.Eof: return string.Empty;
                default: return token.Value == "\\" ? "\\\n" : token.Value;
            }
        }
        private static string Number(CssToken token) => token.Representation.Length > 0 ? token.Representation : CssSerialization.SerializeNumber(token.Number);
        private static string EscapeName(string value)
        {
            var result = new StringBuilder();
            foreach (var c in value)
            {
                if (c == '-' || c == '_' || char.IsLetterOrDigit(c) || c >= 128) result.Append(c);
                else result.Append('\\').Append(c);
            }
            return result.ToString();
        }
        private static string EscapeUrl(string value)
        {
            var result = new StringBuilder();
            foreach (var c in value)
            {
                if (c <= 32 || c == 127) result.Append('\\').Append(((int)c).ToString("x", System.Globalization.CultureInfo.InvariantCulture)).Append(' ');
                else if (c == '"' || c == '\'' || c == '(' || c == ')' || c == '\\') result.Append('\\').Append(c);
                else result.Append(c);
            }
            return result.ToString();
        }
        public static void Append(StringBuilder result, IReadOnlyList<ComponentValue> values) => AppendValues(result, values, false);
        /// <summary>Appends an original pending/custom token stream, using the same boundary and recursive serialization rules.</summary>
        public static void AppendOriginal(StringBuilder result, IReadOnlyList<ComponentValue> values) => AppendValues(result, values, true);
        private static void AppendValues(StringBuilder result, IReadOnlyList<ComponentValue> values, bool originalStrings)
        {
            CssToken? previous = null;
            foreach (var value in values)
            {
                var first = First(value);
                if (previous.HasValue && Separate(previous.Value, first)) result.Append("/**/");
                AppendValue(result, value, originalStrings);
                previous = value is PreservedToken token ? token.Token : null;
            }
        }
        internal static CssToken First(ComponentValue value) => value switch
        {
            PreservedToken token => token.Token,
            SimpleBlock block => block.OpenerToken,
            FunctionValue function => function.NameToken,
            _ => throw new ArgumentException("Unknown component value.", nameof(value)),
        };
        internal static void AppendValue(StringBuilder result, ComponentValue value) => AppendValue(result, value, false);
        internal static void AppendOriginalValue(StringBuilder result, ComponentValue value) => AppendValue(result, value, true);
        private static void AppendValue(StringBuilder result, ComponentValue value, bool originalStrings)
        {
            if (value is PreservedToken token) result.Append(SerializeToken(token.Token, originalStrings));
            else if (value is SimpleBlock block)
            {
                result.Append(Serialize(block.OpenerToken));
                AppendValues(result, block.Value, originalStrings);
                result.Append(block.Opener == CssTokenKind.LeftBrace ? '}' : block.Opener == CssTokenKind.LeftBracket ? ']' : ')');
            }
            else if (value is FunctionValue function)
            {
                result.Append(CssSerialization.SerializeIdentifier(function.Name)).Append('(');
                AppendValues(result, function.Arguments, originalStrings);
                result.Append(')');
            }
        }
        internal static bool Separate(CssToken a, CssToken b)
        {
            var name = a.Kind == CssTokenKind.Ident || a.Kind == CssTokenKind.AtKeyword || a.Kind == CssTokenKind.Hash || a.Kind == CssTokenKind.Dimension;
            var nextName = b.Kind == CssTokenKind.Ident || b.Kind == CssTokenKind.Function || b.Kind == CssTokenKind.Url || b.Kind == CssTokenKind.BadUrl;
            var nextNumber = b.Kind == CssTokenKind.Number || b.Kind == CssTokenKind.Dimension || b.Kind == CssTokenKind.Percentage;
            if (name && (nextName || nextNumber || b.IsDelim('-') || (a.Kind == CssTokenKind.Ident && b.Kind == CssTokenKind.LeftParen))) return true;
            if (a.Kind == CssTokenKind.Number && (nextName || nextNumber || b.IsDelim('%') || b.IsDelim('.'))) return true;
            if ((a.IsDelim('#') || a.IsDelim('@') || a.IsDelim('-')) && (nextName || nextNumber || b.IsDelim('-'))) return true;
            if ((a.IsDelim('+') || a.IsDelim('.')) && nextNumber) return true;
            if (a.IsDelim('/') && b.IsDelim('*')) return true;
            if (a.IsDelim('<') && b.IsDelim('!')) return true;
            return false;
        }
    }
}
