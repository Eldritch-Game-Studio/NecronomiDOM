using System;
using NecronomiDom.Css.Om;
using NUnit.Framework;

namespace NecronomiDom.Tests.CssOm
{
    /// <summary>Inline style writes repeated every frame (positions, transforms, opacity) must not re-parse unchanged text.</summary>
    [TestFixture]
    public sealed class SetPropertyAllocationTests
    {
        private static long Allocated(Action write, int count)
        {
            for (int i = 0; i < 50; i++) write();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < count; i++) write();
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        [Test]
        public void RewritingEqualTextAllocatesNothing()
        {
            var style = new CSSStyleDeclaration();
            int changes = 0;
            style.Changed = () => changes++;
            style.SetProperty("transform", "translate(12.5px, 34.25px)");
            style.SetProperty("left", "4px");
            style.SetProperty("margin", "1px 2px");
            changes = 0;
            long bytes = Allocated(() =>
            {
                style.SetProperty("transform", "translate(12.5px, 34.25px)");
                style.SetProperty("left", "4px");
                style.SetProperty("margin", "1px 2px");
            }, 1000);
            Assert.That(bytes, Is.LessThan(256), "3,000 unchanged writes re-parsed or re-stored their values.");
            Assert.That(changes, Is.Zero, "An unchanged write is not a change.");
            Assert.That(style.GetPropertyValue("transform"), Is.EqualTo("translate(12.5px, 34.25px)"));
            Assert.That(style.GetPropertyValue("margin-left"), Is.EqualTo("2px"));
        }

        [Test]
        public void ChangingTransformWritesStayUnderAFractionOfTheFullGrammarCost()
        {
            // Matching translate() used to try every other transform function first: about 25 KB per write in Release
            // and 50 KB in this Debug test build, against about 12 KB and 17.5 KB now.
            var style = new CSSStyleDeclaration();
            int frame = 0;
            long bytes = Allocated(() => style.SetProperty("transform",
                "translate(" + (frame++ * 0.37).ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "px, 3px)"), 200);
            Assert.That(bytes / 200, Is.LessThan(25_000));
            Assert.That(style.GetPropertyValue("transform"), Does.StartWith("translate("));
        }

        [Test]
        public void CachedParsesKeepPerDeclarationSemantics()
        {
            var first = new CSSStyleDeclaration();
            var second = new CSSStyleDeclaration();
            int secondChanges = 0;
            second.Changed = () => secondChanges++;
            first.SetProperty("transform", "TRANSLATE(1px, 2px) rotate(3deg)");
            second.SetProperty("transform", "TRANSLATE(1px, 2px) rotate(3deg)");
            Assert.That(second.GetPropertyValue("transform"), Is.EqualTo(first.GetPropertyValue("transform")));
            Assert.That(secondChanges, Is.EqualTo(1), "A cached parse still changes a declaration that lacked the value.");

            second.SetProperty("transform", "TRANSLATE(1px, 2px) rotate(3deg)", "important");
            Assert.That(second.GetPropertyPriority("transform"), Is.EqualTo("important"));
            Assert.That(secondChanges, Is.EqualTo(2), "Priority is part of the stored declaration, not the cached parse.");
            second.SetProperty("transform", "TRANSLATE(1px, 2px) rotate(3deg)");
            Assert.That(second.GetPropertyPriority("transform"), Is.Empty);

            for (int attempt = 0; attempt < 2; attempt++)
            {
                second.SetProperty("transform", "translate(nonsense)");
                second.SetProperty("transform", "translate(1px) !important");
                Assert.That(second.GetPropertyValue("transform"), Is.EqualTo(first.GetPropertyValue("transform")), "Rejected text stays rejected when cached.");
            }

            second.SetProperty("margin", "1px 2px");
            first.SetProperty("margin-left", "9px");
            first.SetProperty("margin", "1px 2px");
            Assert.That(first.GetPropertyValue("margin-left"), Is.EqualTo("2px"), "A cached shorthand still expands into every longhand.");
            Assert.That(second.GetPropertyValue("margin"), Is.EqualTo("1px 2px"));
        }

        [Test]
        public void FunctionAlternativesStillMatchInAnyOrderAndCase()
        {
            var style = new CSSStyleDeclaration();
            style.SetProperty("transform", "matrix(1, 0, 0, 1, 5, 6) SCALE(2) translateY(3px) skewX(4deg)");
            Assert.That(style.GetPropertyValue("transform"), Is.EqualTo("matrix(1, 0, 0, 1, 5, 6) scale(2) translateY(3px) skewX(4deg)"));
            style.SetProperty("transform", "translate(1px) frobnicate(2px)");
            Assert.That(style.GetPropertyValue("transform"), Does.StartWith("matrix("), "An unknown function still rejects the whole list.");
            style.SetProperty("clip-path", "polygon(0 0, 100% 0, 0 100%)");
            Assert.That(style.GetPropertyValue("clip-path"), Is.EqualTo("polygon(0px 0px, 100% 0px, 0px 100%)"));
            style.SetProperty("background-image", "linear-gradient(red, blue)");
            Assert.That(style.GetPropertyValue("background-image"), Is.EqualTo("linear-gradient(red, blue)"));
        }
    }
}
