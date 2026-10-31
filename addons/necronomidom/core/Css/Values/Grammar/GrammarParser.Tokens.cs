using System;
using System.Collections.Generic;

namespace NecronomiDom.Css.Values.Grammar
{
    internal enum GrammarTokenKind { Type, Keyword, Function, Quoted, Literal, OpenBracket, CloseBracket, OpenParen, CloseParen, Combinator, Multiplier }

    internal readonly record struct GrammarToken(GrammarTokenKind Kind, string Text);

    internal sealed partial class GrammarParser
    {
        private static List<GrammarToken> Tokenize(string s)
        {
            var tokens = new List<GrammarToken>();
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                switch (c)
                {
                    case '<':
                        tokens.Add(new GrammarToken(GrammarTokenKind.Type, ReadAngle(s, ref i)));
                        continue;
                    case '[':
                        tokens.Add(new GrammarToken(GrammarTokenKind.OpenBracket, "["));
                        i++;
                        continue;
                    case ']':
                        tokens.Add(new GrammarToken(GrammarTokenKind.CloseBracket, "]"));
                        i++;
                        continue;
                    case '(':
                        tokens.Add(new GrammarToken(GrammarTokenKind.OpenParen, "("));
                        i++;
                        continue;
                    case ')':
                        tokens.Add(new GrammarToken(GrammarTokenKind.CloseParen, ")"));
                        i++;
                        continue;
                    case '\'':
                    {
                        int end = s.IndexOf('\'', i + 1);
                        if (end < 0) throw new FormatException("unterminated quote in " + s);
                        tokens.Add(new GrammarToken(GrammarTokenKind.Quoted, s.Substring(i + 1, end - i - 1)));
                        i = end + 1;
                        continue;
                    }

                    case '|':
                    case '&':
                        if (i + 1 < s.Length && s[i + 1] == c)
                        {
                            tokens.Add(new GrammarToken(GrammarTokenKind.Combinator, new string(c, 2)));
                            i += 2;
                        }
                        else
                        {
                            if (c == '&') throw new FormatException("lone '&' in " + s);
                            tokens.Add(new GrammarToken(GrammarTokenKind.Combinator, "|"));
                            i++;
                        }

                        continue;
                    case '*':
                    case '+':
                    case '?':
                    case '#':
                    case '!':
                        tokens.Add(new GrammarToken(GrammarTokenKind.Multiplier, c.ToString()));
                        i++;
                        continue;
                    case '{':
                    {
                        int end = s.IndexOf('}', i);
                        if (end < 0) throw new FormatException("unterminated {} in " + s);
                        tokens.Add(new GrammarToken(GrammarTokenKind.Multiplier, s.Substring(i, end - i + 1).Replace(" ", "", StringComparison.Ordinal)));
                        i = end + 1;
                        continue;
                    }
                }

                if (IsNameChar(c))
                {
                    int start = i;
                    while (i < s.Length && IsNameChar(s[i])) i++;
                    string name = s[start..i];
                    if (i < s.Length && s[i] == '(')
                    {
                        i++;
                        tokens.Add(new GrammarToken(GrammarTokenKind.Function, name));
                    }
                    else
                    {
                        tokens.Add(new GrammarToken(GrammarTokenKind.Keyword, name));
                    }

                    continue;
                }

                // ',', '/', and any other bare delimiter are literals.
                tokens.Add(new GrammarToken(GrammarTokenKind.Literal, c.ToString()));
                i++;
            }

            return tokens;
        }

        private static bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c > 0x7f;

        private static string ReadAngle(string s, ref int i)
        {
            int depth = 0;
            int start = i + 1;
            for (int j = i; j < s.Length; j++)
            {
                if (s[j] == '<') depth++;
                else if (s[j] == '>' && --depth == 0)
                {
                    i = j + 1;
                    return s[start..j];
                }
            }

            throw new FormatException("unterminated <> in " + s);
        }
    }
}
