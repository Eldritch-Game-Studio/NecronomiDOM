using System;
using System.Collections.Generic;
using NecronomiDom.Css.Cascade;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values;
using NecronomiDom.Dom;
using NecronomiDom.Html;
using NecronomiDom.Url;
using NUnit.Framework;

namespace NecronomiDom.Tests.Cascade.ComputedValues
{
    [TestFixture]
    public sealed class ComputedPropertyTests
    {
        internal static CssValue Parse(string property, string specified)
        {
            ParsedProperty parsed = ParseDeclaration(property, specified);
            foreach ((string name, CssValue value) in parsed.Longhands) if (name == (CssPropertyRegistry.Get(property)?.Name ?? property)) return value;
            throw new InvalidOperationException("Test requires a parsed longhand.");
        }

        private static ParsedProperty ParseDeclaration(string property, string specified)
        {
            ParsedProperty? parsed = CssPropertyParser.Parse(property, CssParser.ParseComponentValueList(specified));
            Assert.That(parsed, Is.Not.Null, property + ": " + specified);
            return parsed!;
        }

        internal static string Resolve(string property, string specified, ComputedValueContext context)
        {
            ParsedProperty parsed = ParseDeclaration(property, specified);
            var values = new Dictionary<string, CssValue>(StringComparer.Ordinal);
            foreach ((string name, CssValue value) in parsed.Longhands)
                values[name] = ComputedValueResolver.Resolve(name, value, context);
            PropertyDefinition definition = CssPropertyRegistry.Get(property)!;
            if (!definition.IsShorthand) return values[definition.Name].Serialize();
            bool serialized = ShorthandSerializer.TrySerialize(definition.Name,
                name => values.TryGetValue(name, out CssValue? value) ? value : null, out string result);
            Assert.That(serialized, Is.True, "Computed shorthand serialization: " + property);
            return result;
        }

        // Direct WPT cases: https://github.com/web-platform-tests/wpt/tree/master/css/css-fonts/parsing
        [TestCase("font-weight", "normal", "400")]
        [TestCase("font-weight", "bold", "700")]
        [TestCase("font-weight", "bolder", "700")]
        [TestCase("font-weight", "lighter", "100")]
        [TestCase("font-weight", "calc(1200)", "1000")]
        [TestCase("font-width", "condensed", "75%")]
        [TestCase("font-size", "medium", "16px")]
        [TestCase("font-size", "large", "18px")]
        [TestCase("font-size", "150%", "24px")]
        [TestCase("font-size", "calc(150% + 2px)", "26px")]
        [TestCase("font-style", "oblique", "oblique")]
        [TestCase("font-style", "oblique 0deg", "normal")]
        [TestCase("font-style", "oblique 10grad", "oblique 9deg")]
        [TestCase("font-style", "oblique calc(100deg)", "oblique 90deg")]
        [TestCase("font-style", "oblique calc(-100deg)", "oblique -90deg")]
        [TestCase("font-size-adjust", "ex-height 0.5", "0.5")]
        [TestCase("font-size-adjust", "cap-height calc(-0.5)", "cap-height 0")]
        [TestCase("font-feature-settings", "\"tnum\", \"hist\"", "\"hist\", \"tnum\"")]
        [TestCase("font-feature-settings", "\"dlig\", \"smcp\", \"dlig\" 0", "\"dlig\" 0, \"smcp\"")]
        [TestCase("font-variation-settings", "\"wght\" 100, \"wdth\" 200, \"wght\" 300, \"wdth\" 400", "\"wdth\" 400, \"wght\" 300")]
        [TestCase("font-family", "\"New Century Schoolbook\", serif", "New Century Schoolbook, serif")]
        [TestCase("font-family", "\"inherit\", \"serif\"", "\"inherit\", \"serif\"")]
        // Direct WPT: css/css-backgrounds/parsing/{background-repeat,border-image-outset,border-image-repeat}-computed.html
        [TestCase("background-repeat", "repeat-x", "repeat-x")]
        [TestCase("background-repeat", "repeat repeat", "repeat")]
        [TestCase("background-repeat", "repeat space", "repeat space")]
        [TestCase("border-image-repeat", "round", "round")]
        [TestCase("border-image-outset", "1px", "1px")]
        [TestCase("border-image-outset", "1px 2 3px 4", "1px 2 3px 4")]
        [TestCase("background-size", "2em", "40px auto")]
        [TestCase("border-spacing", "2em", "40px 40px")]
        [TestCase("background-position", "right bottom", "100% 100%")]
        [TestCase("background-position", "right 2em bottom 10%", "calc(100% - 40px) 90%")]
        [TestCase("transform-origin", "top left 3em", "0% 0% 60px")]
        [TestCase("opacity", "150%", "1")]
        [TestCase("opacity", "calc(-2)", "0")]
        [TestCase("order", "calc(1.5)", "2")]
        [TestCase("padding-left", "calc(1px - 2px)", "0px")]
        [TestCase("margin-left", "calc(1px - 2px)", "-1px")]
        [TestCase("transition-delay", "calc(100ms + 0.2s)", "0.3s")]
        [TestCase("contain", "strict", "strict")]
        [TestCase("filter", "brightness(50%) blur(1em)", "brightness(0.5) blur(20px)")]
        public void StandardsAndWptCases(string property, string specified, string expected)
        {
            Assert.That(Resolve(property, specified, ComputedUnitTests.Context), Is.EqualTo(expected));
        }

        [Test]
        public void CrossPropertyComputationUsesContextRatherThanLayout()
        {
            var context = ComputedUnitTests.Context with
            {
                PropertyValue = property => property switch
                {
                    "overflow-y" => new CssKeywordValue("scroll"), "border-left-style" => new CssKeywordValue("none"),
                    "position" => new CssKeywordValue("absolute"), _ => null
                }
            };
            Assert.That(ComputedValueResolver.Resolve("overflow-x", Parse("overflow-x", "visible"), context).Serialize(), Is.EqualTo("auto"));
            Assert.That(ComputedValueResolver.Resolve("border-left-width", Parse("border-left-width", "thick"), context).Serialize(), Is.EqualTo("0px"));
            Assert.That(ComputedValueResolver.Resolve("display", Parse("display", "inline-flex"), context).Serialize(), Is.EqualTo("flex"));
        }

        [Test]
        public void InvalidResolverStagesAreExplicit()
        {
            Assert.Throws<ArgumentException>(() => ComputedValueResolver.Resolve("not-a-property", new CssKeywordValue("auto"), default));
            Assert.Throws<ArgumentException>(() => ComputedValueResolver.Resolve("margin", new CssDimensionValue(1, "px"), default));
            Assert.Throws<ArgumentException>(() => ComputedValueResolver.Resolve("width", CssWideKeyword.Inherit, default));
        }

        [Test]
        public void SystemFontsRequireActualPlatformData()
        {
            var specified = new CssSystemFontValue("caption", "font-size");
            Assert.That(ComputedValueResolver.Resolve("font-size", specified, default), Is.SameAs(specified));
            var context = ComputedUnitTests.Context with { SystemFont = (keyword, longhand) => keyword == "caption" && longhand == "font-size" ? new CssDimensionValue(12, "px") : null };
            Assert.That(ComputedValueResolver.Resolve("font-size", specified, context).Serialize(), Is.EqualTo("12px"));
        }

        [Test]
        public void RegisteredCustomValuesNormalizeWithoutRewritingUnregisteredTokens()
        {
            var tokens = new CssUnparsedValue(CssParser.ParseComponentValueList("2em"));
            Assert.That(ComputedValueResolver.Resolve("--raw", tokens, ComputedUnitTests.Context), Is.SameAs(tokens));
            Assert.That(ComputedValueResolver.Resolve("--length", new CssDimensionValue(2, "em"), ComputedUnitTests.Context).Serialize(), Is.EqualTo("40px"));
        }

        [Test]
        public void ResourceDerivedFontValuesRequireProviderMetrics()
        {
            var context = ComputedUnitTests.Context with { MathFontSizeScale = 0.5 };
            Assert.That(ComputedValueResolver.Resolve("font-size", new CssKeywordValue("math"), context).Serialize(), Is.EqualTo("8px"));
            Assert.That(ComputedValueResolver.Resolve("font-size-adjust", Parse("font-size-adjust", "ic-height from-font"), context).Serialize(), Is.EqualTo("ic-height 1"));
        }

        [Test]
        public void CachedLonghandMetadataStillSelectsPhysicalWideKeywordAndVariableFallbacks()
        {
            // CSS Cascade 5 §7 explicit defaulting and CSS Logical Properties 1 mapping.
            // Metadata may be shared; parent values, direction, custom variables and computed units may not.
            var doc = HtmlImporter.Import("<style>#p{font-size:20px;line-height:1.5;color:navy;margin-left:7px;margin-right:11px}" +
                "#c{margin-inline-start:inherit;margin-inline-end:unset;padding-inline-start:initial;" +
                "font-size:unset;line-height:inherit;color:inherit;width:var(--missing)}</style>" +
                "<div id=p><div id=c></div></div>", "metadata", WebUrl.Parse("res:///metadata.html")!).Document;
            var styles = new StyleEngine(doc);
            var parent = (HTMLElement)doc.GetElementById("p")!;
            var child = doc.GetElementById("c")!;
            foreach (string direction in new[] { "ltr", "rtl", "ltr" })
            {
                parent.Style.SetProperty("direction", direction);
                var values = styles.GetComputedStyle(child);
                string start = direction == "ltr" ? "left" : "right", end = direction == "ltr" ? "right" : "left";
                Assert.That(values.GetPropertyValue("margin-" + start), Is.EqualTo(direction == "ltr" ? "7px" : "11px"));
                Assert.That(values.GetPropertyValue("margin-" + end), Is.EqualTo("0px"));
                Assert.That(values.GetPropertyValue("padding-" + start), Is.EqualTo("0px"));
                Assert.That(values.GetPropertyValue("font-size"), Is.EqualTo("20px"));
                Assert.That(values.GetPropertyValue("line-height"), Is.EqualTo("30px"));
                Assert.That(styles.GetComputedValues(child).GetPropertyValue("line-height"), Is.EqualTo("1.5"));
                Assert.That(values.GetPropertyValue("color"), Is.EqualTo("rgb(0, 0, 128)"));
                Assert.That(values.GetPropertyValue("width"), Is.EqualTo("auto"));
                parent.Style.SetProperty("--missing", "13px");
                Assert.That(values.GetPropertyValue("width"), Is.EqualTo("13px"));
                parent.Style.RemoveProperty("--missing");
            }
        }

        [Test]
        public void StableRuleInputReuseReselectsRemovedDeclarationsAndKeepsEveryComputedPropertyLive()
        {
            // CSS Cascade 5 defaulting/inheritance; every property must equal a full, uncached resolution.
            const string html = "<style>html{font-size:16px}#p{--paint:red;color:navy;font-size:20px;padding-right:13px}" +
                "#c{background-color:var(--paint);width:var(--width);margin-inline-start:inherit}" +
                ".changed{color:green}</style><div id=p><div id=c></div></div>";
            var doc = HtmlImporter.Import(html, "reuse", WebUrl.Parse("res:///reuse.html")!).Document;
            var control = HtmlImporter.Import(html, "reuse", WebUrl.Parse("res:///reuse.html")!).Document;
            var styles = new StyleEngine(doc);
            var reference = new StyleEngine(control) { IncrementalReconciliation = false };
            Action<Document>[] steps = [
                d => Child(d).Style.SetProperty("width", "17px"),
                d => { Parent(d).Style.SetProperty("--paint", "blue"); Child(d).Style.RemoveProperty("width"); },
                d => { Child(d).Style.SetProperty("margin-left", "11px"); Parent(d).Style.SetProperty("direction", "rtl"); },
                d => { Child(d).Style.RemoveProperty("margin-left"); Parent(d).Style.SetProperty("--paint", "green"); },
                d => { Child(d).Style.SetProperty("font-size", "30px"); Parent(d).Style.SetProperty("color", "purple"); },
                d => Child(d).Style.SetProperty("padding-inline-start", "inherit"),
                d => { Child(d).Style.RemoveProperty("padding-inline-start"); Parent(d).Style.SetProperty("direction", "ltr"); },
                d => Parent(d).Style.SetProperty("--width", "23px"),
                d => Parent(d).Style.RemoveProperty("--width"),
                d => Child(d).ClassList.Add("changed"),
                d => { Child(d).ClassList.Remove("changed"); Parent(d).Style.SetProperty("--paint", "blue"); },
                d => Child(d).AttributeStyleMap.Set("width", new NecronomiDom.Css.TypedOm.CSSUnitValue(31, "px")),
                d => { Child(d).AttributeStyleMap.Set("width", new NecronomiDom.Css.TypedOm.CSSUnitValue(41, "px")); Parent(d).Style.SetProperty("--paint", "red"); },
            ];
            CompareAll();
            foreach (var step in steps)
            {
                step(doc); step(control); reference.Invalidate(); CompareAll();
            }
            // Prove that this skips default-selection work, not merely an equivalent full pass.
            const System.Reflection.BindingFlags fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            object entries = typeof(StyleEngine).GetField("_entries", fields)!.GetValue(styles)!;
            object?[] lookup = [Child(doc), null];
            Assert.That(entries.GetType().GetMethod("TryGetValue")!.Invoke(entries, lookup), Is.True);
            object set = lookup[1]!;
            object entry = set.GetType().GetField("Normal", fields)!.GetValue(set)!;
            object cache = entry.GetType().GetField("ComputedReuse", fields)!.GetValue(entry)!;
            var selected = (bool[])cache.GetType().GetField("SpecifiedSelected", fields)!.GetValue(cache)!;
            int count = 0;
            foreach (bool flag in selected) if (flag) count++;
            TestContext.WriteLine("Warm specified inputs selected: " + count + " / " + selected.Length);
            Assert.That(count, Is.InRange(1, selected.Length - 1));
            static HTMLElement Parent(Document d) => (HTMLElement)d.GetElementById("p")!;
            static HTMLElement Child(Document d) => (HTMLElement)d.GetElementById("c")!;
            void CompareAll()
            {
                foreach (var node in doc.QuerySelectorAll("[id]"))
                {
                    var element = (Element)node;
                    var other = control.GetElementById(element.Id)!;
                    foreach (bool computed in new[] { false, true })
                    {
                        var actual = computed ? styles.GetComputedValues(element) : styles.GetComputedStyle(element);
                        var expected = computed ? reference.GetComputedValues(other) : reference.GetComputedStyle(other);
                        Assert.That(actual.Length, Is.EqualTo(expected.Length));
                        for (int i = 0; i < expected.Length; i++)
                        {
                            string name = expected.Item(i);
                            Assert.That(actual.Item(i), Is.EqualTo(name), element.Id + ":order");
                            Assert.That(actual.GetPropertyValue(name), Is.EqualTo(expected.GetPropertyValue(name)), element.Id + ":" + name);
                        }
                    }
                }
            }
        }
    }
}
