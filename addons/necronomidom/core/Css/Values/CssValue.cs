using System.Collections.Generic;
using System.Text;
using NecronomiDom.Css.Syntax;

namespace NecronomiDom.Css.Values
{
    /// <summary>
    /// A specified CSS value (CSS Values 4 § 2; CSSOM 1 § 6.7.2). Parsed instances are immutable; Typed OM may own numeric slots.
    /// <see cref="Serialize"/> produces the specified-value serialization Chrome produces for the same input.
    /// </summary>
    public abstract class CssValue
    {
        public string Serialize()
        {
            var sb = new StringBuilder();
            AppendTo(sb);
            return sb.ToString();
        }

        public abstract void AppendTo(StringBuilder sb);

        public override string ToString() => Serialize();
    }

    /// <summary>A keyword or identifier (CSS Values 4 § 4.1–4.2). Grammar keywords are stored lowercase.</summary>
    public sealed class CssKeywordValue : CssValue
    {
        public CssKeywordValue(string value) => Value = value;

        public string Value { get; }

        public bool Is(string keyword) => NecronomiDom.Dom.Names.AsciiEqual(Value, keyword);

        public override void AppendTo(StringBuilder sb) => CssSerialization.AppendIdentifier(sb, Value);
    }

    /// <summary>A literal delimiter kept inside a value (e.g. <c>+</c> in a grammar literal).</summary>
    public sealed class CssDelimValue : CssValue
    {
        public CssDelimValue(string value) => Value = value;

        public string Value { get; }

        public override void AppendTo(StringBuilder sb) => sb.Append(Value);
    }

    /// <summary><c>&lt;number&gt;</c> / <c>&lt;integer&gt;</c> (CSS Values 4 § 5).</summary>
    public class CssNumberValue : CssValue
    {
        public CssNumberValue(double value, bool isInteger)
        {
            Value = value;
            IsInteger = isInteger;
        }

        public virtual double Value { get; }
        public virtual bool IsInteger { get; }

        public override void AppendTo(StringBuilder sb) => sb.Append(NumberFormat.Format(Value));
    }

    /// <summary><c>&lt;integer&gt;</c> grammar value, CSS Values 4 § 5.1; CSSOM integer serialization never uses an exponent.</summary>
    public class CssIntegerValue : CssNumberValue
    {
        public CssIntegerValue(double value) : base(value, true) { }
        public override void AppendTo(StringBuilder sb)
        {
            System.Span<char> buffer = stackalloc char[312];
            double number = Value == 0 ? 0 : Value;
            if (!number.TryFormat(buffer, out int written, "F0", System.Globalization.CultureInfo.InvariantCulture))
                throw new System.InvalidOperationException("CSS integer serialization exceeded its numeric representation.");
            sb.Append(buffer[..written]);
        }
    }

    /// <summary>A dimension (CSS Values 4 § 6–7): value plus canonical-case unit (lowercase, <c>Q</c> kept).</summary>
    public class CssDimensionValue : CssValue
    {
        public CssDimensionValue(double value, string unit)
        {
            Value = value;
            Unit = unit;
        }

        public virtual double Value { get; }
        public virtual string Unit { get; }

        public override void AppendTo(StringBuilder sb) => sb.Append(NumberFormat.Format(Value)).Append(Unit);
    }

    /// <summary><c>&lt;percentage&gt;</c> (CSS Values 4 § 5.5).</summary>
    public class CssPercentageValue : CssValue
    {
        public CssPercentageValue(double value) => Value = value;

        public virtual double Value { get; }

        public override void AppendTo(StringBuilder sb) => sb.Append(NumberFormat.Format(Value)).Append('%');
    }

    /// <summary><c>&lt;string&gt;</c>, serialized per CSSOM § 2.1 "serialize a string".</summary>
    public sealed class CssStringValue : CssValue
    {
        public CssStringValue(string value) => Value = value;

        public string Value { get; }

        public override void AppendTo(StringBuilder sb) => CssSerialization.AppendString(sb, Value);
    }

    /// <summary>CSS Values 4/5 URL and typed request metadata, serialized in canonical modifier order.</summary>
    public sealed class CssUrlValue : CssValue
    {
        public CssUrlValue(string url, string? crossOrigin = null, string? integrity = null, string? referrerPolicy = null)
        {
            Url = url;
            CrossOrigin = crossOrigin;
            Integrity = integrity;
            ReferrerPolicy = referrerPolicy;
        }

        public string Url { get; }
        public string? CrossOrigin { get; }
        public string? Integrity { get; }
        public string? ReferrerPolicy { get; }

        public override void AppendTo(StringBuilder sb)
        {
            sb.Append("url(");
            CssSerialization.AppendString(sb, Url);
            if (CrossOrigin != null) sb.Append(" cross-origin(").Append(CrossOrigin).Append(')');
            if (Integrity != null)
            {
                sb.Append(" integrity(");
                CssSerialization.AppendString(sb, Integrity);
                sb.Append(')');
            }
            if (ReferrerPolicy != null) sb.Append(" referrer-policy(").Append(ReferrerPolicy).Append(')');
            sb.Append(')');
        }
    }

    /// <summary>A functional notation other than math and colour functions; <see cref="Arguments"/> may be null for <c>f()</c>.</summary>
    public sealed class CssFunctionValue : CssValue
    {
        public CssFunctionValue(string name, CssValue? arguments)
        {
            Name = name;
            Arguments = arguments;
        }

        public string Name { get; }
        public CssValue? Arguments { get; }

        public override void AppendTo(StringBuilder sb)
        {
            CssSerialization.AppendIdentifier(sb, Name);
            sb.Append('(');
            Arguments?.AppendTo(sb);
            sb.Append(')');
        }
    }

    /// <summary>A bracket/parenthesis simple block (CSS Syntax 3 § 5), e.g. grid line names or math grouping.</summary>
    public sealed class CssBlockValue : CssValue
    {
        public CssBlockValue(CssValue? content, char opener = '[') { Content = content; Opener = opener; }

        public CssValue? Content { get; }
        public char Opener { get; }

        public override void AppendTo(StringBuilder sb)
        {
            sb.Append(Opener);
            Content?.AppendTo(sb);
            sb.Append(Opener == '[' ? ']' : ')');
        }
    }

    /// <summary>Separator of a <see cref="CssListValue"/>.</summary>
    public enum CssListSeparator { Space, Comma, Slash }

    /// <summary>A space-, comma- or slash-separated list (comma binds loosest, then slash, then space).</summary>
    public class CssListValue : CssValue
    {
        public CssListValue(IReadOnlyList<CssValue> items, CssListSeparator separator)
        {
            Items = items;
            Separator = separator;
        }

        public IReadOnlyList<CssValue> Items { get; }
        public CssListSeparator Separator { get; }

        public override void AppendTo(StringBuilder sb)
        {
            string sep = Separator switch { CssListSeparator.Comma => ", ", CssListSeparator.Slash => " / ", _ => " " };
            for (int i = 0; i < Items.Count; i++)
            {
                if (i > 0) sb.Append(sep);
                Items[i].AppendTo(sb);
            }
        }
    }
}
