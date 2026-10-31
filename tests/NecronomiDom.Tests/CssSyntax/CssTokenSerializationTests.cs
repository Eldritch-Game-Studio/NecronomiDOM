using System.Linq;
using NecronomiDom.Css.Syntax;
using NUnit.Framework;

namespace NecronomiDom.Tests.CssSyntax
{
    [TestFixture]
    public sealed class CssTokenSerializationTests
    {
        [TestCase("a/**/b")]
        [TestCase("a/**/(b)")]
        [TestCase("1/**/px")]
        [TestCase("1/**/%")]
        [TestCase("-/**/n")]
        [TestCase("+/**/1")]
        [TestCase("#/**/name")]
        [TestCase("@/**/media")]
        [TestCase("//**/*")]
        public void SerializationPreservesTokenKindsAndPayloadsAcrossDangerousBoundaries(string source)
        {
            var original = CssTokenizer.Tokenize(source);
            var serialized = CssTokenSerializer.Serialize(CssParser.ParseComponentValueList(source));
            var result = CssTokenizer.Tokenize(serialized);
            Assert.That(result.Select(t => (t.Kind, t.Value, t.Number, t.Unit, t.IsIdHash)), Is.EqualTo(original.Select(t => (t.Kind, t.Value, t.Number, t.Unit, t.IsIdHash))));
        }
        [Test]
        public void BadUrlSerializationStaysABadUrlRatherThanAValidFunction()
        {
            var token = CssTokenizer.Tokenize("url(a b)").Single();
            Assert.That(token.Kind, Is.EqualTo(CssTokenKind.BadUrl));
            Assert.That(CssTokenizer.Tokenize(CssTokenSerializer.Serialize(token)).Single().Kind, Is.EqualTo(CssTokenKind.BadUrl));
        }
        [Test]
        public void EscapedDimensionUnitsCannotBecomeNumericExponents()
        {
            var token = new CssToken(CssTokenKind.Dimension, Number: 1, Unit: "e-2", Representation: "1");
            var reparsed = CssTokenizer.Tokenize(CssTokenSerializer.Serialize(token)).Single();
            Assert.That(reparsed.Kind, Is.EqualTo(CssTokenKind.Dimension));
            Assert.That(reparsed.Number, Is.EqualTo(1));
            Assert.That(reparsed.Unit, Is.EqualTo("e-2"));
        }
    }
}
