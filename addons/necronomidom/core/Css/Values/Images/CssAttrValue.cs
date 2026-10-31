using System;
using System.Collections.Generic;
using System.Text;
using NecronomiDom.Css.Syntax;

namespace NecronomiDom.Css.Values.Images
{
    /// <summary>CSS Values 5 attr() specified AST; resolved values remain attribute-tainted for resource policy.</summary>
    public sealed class CssAttrValue : CssValue
    {
        internal CssAttrValue(string name,string? unit,string? syntax,IReadOnlyList<ComponentValue>? fallback)
        { Name = name; Unit = unit; Syntax = syntax; Fallback = fallback; }
        public string Name { get; }
        public string? Unit { get; }
        public string? Syntax { get; }
        public IReadOnlyList<ComponentValue>? Fallback { get; }
        public bool IsAttrTainted => true;
        public bool TryResolve(Func<string,string?> lookup,out CssValue? value,Func<string,IReadOnlyList<ComponentValue>,IReadOnlyList<ComponentValue>?>? substitute=null)
        {
            string? text=lookup(Name);
            if (text!=null && TryAttributeValue(text,substitute,out value)) return true;
            if (Fallback!=null) { value=new CssUnparsedValue(Fallback); return true; }
            if (Unit==null && Syntax==null) { value=new CssStringValue(""); return true; }
            value=null; return false;
        }
        private bool TryAttributeValue(string text,Func<string,IReadOnlyList<ComponentValue>,IReadOnlyList<ComponentValue>?>? substitute,out CssValue? value)
        {
            value=null;
            if (Unit==null && Syntax==null) { value=new CssStringValue(text); return true; }
            IReadOnlyList<ComponentValue> tokens=CssParser.ParseComponentValueList(text);
            if (Syntax!=null && substitute!=null)
            {
                if (!CssAttrParser.ValidValueTokens(tokens)) return false;
                IReadOnlyList<ComponentValue>? substituted=substitute(Name,tokens);
                if (substituted==null) return false;
                tokens=substituted;
            }
            if (Syntax=="*")
            {
                if (!CssAttrParser.ValidValueTokens(tokens)) return false;
                value=new CssUnparsedValue(tokens); return true;
            }
            if (Syntax!=null) return CssRegistrationSyntax.TryParseValue(Syntax,tokens,out value,CssSyntaxDefinitionContext.Attribute);
            List<ComponentValue> numeric=TokenText.WithoutWhitespace(tokens);
            if (numeric.Count!=1 || numeric[0] is not PreservedToken { Token.Kind:CssTokenKind.Number } token || !double.IsFinite(token.Token.Number)) return false;
            double number=token.Token.Number;
            value=Unit=="number" ? new CssNumberValue(number,token.Token.NumberType==CssNumberType.Integer) : Unit=="%" ? new CssPercentageValue(number) : new CssDimensionValue(number,Unit!);
            return true;
        }
        public override void AppendTo(StringBuilder sb)
        {
            sb.Append("attr("); CssSerialization.AppendIdentifier(sb,Name);
            if (Syntax!=null) sb.Append(" type(").Append(Syntax).Append(')');
            else if (Unit!=null) sb.Append(' ').Append(Unit);
            if (Fallback!=null) { sb.Append(", "); CssTokenSerializer.AppendOriginal(sb,Fallback); }
            sb.Append(')');
        }
    }
}
