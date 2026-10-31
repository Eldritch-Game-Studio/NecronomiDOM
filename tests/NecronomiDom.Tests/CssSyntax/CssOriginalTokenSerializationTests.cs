using System.Linq;
using System.Text;
using NecronomiDom.Css.Syntax;
using NUnit.Framework;

namespace NecronomiDom.Tests.CssSyntax
{
    [TestFixture]
    public sealed class CssOriginalTokenSerializationTests
    {
        [TestCase("'fallback'", "\"fallback\"")]
        [TestCase("'can\\'t'", "\"can't\"")]
        [TestCase("'\\66 allback'", "\"fallback\"")]
        [TestCase("'foo\\\nbar'", "\"foobar\"")]
        public void StringSourceIsCapturedWithoutChangingCanonicalTokenSerialization(string source, string canonical)
        {
            var token = CssTokenizer.Tokenize(source).Single();
            Assert.That(token.Kind, Is.EqualTo(CssTokenKind.String));
            Assert.That(token.Representation, Is.EqualTo(source));
            Assert.That(CssTokenSerializer.Serialize(token), Is.EqualTo(canonical));
            var values = CssParser.ParseComponentValueList(source);
            Assert.That(CssTokenSerializer.SerializeOriginal(values), Is.EqualTo(source));
            Assert.That(CssTokenSerializer.Serialize(values), Is.EqualTo(canonical));
        }

        [TestCase("attr(data-label type(<string>), 'fallback')", "attr(data-label type(<string>), \"fallback\")")]
        [TestCase("var(--x, ['a', ('b')])", "var(--x, [\"a\", (\"b\")])")]
        public void OriginalContextRecursesThroughFunctionsAndBlocksWithTheSharedSerializer(string source, string canonical)
        {
            var values = CssParser.ParseComponentValueList(source);
            Assert.That(CssTokenSerializer.SerializeOriginal(values), Is.EqualTo(source));
            Assert.That(CssTokenSerializer.Serialize(values), Is.EqualTo(canonical));
            var result = new StringBuilder();
            CssTokenSerializer.AppendOriginal(result, values);
            Assert.That(result.ToString(), Is.EqualTo(source));
        }

        [Test]
        public void OriginalContextStillSeparatesTokensThatWouldOtherwiseMerge()
        {
            var first = CssParser.ParseComponentValueList("lem");
            var second = CssParser.ParseComponentValueList("on 'x'");
            var values = first.Concat(second).ToArray();
            Assert.That(CssTokenSerializer.SerializeOriginal(values), Is.EqualTo("lem/**/on 'x'"));
        }

        [Test]
        public void EofRecoveredStringsSerializeAsCompleteTokensRatherThanSwallowingTheirContainer()
        {
            var values = CssParser.ParseComponentValueList("var(--x,'unterminated");
            Assert.That(CssTokenSerializer.SerializeOriginal(values), Is.EqualTo("var(--x,\"unterminated\")"));
        }
    }
}
