using System.Collections.Generic;

namespace NecronomiDom.Css.Syntax
{
    /// <summary>
    /// CSS Syntax 3 component value: a preserved token, a simple block ({}, [], ()) or a function. Immutable once
    /// built by the parser. <see cref="Line"/>/<see cref="Column"/> point at the first token.
    /// </summary>
    public abstract class ComponentValue
    {
        public abstract int Line { get; }
        public abstract int Column { get; }
        // Source span metadata is used only while selector parsing preserves invalid forgiving
        // branches. It does not retain the original stylesheet string in value objects.
        internal int EndOffset { get; init; } = -1;
    }

    /// <summary>A token that is not a block/function opener (CSS Syntax 3 "preserved token").</summary>
    public sealed class PreservedToken : ComponentValue
    {
        public PreservedToken(CssToken token) => Token = token;

        public CssToken Token { get; }
        public override int Line => Token.Line;
        public override int Column => Token.Column;
        public override string ToString() => Token.Kind + ":" + Token.Value;
    }

    /// <summary>Simple block; <see cref="Opener"/> is LeftBrace, LeftBracket or LeftParen.</summary>
    public sealed class SimpleBlock : ComponentValue
    {
        public SimpleBlock(CssToken opener, IReadOnlyList<ComponentValue> value)
        {
            OpenerToken = opener;
            Value = value;
        }

        public CssToken OpenerToken { get; }
        public CssTokenKind Opener => OpenerToken.Kind;
        public IReadOnlyList<ComponentValue> Value { get; }
        public override int Line => OpenerToken.Line;
        public override int Column => OpenerToken.Column;
    }

    /// <summary>Function component value; <see cref="Name"/> is as written (compare ASCII case-insensitively).</summary>
    public sealed class FunctionValue : ComponentValue
    {
        public FunctionValue(CssToken nameToken, IReadOnlyList<ComponentValue> arguments)
        {
            NameToken = nameToken;
            Arguments = arguments;
        }

        public CssToken NameToken { get; }
        public string Name => NameToken.Value;
        public IReadOnlyList<ComponentValue> Arguments { get; }
        public override int Line => NameToken.Line;
        public override int Column => NameToken.Column;
    }
}
