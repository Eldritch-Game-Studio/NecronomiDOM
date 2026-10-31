using System;
using NecronomiDom.Url;
using NUnit.Framework;

namespace NecronomiDom.Tests.Url
{
    [TestFixture]
    public sealed class UrlTests
    {
        private static readonly WebUrl Base = WebUrl.Parse("res:///ui/screens/page.html")!;
        [TestCase("../images/icon.png", "res:///ui/images/icon.png", "res://ui/images/icon.png")]
        [TestCase("/fonts/ui.woff2", "res:///fonts/ui.woff2", "res://fonts/ui.woff2")]
        [TestCase("?theme=dark#top", "res:///ui/screens/page.html?theme=dark#top", "res://ui/screens/page.html")]
        [TestCase("a%20b.png", "res:///ui/screens/a%20b.png", "res://ui/screens/a b.png")]
        public void RelativePathsResolveWithEmptyHost(string reference, string href, string resource)
        {
            var result = ResourceUrlPolicy.Default.Resolve(reference, Base, ResourceKind.Image);
            Assert.That(result.Allowed, Is.True); Assert.That(result.Url!.Href, Is.EqualTo(href)); Assert.That(result.ResourcePath, Is.EqualTo(resource));
        }
        [TestCase("../../../escape.png")]
        [TestCase("/%2e%2e/escape.png")]
        [TestCase("res://ui/image.png")]
        [TestCase("user:///outside/file")]
        [TestCase("https://example.org/image.png")]
        [TestCase("http://example.org/")]
        [TestCase("file:///tmp/image.png")]
        [TestCase("a%2fb.png")]
        [TestCase("a%5cb.png")]
        [TestCase("a%00b.png")]
        [TestCase("..\\image.png")]
        public void UnsafeResourceReferencesAreRejected(string reference)
        { var result = ResourceUrlPolicy.Default.Resolve(reference, Base, ResourceKind.Image); Assert.That(result.Allowed, Is.False); Assert.That(result.RejectReason, Is.Not.Empty); }
        [Test]
        public void UserUiAndWptRootsMapWithoutAuthorityConfusion()
        {
            Assert.That(ResourceUrlPolicy.Default.Resolve("user:///ui/avatar.png", Base).ResourcePath, Is.EqualTo("user://ui/avatar.png"));
            Assert.That(ResourceUrlPolicy.WithRoot("res://tests/wpt/").Resolve("/fonts/ahem.css", Base).ResourcePath, Is.EqualTo("res://tests/wpt/fonts/ahem.css"));
            Assert.Throws<ArgumentException>(() => ResourceUrlPolicy.WithRoot("file:///tmp/"));
        }
        [Test]
        public void DataBodiesRespectResourceKindDecodedSizeAndBase64()
        {
            foreach (var kind in new[] { ResourceKind.Image, ResourceKind.Font, ResourceKind.Stylesheet })
                Assert.That(ResourceUrlPolicy.Default.Resolve("data:image/png;base64,YQ==", Base, kind).Allowed, Is.True);
            Assert.That(ResourceUrlPolicy.Default.Resolve("data:text/html,x", Base, ResourceKind.Document).Allowed, Is.False);
            Assert.That(ResourceUrlPolicy.Default.Resolve("data:image/png;base64,!", Base, ResourceKind.Image).Allowed, Is.False);
            var boundary = "data:image/png," + new string('a', ResourceUrlPolicy.MaxDataUrlBytes);
            Assert.That(ResourceUrlPolicy.Default.Resolve(boundary, Base, ResourceKind.Image).Allowed, Is.True);
            Assert.That(ResourceUrlPolicy.Default.Resolve(boundary + "a", Base, ResourceKind.Image).Allowed, Is.False);
        }
        [TestCase("https://EXAMPLE.org:443/a/../b?x#y", "https://example.org/b?x#y")]
        [TestCase("res:///a/%2E%2e/b", "res:///b")]
        [TestCase("data:text/plain,a b#f", "data:text/plain,a b#f")]
        public void UrlRecordsSerializeStandardComponents(string input, string expected)
        { Assert.That(WebUrl.Parse(input)!.Href, Is.EqualTo(expected)); }
    }
}
