namespace NecronomiDom.Css.Syntax
{
    /// <summary>CSS Syntax 3 § 4 token types.</summary>
    public enum CssTokenKind
    {
        Ident, Function, AtKeyword, Hash, String, BadString, Url, BadUrl, Delim,
        Number, Percentage, Dimension, Whitespace, Cdo, Cdc, Colon, Semicolon, Comma,
        LeftBracket, RightBracket, LeftParen, RightParen, LeftBrace, RightBrace, Eof,
    }

    /// <summary>CSS Syntax 3 numeric type flag.</summary>
    public enum CssNumberType { Integer, Number }

    /// <summary>
    /// One CSS Syntax 3 token. <see cref="Value"/> holds the ident/function/at-keyword/hash/string/url value or the
    /// delim code point; numeric tokens carry <see cref="Number"/>, <see cref="NumberType"/>, <see cref="Unit"/>
    /// (dimension only) and <see cref="Representation"/> (the source text of the number part, needed by An+B and
    /// unicode-range). <see cref="SignCharacter"/> is '+' or '-' when the source number had an explicit sign, else '\0'.
    /// Properly terminated string tokens also carry their original quoted source in <see cref="Representation"/>;
    /// original-token declaration contexts preserve it while typed string serialization remains canonical.
    /// Hash tokens set <see cref="IsIdHash"/> when the type flag is "id". Positions are 1-based line/column of the
    /// first code point; <see cref="Offset"/> is the 0-based UTF-16 offset in the preprocessed input.
    /// </summary>
    public readonly record struct CssToken(
        CssTokenKind Kind,
        string Value = "",
        double Number = 0,
        CssNumberType NumberType = CssNumberType.Integer,
        string Unit = "",
        string Representation = "",
        char SignCharacter = '\0',
        bool IsIdHash = false,
        int Line = 1,
        int Column = 1,
        int Offset = 0)
    {
        public bool Is(CssTokenKind kind) => Kind == kind;

        public bool IsDelim(char c) => Kind == CssTokenKind.Delim && Value.Length == 1 && Value[0] == c;

        /// <summary>ASCII case-insensitive ident match.</summary>
        public bool IsIdent(string ident) => Kind == CssTokenKind.Ident && Dom.Names.AsciiEqual(Value, ident);
    }
}
