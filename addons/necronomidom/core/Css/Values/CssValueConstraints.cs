using System;
using System.Collections.Generic;
using NecronomiDom.Css.Syntax;

namespace NecronomiDom.Css.Values
{
    internal static class CssValueConstraints
    {
        internal static bool Check(string property, IReadOnlyList<ComponentValue> tokens)
        {
            if (property is "font-feature-settings" or "font-variation-settings" && !FeatureTags(tokens)) return false;
            if (property == "font-language-override" && !LanguageTag(tokens)) return false;
            if (property is "animation-name" or "animation")
                foreach (ComponentValue token in tokens)
                    if (token is PreservedToken { Token.Kind: CssTokenKind.String } text && text.Token.Value.Length == 0) return false;
            if (property == "grid-template-areas" && !GridAreas(tokens)) return false;
            if (property is "grid-template-columns" or "grid-template-rows" && !TrackNames(tokens)) return false;
            if (property == "mask")
                foreach (var token in tokens)
                    if (token is PreservedToken name && name.Token.IsIdent("margin-box")) return false;
            if (property is "animation" or "transition")
            {
                bool timeSeen = false;
                int layers = 1;
                bool none = false;
                foreach (ComponentValue token in tokens)
                {
                    if (token is not PreservedToken p) continue;
                    if (p.Token.Kind == CssTokenKind.Comma) { timeSeen = false; layers++; }
                    else if (p.Token.Kind == CssTokenKind.Dimension && CssTypeParser.UnitType(p.Token.Unit) == "time")
                    {
                        if (!timeSeen && p.Token.Number < 0) return false;
                        timeSeen = true;
                    }
                    else if (p.Token.IsIdent("none")) none = true;
                }
                if (property == "transition" && layers > 1 && none) return false;
            }
            foreach (ComponentValue component in tokens)
            {
                if (component is FunctionValue f)
                {
                    var args = TokenText.WithoutWhitespace(f.Arguments);
                    // Filter Effects 1 constrains these arguments in prose, beyond Webref's formal grammar.
                    // A literal below zero is invalid; a calculation's range is enforced when it is evaluated.
                    if (args.Count == 1 && args[0] is PreservedToken number && number.Token.Number < 0
                        && number.Token.Kind is CssTokenKind.Number or CssTokenKind.Percentage or CssTokenKind.Dimension
                        && (NecronomiDom.Dom.Names.AsciiEqual(f.Name, "blur")
                            || NecronomiDom.Dom.Names.AsciiEqual(f.Name, "brightness")
                            || NecronomiDom.Dom.Names.AsciiEqual(f.Name, "contrast")
                            || NecronomiDom.Dom.Names.AsciiEqual(f.Name, "grayscale")
                            || NecronomiDom.Dom.Names.AsciiEqual(f.Name, "invert")
                            || NecronomiDom.Dom.Names.AsciiEqual(f.Name, "opacity")
                            || NecronomiDom.Dom.Names.AsciiEqual(f.Name, "saturate")
                            || NecronomiDom.Dom.Names.AsciiEqual(f.Name, "sepia"))) return false;
                    if (NecronomiDom.Dom.Names.AsciiEqual(f.Name, "symbols") && args.Count > 0
                        && args[0] is PreservedToken symbolType && (symbolType.Token.IsIdent("numeric") || symbolType.Token.IsIdent("alphabetic"))
                        && args.Count < 3) return false;
                    if (NecronomiDom.Dom.Names.AsciiEqual(f.Name, "path"))
                    {
                        if (args.Count == 0 || args[^1] is not PreservedToken { Token.Kind: CssTokenKind.String } path || !CssPathData.IsValid(path.Token.Value)) return false;
                    }
                    if (NecronomiDom.Dom.Names.AsciiEqual(f.Name, "steps"))
                    {
                        if (args.Count == 0) return false;
                        if (args[0] is PreservedToken n && n.Token.Kind == CssTokenKind.Number)
                        {
                            if (n.Token.Number < 1) return false;
                            if (args.Count == 3 && args[2] is PreservedToken k && k.Token.IsIdent("jump-none") && n.Token.Number < 2) return false;
                        }
                    }
                    if (NecronomiDom.Dom.Names.AsciiEqual(f.Name, "linear"))
                    {
                        int commas = 0;
                        foreach (ComponentValue arg in args) if (arg is PreservedToken { Token.Kind: CssTokenKind.Comma }) commas++;
                        if (commas == 0) return false;
                    }
                    if (!Check("", f.Arguments)) return false;
                }
                else if (component is SimpleBlock b && !Check("", b.Value)) return false;
            }
            return true;
        }

        private static bool TrackNames(IReadOnlyList<ComponentValue> tokens)
        {
            bool previous = false;
            foreach (var token in tokens)
            {
                if (token is PreservedToken name && name.Token.IsIdent("subgrid")) return true;
                bool names = token is SimpleBlock { Opener: CssTokenKind.LeftBracket };
                if (names && previous) return false;
                if (token is not PreservedToken { Token.Kind: CssTokenKind.Whitespace }) previous = names;
                if (token is FunctionValue f && !TrackNames(f.Arguments)) return false;
            }
            return true;
        }

        private static bool LanguageTag(IReadOnlyList<ComponentValue> tokens)
        {
            foreach (ComponentValue token in tokens)
                if (token is PreservedToken { Token.Kind: CssTokenKind.String } text)
                {
                    if (text.Token.Value.Length is < 1 or > 4) return false;
                    foreach (char c in text.Token.Value) if (c < 0x20 || c > 0x7e) return false;
                }
            return true;
        }

        internal static bool FeatureTags(IReadOnlyList<ComponentValue> tokens)
        {
            foreach (ComponentValue component in tokens)
            {
                if (component is not PreservedToken { Token.Kind: CssTokenKind.String } t) continue;
                if (t.Token.Value.Length != 4) return false;
                foreach (char c in t.Token.Value) if (c < 0x20 || c > 0x7e) return false;
            }
            return true;
        }

        internal static bool GridAreas(IReadOnlyList<ComponentValue> tokens)
        {
            var rows = new List<string[]>();
            int columns = -1;
            foreach (ComponentValue component in tokens)
            {
                if (component is PreservedToken { Token.Kind: CssTokenKind.Ident } t && t.Token.IsIdent("none")) return tokens.Count == 1;
                if (component is not PreservedToken { Token.Kind: CssTokenKind.String } s) continue;
                List<string>? cells = GridCells(s.Token.Value);
                if (cells == null) return false;
                if (cells.Count == 0 || columns >= 0 && cells.Count != columns) return false;
                columns = cells.Count;
                rows.Add(cells.ToArray());
            }
            var bounds = new Dictionary<string, (int Top, int Left, int Bottom, int Right)>(StringComparer.Ordinal);
            for (int y = 0; y < rows.Count; y++)
                for (int x = 0; x < columns; x++)
                {
                    string name = rows[y][x];
                    if (name == ".") continue;
                    if (bounds.TryGetValue(name, out var b)) bounds[name] = (b.Top, System.Math.Min(b.Left, x), y, System.Math.Max(b.Right, x));
                    else bounds.Add(name, (y, x, y, x));
                }
            foreach (var entry in bounds)
                for (int y = entry.Value.Top; y <= entry.Value.Bottom; y++)
                    for (int x = entry.Value.Left; x <= entry.Value.Right; x++)
                        if (rows[y][x] != entry.Key) return false;
            return rows.Count > 0;
        }
        internal static List<string>? GridCells(string row)
        {
            var cells = new List<string>();
            for (int i = 0; i < row.Length;)
            {
                if (CssTokenizer.IsWhitespace(row[i])) { i++; continue; }
                int start = i;
                bool dots = row[i] == '.';
                while (i < row.Length && (dots ? row[i] == '.' : IsAreaCharacter(row[i]))) i++;
                if (i == start) return null;
                cells.Add(dots ? "." : row[start..i]);
            }
            return cells;
        }
        private static bool IsAreaCharacter(char c) => char.IsLetterOrDigit(c) || c is '_' or '-' || c >= 0x80;
    }
}
