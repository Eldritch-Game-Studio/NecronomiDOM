using System;
using System.Collections.Generic;
using NecronomiDom.Css.Cascade;
using NecronomiDom.Css.Selectors;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values;
using NUnit.Framework;

namespace NecronomiDom.Tests.Cascade
{
    [TestFixture]
    public sealed class CascadeCoreTests
    {
        [Test]
        public void ImportantOriginsReverseAndLayersReverse()
        {
            var author = Priority(CascadeOrigin.Author, false, 4);
            var user = Priority(CascadeOrigin.User, false, 4);
            Assert.That(author.CompareTo(user), Is.GreaterThan(0));
            var authorImportant = Priority(CascadeOrigin.Author, true, 4);
            var userImportant = Priority(CascadeOrigin.User, true, 4);
            Assert.That(userImportant.CompareTo(authorImportant), Is.GreaterThan(0));
            Assert.That(Priority(CascadeOrigin.Author, true, 0).CompareTo(authorImportant), Is.GreaterThan(0));
            Assert.That(Priority(CascadeOrigin.Author, false, int.MaxValue).CompareTo(author), Is.GreaterThan(0));
        }
        [Test]
        public void ScopeProximityBreaksSpecificityTieBeforeOrder()
        {
            var outer = Priority(CascadeOrigin.Author, false, 0) with { ScopeDistance = 3, Order = 9 };
            var inner = outer with { ScopeDistance = 1, Order = 1 };
            Assert.That(inner.CompareTo(outer), Is.GreaterThan(0));
        }
        [Test]
        public void FallbackReferencesParticipateInCyclesEvenWhenNotUsed()
        {
            var resolver = Variables(("--a", "var(--b, var(--a))"), ("--b", "10px"));
            Assert.That(resolver.Resolve("--a"), Is.Null);
            Assert.That(CssTokenSerializer.Serialize(resolver.Substitute(Parse("var(--a, 2px)"))!), Is.EqualTo(" 2px"));
        }
        [Test]
        public void EmptyFallbackIsValidAndMissingWithoutFallbackIsInvalid()
        {
            var resolver = Variables();
            Assert.That(resolver.Substitute(Parse("var(--missing,)")), Is.Empty);
            Assert.That(resolver.Substitute(Parse("var(--missing)")), Is.Null);
        }
        [Test]
        public void CyclesInvalidateTheirMembersButDependentFallbackCanRecover()
        {
            var resolver = Variables(("--a", "var(--b)"), ("--b", "var(--a)"), ("--c", "var(--a, red)"));
            Assert.That(resolver.Resolve("--a"), Is.Null);
            Assert.That(resolver.Resolve("--b"), Is.Null);
            Assert.That(CssTokenSerializer.Serialize(resolver.Resolve("--c")!), Is.EqualTo(" red"));
        }
        [Test]
        public void CustomPropertyNamesAreCaseSensitive()
        {
            var resolver = Variables(("--A", "red"), ("--a", "blue"));
            Assert.That(CssTokenSerializer.Serialize(resolver.Resolve("--A")!), Is.EqualTo("red"));
            Assert.That(CssTokenSerializer.Serialize(resolver.Resolve("--a")!), Is.EqualTo("blue"));
        }
        [Test]
        public void UnchangedNestedTokensAndDirectVariablesKeepImmutableIdentity()
        {
            var tokens = Parse("calc(1px + (2px)) rgb(10 20 30)");
            var data = new Dictionary<string, IReadOnlyList<ComponentValue>> { ["--paint"] = tokens };
            var resolver = new CustomPropertyResolver(data);
            Assert.That(resolver.Resolve("--paint"), Is.SameAs(tokens));
            Assert.That(resolver.Substitute(Parse("var(--paint)")), Is.SameAs(tokens));
            var reference = new CustomPropertyResolver(data, retainTokens: false);
            var copied = reference.Resolve("--paint")!;
            Assert.That(copied, Is.Not.SameAs(tokens));
            Assert.That(CssTokenSerializer.Serialize(copied), Is.EqualTo(CssTokenSerializer.Serialize(tokens)));
        }
        [Test]
        public void ReusedScratchClearsCachedResolutionCyclesAndAttributeTaint()
        {
            var data = new Dictionary<string, IReadOnlyList<ComponentValue>> {
                ["--a"] = Parse("attr(data-value)"), ["--b"] = Parse("10px") };
            var replacement = Parse("red");
            var resolver = new CustomPropertyResolver(data, _ => replacement);
            Assert.That(resolver.Substitute(Parse("var(--a)"), out bool tainted), Is.SameAs(replacement));
            Assert.That(tainted, Is.True);
            data["--a"] = Parse("var(--b)"); data["--b"] = Parse("var(--a)");
            resolver.Reset();
            Assert.That(resolver.Resolve("--a"), Is.Null);
            Assert.That(resolver.Resolve("--b"), Is.Null);
            data["--b"] = Parse("20px"); resolver.Reset();
            var resolved = resolver.Substitute(Parse("var(--a)"), out tainted);
            Assert.That(CssTokenSerializer.Serialize(resolved!), Is.EqualTo("20px"));
            Assert.That(tainted, Is.False);
        }
        [Test]
        public void ComputedInheritanceDoesNotHideLocalFallbackCycles()
        {
            var inherited = Parse("rgb(10 20 30)");
            var data = new Dictionary<string, IReadOnlyList<ComponentValue>> {
                ["--parent"] = inherited, ["--local"] = Parse("var(--parent, var(--local))") };
            var passed = new Dictionary<string, CssValue> { ["--parent"] = new CssUnparsedValue(inherited) };
            var resolver = new CustomPropertyResolver(data, computedInherited: passed);
            Assert.That(resolver.Resolve("--parent"), Is.SameAs(inherited));
            Assert.That(resolver.Resolve("--local"), Is.Null);
        }
        [Test]
        public void WarmResolverScratchAndUnchangedTokensAllocateNoManagedBuffers()
        {
            var tokens = Parse("calc(1px + (2px)) rgb(10 20 30)");
            var data = new Dictionary<string, IReadOnlyList<ComponentValue>> { ["--paint"] = tokens };
            var resolver = new CustomPropertyResolver(data);
            for (int i = 0; i < 100; i++) { resolver.Reset(); resolver.Resolve("--paint"); }
            long allocated = AllocationWindow.Measure(1000, _ => { resolver.Reset(); resolver.Resolve("--paint"); });
            Assert.That(allocated, Is.Zero);
        }
        private static CascadePriority Priority(CascadeOrigin origin, bool important, int layer)
            => new(origin, important, layer, false, default, int.MaxValue, 0);
        private static IReadOnlyList<ComponentValue> Parse(string value) => CssParser.ParseComponentValueList(value);
        private static CustomPropertyResolver Variables(params (string Name, string Value)[] values)
        {
            var data = new Dictionary<string, IReadOnlyList<ComponentValue>>();
            foreach (var value in values) data.Add(value.Name, Parse(value.Value));
            return new CustomPropertyResolver(data);
        }
    }
}
