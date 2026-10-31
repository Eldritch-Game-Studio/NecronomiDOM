using System;
using NecronomiDom.Css.Cascade;
using NecronomiDom.Html;
using NecronomiDom.Layout;
using NecronomiDom.Paint;
using NecronomiDom.Url;
using NUnit.Framework;

namespace NecronomiDom.Tests.Layout;

[TestFixture]
public sealed class FlowFlexWrapTests
{
    private sealed class Metrics : ITextShaper
    {
        public TextMetrics Measure(string text, int fontSize, double fontWeight = 400, double letterSpacing = 0, bool italic = false) => new(text.Length * 8, 12, 4);
    }
    [TestCase("column", "wrap")]
    [TestCase("row", "wrap-reverse")]
    public void UnsupportedWrappingRemainsExplicit(string direction, string wrapping)
    {
        var document = HtmlImporter.Import($"<!doctype html><div style='display:flex;flex-direction:{direction};flex-wrap:{wrapping}'></div>", "unsupported", WebUrl.Parse("res:///unsupported.html")!).Document;
        Assert.Throws<NotSupportedException>(() => new FlowLayout(new StyleEngine(document), new Metrics(), "serif").Layout(document, 400, 300));
    }
}
