using System.Collections.Generic;
using NecronomiDom.Css.Selectors;
using NecronomiDom.Css.Syntax;
using NUnit.Framework;

namespace NecronomiDom.Tests.Selectors
{
    [TestFixture]
    public sealed class SelectorParserTests
    {
        [TestCase("div ol>li + p ~ a", "div ol > li + p ~ a")]
        [TestCase("[att=val i]", "[att=\"val\" i]")]
        [TestCase("[att~=val]", "[att~=\"val\"]")]
        [TestCase("[att|=en]", "[att|=\"en\"]")]
        [TestCase("[att^=x][att$=y][att*=z s]", "[att^=\"x\"][att$=\"y\"][att*=\"z\" s]")]
        [TestCase("a:before", "a::before")]
        [TestCase(":IS(.a,#b)", ":is(.a, #b)")]
        [TestCase(":is(.a,:unknown)", ":is(.a)")]
        [TestCase(":where(:unknown)", ":where()")]
        [TestCase(":has(> .a,+ #b)", ":has(> .a, + #b)")]
        [TestCase("::part(foo bar):hover::before", "::part(foo bar):hover::before")]
        [TestCase("::slotted(.a)::before", "::slotted(.a)::before")]
        [TestCase("::picker(select)", "::picker(select)")]
        [TestCase(":lang(en,\"*-Latn\")", ":lang(en, \"*-Latn\")")]
        [TestCase("&", "&")]
        [TestCase(":is(&:unknown,.a)", ":is(&:unknown, .a)")]
        public void ValidSelectorsHaveCanonicalSerialization(string source, string expected) => Assert.That(SelectorParser.ParseSelectorList(source)?.Serialize(), Is.EqualTo(expected));

        [TestCase("")]
        [TestCase("a,")]
        [TestCase("#123")]
        [TestCase(".123")]
        [TestCase("[x=]")]
        [TestCase("[x=x q]")]
        [TestCase("[x!y]")]
        [TestCase(":unknown")]
        [TestCase("::unknown")]
        [TestCase(":not(.a,:unknown)")]
        [TestCase(":has(.a:has(.b))")]
        [TestCase(":has(::before)")]
        [TestCase(":host(a b)")]
        [TestCase(":host-context(.a,.b)")]
        [TestCase(":dir(auto)")]
        [TestCase("::picker(div)")]
        [TestCase("::part(0)")]
        [TestCase("::slotted(a,b)")]
        [TestCase("::slotted(.a):hover")]
        [TestCase("::before .a")]
        [TestCase("a >")]
        [TestCase("> a")]
        [TestCase("svg|a")]
        public void InvalidSelectorsRejectWholeUnforgivingList(string source) => Assert.That(SelectorParser.ParseSelectorList(source), Is.Null);

        [TestCase("#a.b div", 1, 1, 1)]
        [TestCase(":where(#a).b", 0, 1, 0)]
        [TestCase(":is(.a,#b)", 1, 0, 0)]
        [TestCase(":not(div,#b)", 1, 0, 0)]
        [TestCase(":has(> #a,.b)", 1, 0, 0)]
        [TestCase(":nth-child(2n+1 of #a,.b)", 1, 1, 0)]
        [TestCase(":host(.a)", 0, 2, 0)]
        [TestCase("::slotted(.a)", 0, 1, 1)]
        [TestCase("::part(a)", 0, 0, 1)]
        public void SpecificityUsesSpecialPseudoRules(string source, int a, int b, int c) => Assert.That(SelectorParser.ParseSelectorList(source)!.Specificity, Is.EqualTo(new Specificity(a, b, c)));

        [TestCase("odd", 2, 1)]
        [TestCase("even", 2, 0)]
        [TestCase("-n-1", -1, -1)]
        [TestCase("2n + 3", 2, 3)]
        [TestCase("+n- 3", 1, -3)]
        [TestCase("n + 2", 1, 2)]
        [TestCase("-2n-3", -2, -3)]
        [TestCase("2n- 3", 2, -3)]
        [TestCase("3", 0, 3)]
        [TestCase("n/**/+1", 1, 1)]
        public void AnBUsesTokenMicrosyntax(string source, int a, int b)
        { var nth = SelectorParser.ParseNth(CssParser.ParseComponentValueList(source)); Assert.That(nth, Is.Not.Null); Assert.That(nth!.A, Is.EqualTo(a)); Assert.That(nth.B, Is.EqualTo(b)); }
        [TestCase("2 n")]
        [TestCase("2/**/n")]
        [TestCase("+ n")]
        [TestCase("2.0n")]
        [TestCase("n 1")]
        [TestCase("n- +1")]
        [TestCase("1e2")]
        [TestCase("n + +1")]
        [TestCase("of .a")]
        public void MalformedAnBDoesNotBecomeValidByJoiningTokens(string source) => Assert.That(SelectorParser.ParseNth(CssParser.ParseComponentValueList(source)), Is.Null);

        [Test]
        public void NamespaceBindingsAndRelativeNestingRemainInAst()
        {
            var options = new SelectorParseOptions(Namespaces: new Dictionary<string, string> { ["svg"] = "http://www.w3.org/2000/svg" });
            var list = SelectorParser.ParseSelectorList("svg|a, *|*, |div", options)!;
            Assert.That(((TypeSelector)list.Selectors[0].Compounds[0].SimpleSelectors[0]).NamespacePrefix, Is.EqualTo("svg"));
            Assert.That(list.Serialize(), Is.EqualTo("svg|a, *, |div"));
            var relative = SelectorParser.ParseSelectorList("> &.x", new SelectorParseOptions(Relative: true, AllowNesting: true))!;
            Assert.That(relative.Selectors[0].LeadingCombinator, Is.EqualTo(Combinator.Child));
            Assert.That(relative.Selectors[0].Compounds[0].SimpleSelectors[0], Is.TypeOf<NestingSelector>());
        }
    }
}
