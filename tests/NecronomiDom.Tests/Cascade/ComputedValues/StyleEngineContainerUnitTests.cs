using System.Collections.Generic;
using NecronomiDom.Css.Cascade;
using NecronomiDom.Css.Media;
using NecronomiDom.Dom;
using NUnit.Framework;

namespace NecronomiDom.Tests.Cascade.ComputedValues
{
    [TestFixture]
    public sealed class StyleEngineContainerUnitTests
    {
        [TestCase("cqw", "80px")]
        [TestCase("cqh", "50px")]
        [TestCase("cqi", "80px")]
        [TestCase("cqb", "50px")]
        [TestCase("cqmin", "50px")]
        [TestCase("cqmax", "80px")]
        public void KnownAbsenceUsesConfiguredSmallViewport(string unit, string expected)
        {
            var document = Document();
            var target = Child(document.Body!);
            target.Style.SetProperty("margin-left", "10" + unit);
            Assert.That(Value(target), Is.EqualTo(expected));
        }

        [Test]
        public void SmallViewportPreservesEachSuppliedAxisIndependently()
        {
            var document = Document();
            var target = Child(document.Body!);
            var engine = document.DefaultView.StyleEngine;
            engine.ValueContext = _ => new ComputedValueContext { SmallViewport = new(null, 300) };
            target.Style.SetProperty("margin-left", "10cqw");
            target.Style.SetProperty("margin-top", "10cqh");
            Assert.That(Value(target), Is.EqualTo("80px"));
            Assert.That(Value(target, "margin-top"), Is.EqualTo("30px"));
            engine.ValueContext = _ => new ComputedValueContext { SmallViewport = new(200, null) };
            engine.Invalidate();
            Assert.That(Value(target), Is.EqualTo("20px"));
            Assert.That(Value(target, "margin-top"), Is.EqualTo("50px"));
        }

        [TestCase("vertical-rl")]
        [TestCase("vertical-lr")]
        [TestCase("sideways-rl")]
        [TestCase("sideways-lr")]
        public void UnsupportedWritingModesDoNotChangeHorizontalProfileFallback(string writingMode)
        {
            var document = Document();
            var target = Child(document.Body!);
            target.Style.CssText = "writing-mode:" + writingMode + ";margin-left:10cqi;margin-top:10cqb";
            Assert.That(target.Style.GetPropertyValue("writing-mode"), Is.Empty, "Profile v2.1 Non-goal 6: horizontal writing only");
            Assert.That(Value(target), Is.EqualTo("80px"));
            Assert.That(Value(target, "margin-top"), Is.EqualTo("50px"));
        }


        [Test]
        public void EligibleFartherAncestorPreventsFallbackOnOtherwiseAbsentAxis()
        {
            var document = Document();
            var outer = Child(document.Body!); outer.Style.SetProperty("container-type", "size");
            var inner = Child(outer); inner.Style.SetProperty("container-type", "inline-size");
            var target = Child(inner); target.Style.SetProperty("margin-left", "10cqh");
            Assert.That(Value(target), Is.EqualTo("10cqh"));
            outer.Style.SetProperty("container-type", "normal");
            Assert.That(Value(target), Is.EqualTo("50px"));
        }

        [Test]
        public void UnsupportedWritingModesDoNotChangeHorizontalContainerEligibility()
        {
            var document = Document();
            var container = Child(document.Body!);
            container.Style.CssText = "container-type:inline-size;writing-mode:vertical-rl";
            var target = Child(container);
            target.Style.CssText = "writing-mode:horizontal-tb;margin-left:10cqi;margin-top:10cqb";
            Assert.That(container.Style.GetPropertyValue("writing-mode"), Is.Empty, "Non-goal 6");
            Assert.That(Value(target), Is.EqualTo("10cqi"));
            Assert.That(Value(target, "margin-top"), Is.EqualTo("50px"));
            target.Style.SetProperty("writing-mode", "vertical-lr");
            Assert.That(Value(target), Is.EqualTo("10cqi"));
            Assert.That(Value(target, "margin-top"), Is.EqualTo("50px"));
            container.Style.SetProperty("writing-mode", "horizontal-tb");
            Assert.That(Value(target), Is.EqualTo("10cqi"));
            Assert.That(Value(target, "margin-top"), Is.EqualTo("50px"));
        }

        [TestCase("none")]
        [TestCase("contents")]
        [TestCase("inline")]
        [TestCase("table")]
        [TestCase("table-cell")]
        public void IneligibleDisplayDoesNotEstablishSizeContainment(string display)
        {
            var document = Document();
            var container = Child(document.Body!);
            container.Style.CssText = "container-type:size;display:" + display;
            var target = Child(container); target.Style.SetProperty("margin-left", "10cqw");
            Assert.That(Value(target), Is.EqualTo("80px"));
        }

        [Test]
        public void HostCallbackReceivesOriginalLogicalUnitsAndOverridesOnlyWithActualMetrics()
        {
            var document = Document();
            var container = Child(document.Body!); container.Style.SetProperty("container-type", "inline-size");
            var target = Child(container);
            target.Style.CssText = "writing-mode:vertical-rl;margin-left:10cqi;margin-top:10cqb";
            var units = new List<string>();
            document.DefaultView.StyleEngine.ValueContext = element => ReferenceEquals(element, target)
                ? new ComputedValueContext { ContainerUnitBasis = unit => { units.Add(unit); return unit == "cqb" ? 300 : null; } }
                : default;
            Assert.That(target.Style.GetPropertyValue("writing-mode"), Is.Empty, "Non-goal 6: original horizontal logical-unit callback");
            Assert.That(Value(target), Is.EqualTo("10cqi"));
            Assert.That(Value(target, "margin-top"), Is.EqualTo("30px"));
            Assert.That(units, Does.Contain("cqi"));
            Assert.That(units, Does.Contain("cqb"));
            Assert.That(units, Does.Not.Contain("cqw"));
        }

        [Test]
        public void HostKnownAbsenceAndNullMetricsRemainDistinct()
        {
            var document = Document();
            var container = Child(document.Body!); container.Style.SetProperty("container-type", "size");
            var target = Child(container); target.Style.SetProperty("margin-left", "10cqw");
            var engine = document.DefaultView.StyleEngine;
            engine.ValueContext = _ => new ComputedValueContext { ContainerUnitBasis = unit => null };
            Assert.That(Value(target), Is.EqualTo("10cqw"));
            engine.ValueContext = _ => new ComputedValueContext { NoEligibleContainer = true, ContainerUnitBasis = unit => null };
            engine.Invalidate();
            Assert.That(Value(target), Is.EqualTo("80px"));
        }

        [Test]
        public void ShadowDescendantAndAssignedLightChildUseFlatTreeAncestry()
        {
            var document = Document();
            var host = Child(document.Body!);
            var shadow = host.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Closed });
            var container = Child(shadow); container.Style.SetProperty("container-type", "inline-size");
            var inside = Child(container); inside.Style.SetProperty("margin-left", "10cqw");
            container.AppendChild(document.CreateElement("slot"));
            var assigned = Child(host); assigned.Style.SetProperty("margin-left", "10cqw");
            Assert.That(Value(inside), Is.EqualTo("10cqw"));
            Assert.That(Value(assigned), Is.EqualTo("10cqw"));
            container.Style.SetProperty("container-type", "normal");
            host.Style.SetProperty("container-type", "size");
            Assert.That(Value(inside), Is.EqualTo("10cqw"));
            Assert.That(Value(assigned), Is.EqualTo("10cqw"));
            host.Style.SetProperty("container-type", "normal");
            Assert.That(Value(inside), Is.EqualTo("80px"));
            Assert.That(Value(assigned), Is.EqualTo("80px"));
        }


        [Test]
        public void MinMaxCombineMeasuredAndAbsentAxesButNotUnmeasuredAxes()
        {
            var document = Document();
            var container = Child(document.Body!); container.Style.SetProperty("container-type", "inline-size");
            var target = Child(container);
            target.Style.CssText = "margin-left:10cqmin;margin-top:10cqmax";
            Assert.That(Value(target), Is.EqualTo("10cqmin"));
            Assert.That(Value(target, "margin-top"), Is.EqualTo("10cqmax"));
            document.DefaultView.StyleEngine.ValueContext = element => ReferenceEquals(element, target)
                ? new ComputedValueContext { ContainerUnitBasis = unit => unit == "cqi" ? 300 : null }
                : default;
            document.DefaultView.StyleEngine.Invalidate();
            Assert.That(Value(target), Is.EqualTo("30px"));
            Assert.That(Value(target, "margin-top"), Is.EqualTo("50px"));
        }

        [Test]
        public void OriginatingElementIsContainerForPseudoButNotItself()
        {
            var document = Document();
            var target = Child(document.Body!);
            target.Style.CssText = "container-type:size;margin-left:10cqw";
            Assert.That(Value(target), Is.EqualTo("80px"));
            var style = (HTMLStyleElement)document.CreateElement("style");
            style.TextContent = "div::before { content:'x';margin-left:10cqw }";
            document.Head!.AppendChild(style);
            Assert.That(document.DefaultView.GetComputedStyle(target, "::before").GetPropertyValue("margin-left"), Is.EqualTo("10cqw"));
        }

        [Test]
        public void DefiniteBorderBoxContainerUsesConstrainedContentSizeAndTracksResize()
        {
            var document = Document();
            var container = Child(document.Body!);
            container.Style.CssText = "container-type:size;box-sizing:border-box;width:140px;height:240px;padding:10px;border:10px solid;min-width:150px;max-width:160px";
            var target = Child(container);
            target.Style.CssText = "margin-left:10cqw;margin-top:10cqh";
            var live = document.DefaultView.GetComputedStyle(target);
            Assert.That(live.GetPropertyValue("margin-left"), Is.EqualTo("11px"));
            Assert.That(live.GetPropertyValue("margin-top"), Is.EqualTo("20px"));
            container.Style.SetProperty("width", "300px");
            Assert.That(live.GetPropertyValue("margin-left"), Is.EqualTo("12px"));
            container.Style.SetProperty("min-width", "180px");
            Assert.That(live.GetPropertyValue("margin-left"), Is.EqualTo("14px"), "The minimum wins when it exceeds the maximum.");
        }

        [Test]
        public void LiveNumericFunctionsFollowDefiniteContainerResizeAcrossSignBoundary()
        {
            var document = Document();
            var container = Child(document.Body!);
            container.Style.CssText = "container-type:inline-size;width:1000px";
            var target = Child(container);
            target.Style.SetProperty("opacity", "calc(0.5 + sign(20cqw - 100px) * 0.1)");
            var live = document.DefaultView.GetComputedStyle(target);
            Assert.That(live.GetPropertyValue("opacity"), Is.EqualTo("0.6"));
            container.Style.SetProperty("width", "500px");
            Assert.That(live.GetPropertyValue("opacity"), Is.EqualTo("0.5"));
            container.Style.SetProperty("width", "100px");
            Assert.That(live.GetPropertyValue("opacity"), Is.EqualTo("0.4"));
        }

        private static Document Document()
        {
            var document = new Document().Implementation.CreateHTMLDocument("containers");
            document.DefaultView.StyleEngine.Media = new MediaEnvironment { Width = 800, Height = 500 };
            return document;
        }
        private static HTMLElement Child(Node parent)
        {
            var element = (HTMLElement)parent.OwnerDocument!.CreateElement("div");
            parent.AppendChild(element);
            return element;
        }
        private static string Value(HTMLElement target, string property = "margin-left")
            => target.OwnerDocument!.DefaultView.GetComputedStyle(target).GetPropertyValue(property);
    }
}
