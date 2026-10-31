using System;
using System.Collections.Generic;
using System.Linq;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values;
using NUnit.Framework;

namespace NecronomiDom.Tests.CssValues
{
    [TestFixture]
    public sealed class ShorthandTests
    {
        private static readonly Dictionary<string, string> Examples = new(StringComparer.Ordinal)
        {
            ["all"] = "initial", ["-webkit-text-stroke"] = "1px red", ["animation"] = "slide 1s ease-in 2s 3 alternate both paused",
            ["animation-range"] = "entry 10% exit 90%", ["background"] = "url('tile.png') 1px 2px / 3px 4px no-repeat fixed padding-box content-box red",
            ["background-position"] = "top 17px right 18px, 10% 20%", ["border"] = "2px solid red",
            ["border-block"] = "2px solid red", ["border-inline"] = "2px solid red", ["border-top"] = "2px solid red", ["border-right"] = "2px solid red",
            ["border-bottom"] = "2px solid red", ["border-left"] = "2px solid red", ["border-block-start"] = "2px solid red", ["border-block-end"] = "2px solid red",
            ["border-inline-start"] = "2px solid red", ["border-inline-end"] = "2px solid red",
            ["border-width"] = "1px 2px 3px 4px", ["border-style"] = "solid dashed", ["border-color"] = "red blue green",
            ["border-block-width"] = "1px 2px", ["border-inline-width"] = "1px 2px", ["border-block-style"] = "solid dashed", ["border-inline-style"] = "solid dashed",
            ["border-block-color"] = "red blue", ["border-inline-color"] = "red blue", ["border-radius"] = "1px 2px 3px / 4px 5px",
            ["border-block-start-radius"] = "1px 2px / 3px 4px", ["border-block-end-radius"] = "1px 2px / 3px 4px",
            ["border-inline-start-radius"] = "1px 2px / 3px 4px", ["border-inline-end-radius"] = "1px 2px / 3px 4px",
            ["border-image"] = "url('border.png') 30 fill / 2 / 1 round",
            ["container"] = "card / inline-size", ["flex"] = "2 3 10px", ["flex-flow"] = "row-reverse wrap", ["font"] = "italic small-caps bold 12px / 1.5 MyFont, serif",
            ["font-synthesis"] = "weight small-caps", ["font-variant"] = "small-caps tabular-nums slashed-zero",
            ["gap"] = "1px 2px", ["grid"] = "auto-flow dense 20px / 1fr 2fr", ["grid-template"] = "\"a a\" 20px \"b c\" auto / 1fr 2fr",
            ["grid-area"] = "a / b / c / d", ["grid-row"] = "a / span 2", ["grid-column"] = "2 / 4",
            ["inset"] = "1px 2px 3px", ["inset-block"] = "1px auto", ["inset-inline"] = "1px auto", ["list-style"] = "inside square",
            ["margin"] = "1px auto", ["margin-block"] = "1px 2px", ["margin-inline"] = "1px 2px",
            ["mask"] = "url('mask.png') center / cover no-repeat border-box no-clip subtract alpha",
            ["mask-border"] = "url('border.png') 30 / 2 / 1 round luminance", ["outline"] = "1px solid blue",
            ["overflow"] = "hidden scroll", ["overscroll-behavior"] = "contain none", ["padding"] = "1px 2px 3px",
            ["padding-block"] = "1px 2px", ["padding-inline"] = "1px 2px", ["place-content"] = "center start", ["place-items"] = "center start", ["place-self"] = "center start",
            ["position-try"] = "most-width --fallback", ["scroll-margin"] = "1px 2px", ["scroll-margin-block"] = "1px 2px", ["scroll-margin-inline"] = "1px 2px",
            ["scroll-padding"] = "1px auto", ["scroll-padding-block"] = "1px auto", ["scroll-padding-inline"] = "1px auto",
            ["text-box"] = "trim-start cap alphabetic",
            ["vertical-align"] = "last middle 1px",
            ["contain-intrinsic-size"] = "auto 2px 3px",
            ["text-decoration"] = "underline wavy red 2px", ["text-decoration-skip"] = "none", ["text-wrap"] = "nowrap pretty",
            ["transition"] = "opacity 1s ease-in 2s allow-discrete", ["white-space"] = "pre-line",
        };
        public static IEnumerable<TestCaseData> ProfileCases()
        {
            foreach (string name in CssPropertyRegistry.SupportedNames)
                if (CssPropertyRegistry.Get(name)!.IsShorthand)
                    yield return new TestCaseData(name, Examples.TryGetValue(name, out string? sample) ? sample : "").SetName("Shorthand_" + name);
        }

        [TestCaseSource(nameof(ProfileCases))]
        public void EverySupportedShorthandExpandsAndRoundTrips(string property, string input)
        {
            Assert.That(input, Is.Not.Empty, "Missing explicit profile sample: " + property);
            ParsedProperty? parsed = Parse(property, input);
            Assert.That(parsed, Is.Not.Null, property);
            var map = parsed!.Longhands.ToDictionary(p => p.Longhand, p => p.Value, StringComparer.Ordinal);
            Assert.That(map.Count, Is.GreaterThan(0));
            Assert.That(ShorthandSerializer.TrySerialize(property, name => map.GetValueOrDefault(name), out string text), Is.True, property);
            ParsedProperty? again = Parse(property, text);
            Assert.That(again, Is.Not.Null, text);
            Assert.That(again!.Longhands.ToDictionary(p => p.Longhand, p => p.Value.Serialize()),
                Is.EquivalentTo(map.ToDictionary(p => p.Key, p => p.Value.Serialize())));
        }

        // WPT cd1d8aa5bfebc15d1af2af230c7ff95f9e582a4c:
        // css/css-backgrounds/parsing/border-valid.html:14–15; background-position-valid.html:37,39;
        // css/css-transitions/parsing/transition-valid.html:22,26.
        [TestCase("border", "green double thin", "thin double green")]
        [TestCase("background-position", "top 17px right 18px", "right 18px top 17px")]
        [TestCase("background-position", "top left", "left top")]
        [TestCase("transition", "1s -3s cubic-bezier(0, -2, 1, 3) top", "top 1s cubic-bezier(0, -2, 1, 3) -3s")]
        [TestCase("transition", "all 1s", "1s")]
        [TestCase("margin", "1px 2px 1px 2px", "1px 2px")]
        [TestCase("border-radius", "1px 2px 1px 2px / 3px 4px 3px 4px", "1px 2px / 3px 4px")]
        [TestCase("white-space", "preserve nowrap", "pre")]
        [TestCase("flex", "none", "0 0 auto")]
        public void CanonicalShorthandSerialization(string property, string input, string expected)
        {
            var parsed = Parse(property, input)!;
            var canonical = Parse(property, expected)!;
            Assert.That(parsed.Longhands.ToDictionary(p => p.Longhand, p => p.Value.Serialize()),
                Is.EquivalentTo(canonical.Longhands.ToDictionary(p => p.Longhand, p => p.Value.Serialize())), "Canonical spelling must preserve every leaf before serializer invocation.");
            Assert.That(ShorthandSerializer.TrySerialize(property, name => parsed.Longhands.FirstOrDefault(p => p.Longhand == name).Value, out string text), Is.True);
            Assert.That(text, Is.EqualTo(expected));
        }

        [Test]
        public void EllipticalRadiusRetainsTwoIndependentFourSideCycles()
        {
            ParsedProperty parsed = Parse("border-radius", "1px 2px 1px 2px / 3px 4px 3px 4px")!;
            Assert.That(parsed.Longhands.ToDictionary(p => p.Longhand, p => p.Value.Serialize()),
                Is.EquivalentTo(new Dictionary<string, string>
                {
                    ["border-top-left-radius"] = "1px 3px", ["border-top-right-radius"] = "2px 4px",
                    ["border-bottom-right-radius"] = "1px 3px", ["border-bottom-left-radius"] = "2px 4px",
                }));
        }

        [Test]
        public void MissingOrMixedWideLonghandsCannotSerialize()
        {
            Assert.That(ShorthandSerializer.TrySerialize("margin", _ => null, out _), Is.False);
            Assert.That(ShorthandSerializer.TrySerialize("margin", name => name == "margin-top" ? CssWideKeyword.Inherit : new CssDimensionValue(1, "px"), out _), Is.False);
            Assert.That(ShorthandSerializer.TrySerialize("margin", _ => CssWideKeyword.RevertLayer, out string text), Is.True);
            Assert.That(text, Is.EqualTo("revert-layer"));
        }
        private static ParsedProperty? Parse(string name, string value) => CssPropertyParser.Parse(name, CssParser.ParseComponentValueList(value));
    }
}
