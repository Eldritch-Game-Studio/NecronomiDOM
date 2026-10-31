using System;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values;
using NecronomiDom.Css.Values.Grammar;
using NUnit.Framework;

namespace NecronomiDom.Tests.CssValues
{
    [TestFixture]
    public sealed class GrammarTests
    {
        [TestCase("a b | c", "a b", true)]
        [TestCase("a b | c", "a", false)]
        [TestCase("a b | c", "c", true)]
        [TestCase("a && b", "b a", true)]
        [TestCase("a && b", "a", false)]
        [TestCase("a? && b", "b", true)]
        [TestCase("a || b", "b", true)]
        [TestCase("a || b", "b a", true)]
        [TestCase("a || b", "a a", false)]
        [TestCase("[ a? b? ]!", "", false)]
        [TestCase("[ a? b? ]!", "b", true)]
        [TestCase("a{2,3}", "a a a", true)]
        [TestCase("a{2,3}", "a", false)]
        [TestCase("a{2,}", "a a a a", true)]
        [TestCase("a* b", "b", true)]
        [TestCase("a+ b", "b", false)]
        [TestCase("[ a | a b ] c", "a b c", true)]
        [TestCase("<number>#{2}", "1, 2", true)]
        [TestCase("<number>#{2}", "1 2", false)]
        [TestCase("<number>#{2}", "1, 2,", false)]
        [TestCase("[ <integer> && <string> ]#", "'a' 1, 2 'b'", true)]
        [TestCase("<integer [1,3]>", "0", false)]
        [TestCase("<integer [1,3]>", "3", true)]
        [TestCase("<integer>", "1.0", false)]
        [TestCase("<number>", "1.0", true)]
        [TestCase("<length [0,∞]>", "-1px", false)]
        [TestCase("<angle [-90deg,90deg]>", "90deg", true)]
        [TestCase("f( <number> , <number>? )", "f(1)", true)]
        [TestCase("f( <number> , <number>? )", "f(1 2)", false)]
        [TestCase("f( <number> , <number>? )", "f(1, 2)", true)]
        [TestCase("<number>#? , <string>", "'x'", true)]
        [TestCase("'[' <custom-ident>* ']'", "[a b]", true)]
        [TestCase("'[' <custom-ident>* ']'", "[]", true)]
        [TestCase("'[' <custom-ident>* ']'", "[inherit]", false)]
        [TestCase("( <number> )", "(1)", true)]
        [TestCase("'(' <number> ')'", "(2)", true)]
        [TestCase("'(' <number> ')'", "[2]", false)]
        [TestCase("<declaration-value>", "one; two", false)]
        [TestCase("<declaration-value>", "one !important", false)]
        [TestCase("<any-value>", "one; two", true)]
        [TestCase("<unrecognized-type>", "anything", false)]
        [TestCase("<dashed-ident>", "--named", true)]
        [TestCase("<dashed-ident>", "named", false)]
        [TestCase("<url>", "url('image.png')", true)]
        [TestCase("<url>", "url('image.png' junk)", false)]
        public void ValuesDefinitionSyntax(string syntax, string input, bool expected)
        {
            GrammarMatch? match = new GrammarMatcher(CssParser.ParseComponentValueList(input), "").Match(GrammarParser.Parse(syntax));
            Assert.That(match != null, Is.EqualTo(expected), syntax + " :: " + input);
        }

        [Test]
        public void UnorderedValuesSerializeInGrammarOrderAndNestedListsFlatten()
        {
            GrammarMatch match = new GrammarMatcher(CssParser.ParseComponentValueList("b a 1 2"), "").Match(GrammarParser.Parse("a && b && <number>{2}"))!;
            Assert.That(match.Value.Serialize(), Is.EqualTo("a b 1 2"));
            Assert.That(((CssListValue)match.Value).Items.Count, Is.EqualTo(4));
        }

        [TestCase("[ a")]
        [TestCase("<number")]
        [TestCase("a & b")]
        [TestCase("a{3,2}")]
        public void MalformedDefinitionSyntaxFails(string syntax) => Assert.Throws<FormatException>(() => GrammarParser.Parse(syntax));

        [TestCase("rect(1px, 2px, 3px, 4px)", true)]
        [TestCase("rect(auto, 2em, -3px, 0)", true)]
        [TestCase("rect(1%, 2px, 3px, 4px)", false)]
        [TestCase("rect(1px, 2px, 3px)", false)]
        [TestCase("rect(1px, 2px, 3px, 4px round 2px)", false)]
        public void LegacyClipRectangleSelectsScopedLengthOnlyGrammar(string input, bool expected)
        {
            GrammarNode grammar = GrammarParser.Parse("<rect()>");
            GrammarMatch? match = new GrammarMatcher(CssParser.ParseComponentValueList(input), "clip").Match(grammar);
            Assert.That(match != null, Is.EqualTo(expected));
        }

        [Test]
        public void NumericGrammarTargetMetadataSurvivesMathForRegisteredAndNestedValues()
        {
            Assert.That(CssTypeParser.TryParse("integer [1,10]", CssParser.ParseComponentValueList("calc(5 / 2)"), out CssValue? parsed), Is.True);
            var math = (NecronomiDom.Css.Values.Math.CssMathValue)parsed!;
            Assert.That(math.IsIntegerTarget, Is.True);
            Assert.That(math.GrammarMinimum, Is.EqualTo(1));
            Assert.That(math.GrammarMaximum, Is.EqualTo(10));
            Assert.That(CssRegistrationSyntax.TryParseValue("<integer>", CssParser.ParseComponentValueList("calc(5 / 2)"), out CssValue? registered), Is.True);
            Assert.That(((NecronomiDom.Css.Values.Math.CssMathValue)registered!).IsIntegerTarget, Is.True);
        }
    }
}
