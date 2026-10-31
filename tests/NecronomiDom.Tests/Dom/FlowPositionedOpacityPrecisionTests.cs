using System;
using NecronomiDom.Css.Cascade;
using NecronomiDom.Css.Values;
using NecronomiDom.Html;
using NecronomiDom.Layout;
using NecronomiDom.Url;
using NUnit.Framework;

namespace NecronomiDom.Tests.Dom;

public sealed partial class FlowPositionedInputTests
{
    [Test]
    public void StaticNearOpaqueAncestorRetainsItsActualEffectBoundaryDespiteCssomRounding()
    {
        var document = HtmlImporter.Import("""
            <!doctype html><style>
            html,body,div{display:block;margin:0;padding:0;border:0 solid black;font-family:monospace;font-size:16px;line-height:20px}
            #parent{position:relative;width:100px;height:100px}
            #wrapper{opacity:.99999999}
            #absolute{position:absolute;left:0;top:0;width:10px;height:10px}
            </style><div id=parent><div id=wrapper><div id=absolute></div></div></div>
            """, "near-opacity", WebUrl.Parse("res:///near-opacity.html")!).Document;
        var styles = new StyleEngine(document);
        var computed = styles.GetComputedStyle(document.GetElementById("wrapper")!);
        Assert.That(computed.GetPropertyValue("opacity"), Is.EqualTo("1"), "CSSOM serialization remains unchanged");
        Assert.That(((CssNumberValue)computed.GetSpecifiedValue("opacity")!).Value, Is.LessThan(1));
        Assert.That(() => new FlowLayout(styles, new FixedGlyphMetrics(), "monospace").Layout(document, 100, 100),
            Throws.TypeOf<NotSupportedException>().With.Message.Contains("absolute descendant crosses a static opacity/clip-path group"),
            "The explicitly unsupported non-laminar effect must not silently disappear after serialization");
    }
}
