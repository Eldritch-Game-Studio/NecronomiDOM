using System.Linq;
using System.Text.Json;
using NecronomiDom.Css.Cascade;
using NecronomiDom.Html;
using NecronomiDom.Layout;
using NecronomiDom.Paint;
using NecronomiDom.Url;
using NUnit.Framework;

namespace NecronomiDom.Tests;

/// <summary>Paint runs reference live DOM owners; serializing them directly fails with a JSON object cycle,
/// so <see cref="PaintRunJson"/> writes plain values.</summary>
public sealed class PaintRunJsonTests
{
    private sealed class FontMetrics : ITextShaper
    {
        public TextMetrics Measure(string text, int fontSize, double fontWeight = 400, double letterSpacing = 0, bool italic = false) =>
            new(text.Length * 6, 11, 4);
    }

    [Test]
    public void DomAttributedRunsSerializeAsPlainValues()
    {
        var document = HtmlImporter.Import("<!doctype html><style>" +
            "html,body,div{display:block;margin:0;padding:0;border:0 solid black;box-sizing:content-box;font-family:monospace;font-size:10px;line-height:normal;appearance:none}" +
            "html,body{width:100%;height:100%;background-color:white}#line{width:100px;background-color:#ffc000}" +
            "</style><div id=line class=x title=y>owned text</div>",
            "paint-json", WebUrl.Parse("res:///paint-json.html")!).Document;
        var display = new FlowLayout(new StyleEngine(document), new FontMetrics(), "monospace").Layout(document, 400, 300);
        var text = display.Runs.OfType<TextRun>().First();
        Assert.That(text.Owner, Is.Not.Null);
        Assert.Throws<JsonException>(() => JsonSerializer.SerializeToElement(text, text.GetType()));

        var values = display.Runs.Select(PaintRunJson.ToElement).ToArray();
        var json = values[display.Runs.IndexOf(text)];
        Assert.That(json.GetProperty("Owner").GetString(), Is.EqualTo("line"));
        Assert.That(json.GetProperty("Text").GetString(), Is.EqualTo(text.Text));
        Assert.That(json.GetProperty("X").GetDouble(), Is.EqualTo(text.X));
    }
}
