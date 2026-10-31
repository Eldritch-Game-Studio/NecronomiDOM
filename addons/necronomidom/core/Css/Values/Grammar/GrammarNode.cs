using System.Collections.Generic;

namespace NecronomiDom.Css.Values.Grammar
{
    /// <summary>A node of a parsed CSS Values 4 § 2 value definition syntax.</summary>
    internal abstract class GrammarNode
    {
    }

    /// <summary>A keyword literal (§ 2.1 "keyword values"), stored lowercase, matched ASCII case-insensitively.</summary>
    internal sealed class KeywordNode : GrammarNode
    {
        public KeywordNode(string name) => Name = name;

        public string Name { get; }

        public override string ToString() => Name;
    }

    /// <summary>A literal token: <c>,</c> <c>/</c> or a quoted delimiter such as <c>'+'</c>.</summary>
    internal sealed class LiteralNode : GrammarNode
    {
        public LiteralNode(string text) => Text = text;

        public string Text { get; }
        public bool IsComma => Text == ",";

        public override string ToString() => "'" + Text + "'";
    }

    /// <summary>
    /// A component value type <c>&lt;name&gt;</c> (§ 2.1), a property reference <c>&lt;'name'&gt;</c>, or a functional
    /// notation reference <c>&lt;name()&gt;</c>; optional numeric range <c>[min,max]</c> (§ 2.4.1).
    /// </summary>
    internal sealed class TypeNode : GrammarNode
    {
        public TypeNode(string name, bool isProperty, double min, double max)
        {
            Name = name;
            IsProperty = isProperty;
            Min = min;
            Max = max;
        }

        public string Name { get; }
        public bool IsProperty { get; }
        public double Min { get; }
        public double Max { get; }
        public bool HasRange => !double.IsNegativeInfinity(Min) || !double.IsPositiveInfinity(Max);

        public override string ToString() => IsProperty ? "<'" + Name + "'>" : "<" + Name + ">";
    }

    /// <summary>A function written inline in a grammar, e.g. <c>rgb( … )</c>.</summary>
    internal sealed class FunctionNode : GrammarNode
    {
        public FunctionNode(string name, GrammarNode body)
        {
            Name = name;
            Body = body;
        }

        public string Name { get; }
        public GrammarNode Body { get; }

        public override string ToString() => Name + "( " + Body + " )";
    }

    /// <summary>A literal bracket or parenthesis simple block in a Values 4 § 2 grammar.</summary>
    internal sealed class BlockNode : GrammarNode
    {
        public BlockNode(GrammarNode body, char opener = '[') { Body = body; Opener = opener; }

        public GrammarNode Body { get; }
        public char Opener { get; }

        public override string ToString() => "'" + Opener + "' " + Body + " '" + (Opener == '[' ? ']' : ')') + "'";
    }

    /// <summary>Combinator kinds (§ 2.2) in decreasing precedence.</summary>
    internal enum CombinatorKind { Sequence, AllOf, AnyOf, OneOf }

    /// <summary>Juxtaposition, <c>&amp;&amp;</c>, <c>||</c> or <c>|</c> over child nodes.</summary>
    internal sealed class CombinationNode : GrammarNode
    {
        public CombinationNode(CombinatorKind kind, IReadOnlyList<GrammarNode> items)
        {
            Kind = kind;
            Items = items;
        }

        public CombinatorKind Kind { get; }
        public IReadOnlyList<GrammarNode> Items { get; }

        public override string ToString()
        {
            string sep = Kind switch { CombinatorKind.AllOf => " && ", CombinatorKind.AnyOf => " || ", CombinatorKind.OneOf => " | ", _ => " " };
            return "[ " + string.Join(sep, Items) + " ]";
        }
    }

    /// <summary>
    /// Multipliers (§ 2.3): <c>* + ? {A} {A,} {A,B}</c> and <c>#</c> (comma-separated repetition). <see cref="Max"/> is
    /// <see cref="int.MaxValue"/> for unbounded repetition.
    /// </summary>
    internal sealed class MultiplierNode : GrammarNode
    {
        public MultiplierNode(GrammarNode child, int min, int max, bool commaSeparated)
        {
            Child = child;
            Min = min;
            Max = max;
            CommaSeparated = commaSeparated;
        }

        public GrammarNode Child { get; }
        public int Min { get; }
        public int Max { get; }
        public bool CommaSeparated { get; }

        public override string ToString() => Child + (CommaSeparated ? "#" : "") + "{" + Min + "," + (Max == int.MaxValue ? "" : Max.ToString(System.Globalization.CultureInfo.InvariantCulture)) + "}";
    }

    /// <summary>The <c>!</c> multiplier on a group: the group must produce at least one value (§ 2.3).</summary>
    internal sealed class RequiredNode : GrammarNode
    {
        public RequiredNode(GrammarNode child) => Child = child;

        public GrammarNode Child { get; }

        public override string ToString() => Child + "!";
    }

    /// <summary>Matches nothing (unresolvable reference or an empty group).</summary>
    internal sealed class NeverNode : GrammarNode
    {
        public NeverNode(string reason) => Reason = reason;

        public string Reason { get; }

        public override string ToString() => "<never:" + Reason + ">";
    }
}
