using System.Collections.Generic;
using System.Linq;
using NecronomiDom.Css.Media;
using NecronomiDom.Css.Om;
using NecronomiDom.Css.Selectors;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values;
using NUnit.Framework;

namespace NecronomiDom.Tests.CssOm
{
    [TestFixture]
    public sealed class CSSAdvancedSyntaxTests
    {
        [Test]
        public void CounterSemanticInvalidityIsDistinctFromCssomSyntaxValidity()
        {
            var sheet = CSSStyleSheet.Parse("@counter-style one {system:numeric;symbols:x} @counter-style missing {} @counter-style inherited {system:extends decimal;symbols:x}");
            Assert.That(sheet.CssRules.Length, Is.EqualTo(3));
            foreach (var rule in sheet.CssRules) Assert.That(((CSSCounterStyleRule)rule).IsValidCounterStyle, Is.False);
            Assert.That(CSSStyleSheet.Parse("@counter-style decimal {symbols:a b} @counter-style disc {symbols:x}").CssRules.Length, Is.Zero);
        }
        [Test]
        public void CounterAttributeSettersCannotChangeAlgorithmOrInvalidateTheCounter()
        {
            var rule = (CSSCounterStyleRule)CSSStyleSheet.Parse("@counter-style foo {system:numeric;symbols:a b}").CssRules[0];
            rule.Symbols = "x";
            Assert.That(rule.Symbols, Is.EqualTo("a b"));
            rule.System = "cyclic";
            Assert.That(rule.System, Is.EqualTo("numeric"));
            rule.Name = "decimal";
            Assert.That(rule.Name, Is.EqualTo("foo"));
            rule.Name = "ARMENIAN";
            Assert.That(rule.Name, Is.EqualTo("armenian"));
            rule.Name = "with space";
            Assert.That(rule.Name, Is.EqualTo("with\\ space"));
        }
        [Test]
        public void CssRuleTextSetterIsLegacyIgnoredButSelectorTextCanMutate()
        {
            var rule = (CSSStyleRule)CSSStyleSheet.Parse("a {width:1px}").CssRules[0];
            var text = rule.CssText;
            rule.CssText = "b{height:2px}";
            Assert.That(rule.CssText, Is.EqualTo(text));
            rule.SelectorText = "b";
            Assert.That(rule.SelectorText, Is.EqualTo("b"));
            rule.SelectorText = ":unknown";
            Assert.That(rule.SelectorText, Is.EqualTo("b"));
        }
        [Test]
        public void NestedRelativeSelectorsAreAbsolutizedWithoutDoubleParentInsertion()
        {
            var style = (CSSStyleRule)CSSStyleSheet.Parse("a {b{} > c{} &:hover{} :is(&,.foo){} }").CssRules[0];
            Assert.That(style.CssRules.Cast<CSSStyleRule>().Select(rule => rule.SelectorText), Is.EqualTo(new[] { "& b", "& > c", "&:hover", ":is(&, .foo)" }));
            style.InsertRule("height: 3px; color: red", 0);
            Assert.That(style.CssRules[0], Is.TypeOf<CSSNestedDeclarations>());
            Assert.That(((CSSNestedDeclarations)style.CssRules[0]).Style.Length, Is.EqualTo(2));
        }
        [Test]
        public void EscapedLayerDotsRemainDifferentFromHierarchicalDots()
        {
            var sheet = CSSStyleSheet.Parse("@layer a\\.b, a . b; @layer a\\.b {} @import 'x.css' layer(a\\.b);");
            Assert.That(((CSSLayerStatementRule)sheet.CssRules[0]).NameList, Is.EqualTo(new[] { "a\\.b", "a.b" }));
            Assert.That(((CSSLayerBlockRule)sheet.CssRules[1]).Name, Is.EqualTo("a\\.b"));
            Assert.That(sheet.CssRules[0].CssText, Is.EqualTo("@layer a\\.b, a.b;"));
            Assert.That(sheet.CssRules.Length, Is.EqualTo(2));
        }
        [Test]
        public void PhysicalLogicalOverridesAreMovedAfterConflictingMappings()
        {
            var style = CSSStyleDeclaration.Parse("margin-left:1px; margin-inline-start:2px; margin-left:3px");
            Assert.That(style.EnumerateLonghands().Select(d => d.Name), Is.EqualTo(new[] { "margin-inline-start", "margin-left" }));
            style.SetProperty("margin-inline-start", "4px");
            Assert.That(style.Item(1), Is.EqualTo("margin-inline-start"));
            var mixed = CSSStyleDeclaration.Parse("margin-top:1px; margin-inline-start:2px; margin-right:1px; margin-bottom:1px; margin-left:1px");
            Assert.That(mixed.CssText, Does.Not.StartWith("margin:"));
        }
        [Test]
        public void BeforeChangeRunsOnceOnlyForRealStringMutations()
        {
            var style = CSSStyleDeclaration.Parse("width:1px");
            var before = new List<string>();
            var changes = 0;
            style.BeforeChange = () => before.Add(style.GetPropertyValue("width"));
            style.Changed = () => changes++;
            style.SetProperty("width", "nonsense");
            style.SetProperty("width", "1px");
            Assert.That(before, Is.Empty);
            style.SetProperty("width", "2px");
            Assert.That(before, Is.EqualTo(new[] { "1px" }));
            style.SetProperty("margin", "1px");
            Assert.That(changes, Is.EqualTo(2));
            Assert.That(before.Count, Is.EqualTo(2));
            style.CssText = style.CssText;
            Assert.That(changes, Is.EqualTo(2));
            style.RemoveProperty("margin");
            Assert.That(before.Count, Is.EqualTo(3));
        }
        [Test]
        public void TypedRefreshReusesPersistentDeclarationRecord()
        {
            var style = new CSSStyleDeclaration();
            var value = new CssNumberValue(.5, false);
            style.SetTypedProperty("opacity", value);
            var declaration = style.Declarations[0];
            var changes = 0;
            style.Changed = () => changes++;
            style.SetTypedProperty("opacity", value);
            Assert.That(style.Declarations[0], Is.SameAs(declaration));
            Assert.That(style.GetSpecifiedValue("opacity"), Is.SameAs(value));
            Assert.That(changes, Is.EqualTo(1));
        }
        [Test]
        public void CommentSeparatedTokensStillMatchConditionAndArgumentGrammars()
        {
            Assert.That(MediaQueryParser.Parse("screen/**/and/**/(color)").Serialize(), Is.EqualTo("screen and (color)"));
            Assert.That(SupportsParser.Parse("(display:grid)and/**/(width:1px)"), Is.TypeOf<JunctionCondition>());
            Assert.That(SelectorParser.ParseSelectorList("[x=\"y\"i]::part(foo/**/bar)")!.Serialize(), Is.EqualTo("[x=\"y\" i]::part(foo bar)"));
            Assert.That(SelectorParser.ParseSelectorList(":nth-child(2n/**/of.a)")!.Serialize(), Is.EqualTo(":nth-child(2n of .a)"));
            Assert.That(CssParser.ParseDeclaration("--bad: f(url(a b))"), Is.Null);
        }
        [Test]
        public void SupportsSelectorUsesTheStylesheetNamespaceBindings()
        {
            var sheet = CSSStyleSheet.Parse("@namespace svg 'urn:svg'; @supports selector(svg|a) { svg|a {} }");
            Assert.That(((CSSSupportsRule)sheet.CssRules[1]).Condition, Is.TypeOf<SelectorCondition>());
        }
    }
}
