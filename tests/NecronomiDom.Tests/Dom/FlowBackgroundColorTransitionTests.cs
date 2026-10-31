using System.Linq;
using NecronomiDom.Css.Cascade;
using NecronomiDom.Dom;
using NecronomiDom.Html;
using NecronomiDom.Layout;
using NecronomiDom.Paint;
using NecronomiDom.Url;
using NUnit.Framework;

namespace NecronomiDom.Tests.Dom;

public sealed class FlowBackgroundColorTransitionTests
{
    private sealed class Metrics : ITextShaper
    { public TextMetrics Measure(string text, int size, double fontWeight = 400, double letterSpacing = 0, bool italic = false) => new(0, 12, 4); }

    [TestCase("")]
    [TestCase("border-radius:7px;border:2px solid blue")]
    [TestCase("border-radius:7px 12px 3px 9px;border:2px solid blue")]
    [TestCase("background-image:linear-gradient(transparent,transparent)")]
    public void SampledBackgroundReachesActualPaintWithoutChangingComputedStyleOrGeometry(string decoration)
    {
        var document = HtmlImporter.Import("<!doctype html><style>html,body,div{display:block;margin:0;padding:0;" +
            "font-family:monospace;font-size:16px;line-height:20px}#target{width:80px;height:40px;background-color:black;" +
            "transition:background-color .12s linear;" + decoration + "}.entered{background-color:white!important}</style><div id=target></div>",
            "background-paint", WebUrl.Parse("res:///background-paint.html")!).Document;
        var styles = new StyleEngine(document); var controller = new BackgroundColorTransitionController();
        var flow = new FlowLayout(styles, new Metrics(), "monospace", animatedBackgroundColor: controller);
        var target = document.GetElementById("target")!;
        controller.UpdateStyle(target, styles.GetComputedStyle(target), 0);
        var initial = flow.Layout(document, 200, 100);
        var bounds = initial.Boxes.Single(b => ReferenceEquals(b.Element, target)).Bounds;
        target.ClassList.Add("entered"); controller.UpdateStyle(target, styles.GetComputedStyle(target), 0); controller.Sample(.06);
        var current = flow.Layout(document, 200, 100);
        var background = current.Runs.Select(run => run switch
        {
            BoxRun box when box.Bounds == bounds => (Rgba?)box.Style.Background,
            BorderBoxRun box when box.Bounds == bounds => box.Background,
            _ => null
        }).First(color => color is { A: > 0 });
        Assert.That(background, Is.EqualTo(new Rgba(.5f, .5f, .5f, 1)));
        Assert.That(BackgroundColorTransitionStyle.Resolve(styles.GetComputedStyle(target)).Color, Is.EqualTo(new Rgba(1, 1, 1, 1)));
        Assert.That(current.Boxes.Single(b => ReferenceEquals(b.Element, target)).Bounds, Is.EqualTo(bounds));
        controller.Sample(.12); var final = flow.Layout(document, 200, 100);
        Assert.That(final.Runs.OfType<BoxRun>().Any(r => r.Bounds == bounds && r.Style.Background.R == 1)
            || final.Runs.OfType<BorderBoxRun>().Any(r => r.Bounds == bounds && r.Background.R == 1), Is.True);
    }
}
