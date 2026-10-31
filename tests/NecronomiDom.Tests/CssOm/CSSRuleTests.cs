using System;
using System.Collections.Generic;
using NecronomiDom.Css.Om;
using NecronomiDom.Dom;
using NUnit.Framework;

namespace NecronomiDom.Tests.CssOm
{
    [TestFixture]
    public sealed class CSSRuleTests
    {
        [TestCase("a{color:red}", "a { color: red; }")]
        [TestCase("a{}", "a { }")]
        [TestCase("@layer a,b.c;", "@layer a, b.c;")]
        [TestCase("@namespace svg 'urn:svg';", "@namespace svg url(\"urn:svg\");")]
        [TestCase("@import 'a.css' layer(foo) supports(display:grid) screen;", "@import url(\"a.css\") layer(foo) supports(display: grid) screen;")]
        [TestCase("@custom-media --small (width<10px);", "@custom-media --small (width < 10px);")]
        [TestCase("@position-try --left { top: 1px; }", "@position-try --left { top: 1px; }")]
        [TestCase("@property --size {syntax:'<length>';inherits:false;initial-value:1px}", "@property --size { syntax: \"<length>\"; inherits: false; initial-value: 1px; }")]
        public void RuleCssTextUsesCssomCanonicalFormat(string source, string expected)
        { var sheet = CSSStyleSheet.Parse(source); Assert.That(sheet.CssRules.Length, Is.EqualTo(1)); Assert.That(sheet.CssRules[0].CssText, Is.EqualTo(expected)); }

        [TestCase("@media (color) { a{} }", typeof(CSSMediaRule))]
        [TestCase("@supports (display:grid) {a{}}", typeof(CSSSupportsRule))]
        [TestCase("@layer widgets {a{}}", typeof(CSSLayerBlockRule))]
        [TestCase("@scope (.root) to (.stop) {a{}}", typeof(CSSScopeRule))]
        [TestCase("@container sidebar (width>10px) {a{}}", typeof(CSSContainerRule))]
        [TestCase("@starting-style {a{opacity:0}}", typeof(CSSStartingStyleRule))]
        [TestCase("@keyframes foo {from{opacity:0}to{opacity:1}}", typeof(CSSKeyframesRule))]
        [TestCase("@font-face {font-family:foo;src:url(font.woff2)}", typeof(CSSFontFaceRule))]
        [TestCase("@counter-style foo {system:cyclic;symbols:'x';}", typeof(CSSCounterStyleRule))]
        public void NamedRulesExposeTheCorrectCssomClass(string source, Type expected)
        { Assert.That(CSSStyleSheet.Parse(source).CssRules[0], Is.TypeOf(expected)); }

        [TestCase("@unknown foo {a{}}")]
        [TestCase("@font-face extra {font-family:a}")]
        [TestCase("@media {a{}}")]
        [TestCase("@layer a,b {a{}}")]
        [TestCase("@layer a.;")]
        [TestCase("@scope .root {a{}}")]
        [TestCase("@container none (width>1px) {a{}}")]
        [TestCase("@starting-style foo {a{}}")]
        [TestCase("@property foo {syntax:'*';inherits:false}")]
        [TestCase("@property --x {syntax:'<length>';inherits:false}")]
        [TestCase("@property --x {syntax:'<length>';inherits:false;initial-value:1em}")]
        [TestCase("@property --x {syntax:'<length>';inherits:false;initial-value:red}")]
        [TestCase("@property --x {syntax:'<unknown>';inherits:false;initial-value:1px}")]
        [TestCase("@position-try foo {top:1px}")]
        [TestCase("a:unknown {color:red}")]
        public void InvalidRulesAreDroppedAndReported(string source)
        { var diagnostics = new List<WebDiagnostic>(); var sheet = CSSStyleSheet.Parse(source, "bad.css", diagnostics: diagnostics); Assert.That(sheet.CssRules.Length, Is.Zero); Assert.That(diagnostics, Is.Not.Empty); }

        [TestCase("@media (width >) {a{}}", typeof(CSSMediaRule), "(width >)")]
        [TestCase("@media and {a{}}", typeof(CSSMediaRule), "not all")]
        [TestCase("@supports (color red) {a{}}", typeof(CSSSupportsRule), "(color red)")]
        [TestCase("@supports not ((display: grid) and (color red)) {a{}}", typeof(CSSSupportsRule), "not ((display: grid) and (color red))")]
        [TestCase("@container (width >) {a{}}", typeof(CSSContainerRule), "(width >)")]
        public void ConditionalRecoveryReportsWarningsWithoutDroppingRules(string source, Type type, string conditionText)
        {
            var diagnostics = new List<WebDiagnostic>();
            var sheet = CSSStyleSheet.Parse("/* preceding line */\n" + source, "conditions.css", diagnostics: diagnostics);
            Assert.That(sheet.CssRules.Length, Is.EqualTo(1));
            var rule = sheet.CssRules[0];
            Assert.That(rule, Is.TypeOf(type));
            Assert.That(((CSSGroupingRule)rule).CssRules.Length, Is.EqualTo(1));
            var actual = rule switch
            {
                CSSMediaRule media => media.ConditionText,
                CSSSupportsRule supports => supports.ConditionText,
                CSSContainerRule container => container.ConditionText,
                _ => throw new InvalidOperationException()
            };
            Assert.That(actual, Is.EqualTo(conditionText));
            Assert.That(diagnostics, Is.Not.Empty);
            foreach (var diagnostic in diagnostics)
            {
                Assert.That(diagnostic.File, Is.EqualTo("conditions.css"));
                Assert.That(diagnostic.Line, Is.EqualTo(2));
                Assert.That(diagnostic.Column, Is.GreaterThan(0));
                Assert.That(diagnostic.Severity, Is.EqualTo(WebDiagnosticSeverity.Warning));
            }
        }

        [Test]
        public void NestedDeclarationRunsKeepImplicitParentAndSourceOrder()
        {
            var sheet = CSSStyleSheet.Parse("a {color:red; &:hover {color:blue} color:green; @media (color) { color:black; > b {width:1px} color:white } }");
            var style = (CSSStyleRule)sheet.CssRules[0];
            Assert.That(style.Style.GetPropertyValue("color"), Is.EqualTo("red"));
            Assert.That(style.CssRules.Length, Is.EqualTo(3));
            Assert.That(style.CssRules[0], Is.TypeOf<CSSStyleRule>());
            Assert.That(style.CssRules[1], Is.TypeOf<CSSNestedDeclarations>());
            Assert.That(((CSSNestedDeclarations)style.CssRules[1]).Style.GetPropertyValue("color"), Is.EqualTo("green"));
            var media = (CSSMediaRule)style.CssRules[2];
            Assert.That(media.CssRules.Length, Is.EqualTo(3));
            Assert.That(media.CssRules[0], Is.TypeOf<CSSNestedDeclarations>());
            Assert.That(media.CssRules[1].ParentRule, Is.SameAs(media));
            Assert.That(media.CssRules[2].ParentStyleSheet, Is.SameAs(sheet));
        }
        [Test]
        public void ImportAndNamespaceOrderingIsEnforced()
        {
            var sheet = CSSStyleSheet.Parse("@layer reset; @import 'a.css'; @namespace svg 'urn:svg'; svg|a {} @import 'b.css'; @namespace bad 'urn:bad';");
            Assert.That(sheet.CssRules.Length, Is.EqualTo(4));
            Assert.That(((CSSStyleRule)sheet.CssRules[3]).SelectorText, Is.EqualTo("svg|a"));
            var ex = Assert.Throws<DomException>(() => sheet.InsertRule("@import 'late.css';", 4));
            Assert.That(ex!.Name, Is.EqualTo("HierarchyRequestError"));
            Assert.That(Assert.Throws<DomException>(() => sheet.InsertRule("@namespace x 'urn:x';", 0))!.Name, Is.EqualTo("InvalidStateError"));
        }
        [Test]
        public void CssomInsertDeleteOwnsLiveListsAndDetachesRules()
        {
            var sheet = new CSSStyleSheet();
            var list = sheet.CssRules;
            sheet.InsertRule("@media (color) { a{color:red} }");
            var group = (CSSMediaRule)list[0];
            var child = group.CssRules[0];
            Assert.That(child.ParentRule, Is.SameAs(group));
            Assert.That(child.ParentStyleSheet, Is.SameAs(sheet));
            var revision = sheet.Revision;
            ((CSSStyleRule)child).Style.SetProperty("color", "blue");
            Assert.That(sheet.Revision, Is.GreaterThan(revision));
            group.InsertRule("b{}", 1);
            group.DeleteRule(0);
            Assert.That(child.ParentRule, Is.Null);
            Assert.That(child.ParentStyleSheet, Is.Null);
            sheet.DeleteRule(0);
            Assert.That(list.Length, Is.Zero);
            Assert.That(group.ParentStyleSheet, Is.Null);
        }
        [Test]
        public void ConstructedReplacementOmitsImportsAndParsedSheetsRejectReplacement()
        {
            var sheet = new CSSStyleSheet();
            sheet.ReplaceSync("@import 'a.css'; a{color:red}");
            Assert.That(sheet.CssRules.Length, Is.EqualTo(1));
            Assert.That(Assert.Throws<DomException>(() => sheet.InsertRule("@import 'a.css';"))!.Name, Is.EqualTo("SyntaxError"));
            Assert.That(Assert.Throws<DomException>(() => CSSStyleSheet.Parse("a{}").ReplaceSync("b{}"))!.Name, Is.EqualTo("NotAllowedError"));
            Assert.That(Assert.Throws<DomException>(() => sheet.InsertRule("a{} b{}"))!.Name, Is.EqualTo("SyntaxError"));
            Assert.That(Assert.Throws<DomException>(() => sheet.DeleteRule(10))!.Name, Is.EqualTo("IndexSizeError"));
        }
        [Test]
        public void KeyframesIgnoreImportanceValidateKeysAndFindLastDuplicate()
        {
            var sheet = CSSStyleSheet.Parse("@keyframes fade {from{opacity:0!important;color:red} 0%{opacity:.2} 120%{opacity:1} to{opacity:1}}");
            var keyframes = (CSSKeyframesRule)sheet.CssRules[0];
            Assert.That(keyframes.CssRules.Length, Is.EqualTo(3));
            Assert.That(keyframes.FindRule("from")!.Style.GetPropertyValue("opacity"), Is.EqualTo("0.2"));
            keyframes.AppendRule("50% {opacity:.5}");
            Assert.That(keyframes.FindRule("50%"), Is.Not.Null);
            keyframes.DeleteRule("from");
            Assert.That(keyframes.FindRule("from")!.Style.GetPropertyValue("opacity"), Is.Empty);
            Assert.That(Assert.Throws<DomException>(() => keyframes.FindRule("to")!.KeyText = "bad")!.Name, Is.EqualTo("SyntaxError"));
        }
        [Test]
        public void PropertyRegistrationCanUseUniversalSyntaxWithoutAnInitialValue()
        {
            var registration = (CSSPropertyRule)CSSStyleSheet.Parse("@property --tokens {syntax:'*';inherits:true}").CssRules[0];
            Assert.That(registration.Syntax, Is.EqualTo("*"));
            Assert.That(registration.Inherits, Is.True);
            Assert.That(registration.InitialValue, Is.Empty);
        }

        [Test]
        public void PositionTryExpandsAndSerializesAllowedPropertyShorthands()
        {
            var sheet = CSSStyleSheet.Parse("@position-try --fit {margin:1px 2px;inset:3px;place-self:start end;color:red;--x:1px;margin-top:4px!important}");
            var style = ((CSSPositionTryRule)sheet.CssRules[0]).Style;
            Assert.That(style.Length, Is.EqualTo(10));
            Assert.That(style.GetPropertyValue("margin-top"), Is.EqualTo("1px"));
            Assert.That(style.GetPropertyValue("margin-right"), Is.EqualTo("2px"));
            Assert.That(style.GetPropertyValue("margin"), Is.EqualTo("1px 2px"));
            Assert.That(style.GetPropertyValue("inset"), Is.EqualTo("3px"));
            Assert.That(style.GetPropertyValue("align-self"), Is.EqualTo("start"));
            Assert.That(style.GetPropertyValue("justify-self"), Is.EqualTo("end"));
            Assert.That(style.GetPropertyValue("color"), Is.Empty);
            Assert.That(style.GetPropertyValue("--x"), Is.Empty);
            var revision = sheet.Revision;
            style.SetProperty("margin", "5px");
            Assert.That(sheet.Revision, Is.GreaterThan(revision));
            Assert.That(style.GetPropertyValue("margin-left"), Is.EqualTo("5px"));
            Assert.That(style.CssText, Does.Contain("margin: 5px;"));
            Assert.That(style.RemoveProperty("inset"), Is.EqualTo("3px"));
            Assert.That(style.GetPropertyValue("top"), Is.Empty);
            Assert.That(style.Length, Is.EqualTo(6));
            style.SetProperty("opacity", "0.5");
            Assert.That(style.GetPropertyValue("opacity"), Is.Empty);
            style.CssText = "inset: 1px 2px; color: blue;";
            Assert.That(style.Length, Is.EqualTo(4));
            Assert.That(style.GetPropertyValue("inset"), Is.EqualTo("1px 2px"));
            Assert.That(style.GetPropertyValue("margin"), Is.Empty);
        }
    }
}
