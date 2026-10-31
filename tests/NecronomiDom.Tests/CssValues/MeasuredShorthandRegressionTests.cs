using System.Linq;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values;
using NUnit.Framework;

namespace NecronomiDom.Tests.CssValues
{
    [TestFixture]
    public sealed class MeasuredShorthandRegressionTests
    {
        // Original assertions: WPT cd1d8aa5, css-inline/css-masking/css-grid/css-ui parsing.
        [TestCase("vertical-align", "baseline", "baseline")]
        [TestCase("vertical-align", "0", "baseline")]
        [TestCase("vertical-align", "0px baseline", "baseline")]
        [TestCase("vertical-align", "first baseline", "first")]
        [TestCase("vertical-align", "middle 0", "middle")]
        [TestCase("vertical-align", "last baseline sub", "last sub")]
        [TestCase("vertical-align", "super middle first", "first middle super")]
        [TestCase("vertical-align", "1em last", "last 1em")]
        [TestCase("vertical-align", "text-top first 10%", "first text-top 10%")]
        [TestCase("vertical-align", "calc(20% - 10px)", "calc(20% - 10px)")]
        [TestCase("grid-row", "span 2 / auto", "span 2")]
        [TestCase("grid-column", "span 2", "span 2")]
        [TestCase("grid-area", "span 1 i", "span i")]
        [TestCase("grid-template", "\"a\" [a] [b] \"b\"", "\"a\" [a b] \"b\"")]
        [TestCase("mask", "border-box no-clip", "no-clip")]
        [TestCase("mask", "no-clip", "no-clip")]
        [TestCase("contain-intrinsic-size", "auto 1px auto 1px", "auto 1px")]
        [TestCase("contain-intrinsic-size", "1px auto 1px", "1px auto 1px")]
        [TestCase("contain-intrinsic-size", "none none", "none")]
        [TestCase("mask", "repeat no-repeat", "repeat-x")]
        [TestCase("outline", "dashed thin", "dashed thin")]
        [TestCase("outline", "3px ridge rgba(10, 20, 30, 0.4)", "rgba(10, 20, 30, 0.4) ridge 3px")]
        public void ShorthandCanonicalFormPreservesLeaves(string property, string input, string expected)
        {
            ParsedProperty? parsed = Parse(property, input);
            Assert.That(parsed, Is.Not.Null);
            Assert.That(ShorthandSerializer.TrySerialize(property,
                name => parsed!.Longhands.FirstOrDefault(p => p.Longhand == name).Value, out string text), Is.True);
            Assert.That(text, Is.EqualTo(expected));
            ParsedProperty? roundTrip = Parse(property, text);
            Assert.That(roundTrip, Is.Not.Null);
            Assert.That(roundTrip!.Longhands.ToDictionary(p => p.Longhand, p => p.Value.Serialize()),
                Is.EquivalentTo(parsed!.Longhands.ToDictionary(p => p.Longhand, p => p.Value.Serialize())));
        }

        [TestCase("grid-row-end", "1 -πA", "1 -πA")]
        [TestCase("mask-position", "10%", "10% center")]
        [TestCase("mask-position", "top", "center top")]
        [TestCase("mask-position", "top, center, left", "center top, center center, left center")]
        [TestCase("mask-repeat", "no-repeat repeat", "repeat-y")]
        [TestCase("contain", "layout paint style size", "size layout style paint")]
        [TestCase("contain", "paint inline-size layout", "inline-size layout paint")]
        [TestCase("text-decoration-inset", "0px 0px", "0px")]
        [TestCase("text-decoration-inset", "-1ch -1ch", "-1ch")]
        [TestCase("text-decoration-skip-spaces", "end start", "start end")]
        [TestCase("overflow-clip-margin", "0px content-box", "content-box")]
        [TestCase("overflow-clip-margin", "padding-box", "0px")]
        [TestCase("overflow-clip-margin", "10px padding-box", "10px")]
        [TestCase("overflow-clip-margin", "padding-box calc(100px - 100px)", "calc(0px)")]
        public void LonghandCanonicalFormRoundTrips(string property, string input, string expected)
        {
            ParsedProperty? parsed = Parse(property, input);
            Assert.That(parsed, Is.Not.Null);
            string text = parsed!.Longhands.Single().Value.Serialize();
            Assert.That(text, Is.EqualTo(expected));
            Assert.That(Parse(property, text)!.Longhands.Single().Value.Serialize(), Is.EqualTo(text));
        }

        [TestCase("vertical-align", "first last")]
        [TestCase("vertical-align", "middle alphabetic")]
        [TestCase("vertical-align", "sub super")]
        [TestCase("vertical-align", "auto")]
        [TestCase("grid", "[] [] \"a\"")]
        [TestCase("grid-template", "\"a\" 10px [a] [a]")]
        [TestCase("grid-template", "[a] \"a\" [a] [a]")]
        [TestCase("grid-template-rows", "[one] 10px [two] [three]")]
        [TestCase("grid-template-columns", "repeat(2, [a] [b] 10px)")]
        [TestCase("mask", "margin-box")]
        [TestCase("text-decoration-skip-spaces", "all start")]
        [TestCase("text-decoration-skip-spaces", "start start")]
        public void InvalidPropertyGroupsAreRejected(string property, string input)
            => Assert.That(Parse(property, input), Is.Null);

        private static ParsedProperty? Parse(string property, string text)
            => CssPropertyParser.Parse(property, CssParser.ParseComponentValueList(text));
    }
}
