using System;
using System.Linq;
using NecronomiDom.Dom;
using NUnit.Framework;

namespace NecronomiDom.Tests.Layout;

public sealed partial class ScrollContainerTests
{
    private static KeyboardEvent Down(string key) => new("keydown", new KeyboardEventInit { Key = key, Bubbles = true, Cancelable = true, Composed = true });
    [Test]
    public void WheelCancellationAndNestedEdgeChainingUseActualClippedTarget()
    {
        using var v = new View("<div id=outer><div id=inner><button id=row></button></div><div id=tail></div></div>",
            "#outer{width:140px;height:100px;overflow:auto}#inner{height:60px;overflow:auto}#row{height:200px;width:100px}#tail{height:100px}");
        var inner = v.E("inner"); var outer = v.E("outer");
        bool canceled = true; bool trusted = false;
        inner.AddEventListener("wheel", new DelegateEventListener(e => { trusted = e.IsTrusted; if (canceled) e.PreventDefault(); }));
        Assert.That(v.Input.Wheel(10,10,0,30), Is.False); Assert.That(inner.ScrollTop, Is.Zero); Assert.That(trusted, Is.True);
        canceled = false; Assert.That(v.Input.Wheel(10,10,0,40), Is.True); Assert.That(inner.ScrollTop, Is.EqualTo(40)); Assert.That(outer.ScrollTop, Is.Zero);
        inner.ScrollTop = 1000; v.Update();
        v.Input.Wheel(10,10,0,40); Assert.That(outer.ScrollTop, Is.EqualTo(40));
    }
    [Test]
    public void KeyboardPageHomeEndAndCanceledKeyPreserveButtonsAndEditingDefaults()
    {
        using var v = new View("<div id=scroller tabindex=0><button id=button></button><input id=text value=12><div id=tail></div></div>",
            "#scroller{width:140px;height:80px;overflow:auto}#button{height:20px}input{display:block;appearance:none;padding:0;border:0;width:100px;height:20px}#tail{height:240px}");
        ((HTMLElement)v.E("scroller")).Focus();
        v.Input.Key(Down("ArrowDown")); Assert.That(v.E("scroller").ScrollTop, Is.EqualTo(40));
        v.Input.Key(Down("PageDown")); Assert.That(v.E("scroller").ScrollTop, Is.EqualTo(110));
        v.Input.Key(Down("End")); Assert.That(v.E("scroller").ScrollTop, Is.EqualTo(200));
        v.Input.Key(Down("Home")); Assert.That(v.E("scroller").ScrollTop, Is.Zero);
        v.E("scroller").AddEventListener("keydown", new DelegateEventListener(e => { if (((KeyboardEvent)e).Key == "ArrowDown") e.PreventDefault(); }));
        Assert.That(v.Input.Key(Down("ArrowDown")), Is.False); Assert.That(v.E("scroller").ScrollTop, Is.Zero);
        var text = (HTMLInputElement)v.E("text"); text.Focus(); text.SetSelectionRange(0,0);
        v.Input.Key(Down("End")); Assert.That(text.SelectionStart, Is.EqualTo(2)); Assert.That(v.E("scroller").ScrollTop, Is.Zero);
        ((HTMLElement)v.E("button")).Focus(); v.Input.Key(Down(" ")); Assert.That(v.E("scroller").ScrollTop, Is.Zero);
    }
    [Test]
    public void FocusCentersOnlyWhenNeededPreventScrollAndExplicitNearestAreHonored()
    {
        using var v = new View("<div id=scroller><button id=a></button><div id=gap></div><button id=b></button><div id=tail></div></div>",
            "#scroller{height:80px;width:120px;overflow:auto}button{height:20px;width:100px}#gap{height:100px}#tail{height:100px}");
        var b = (HTMLElement)v.E("b");
        b.Focus(new FocusOptions { PreventScroll = true }); Assert.That(v.E("scroller").ScrollTop, Is.Zero);
        b.Blur(); b.Focus(); Assert.That(v.E("scroller").ScrollTop, Is.EqualTo(90));
        v.Update(); b.Blur(); b.Focus(); Assert.That(v.E("scroller").ScrollTop, Is.EqualTo(90), "Already-visible focus must not recenter.");
        v.E("scroller").ScrollTop = 0; b.ScrollIntoView(new ScrollIntoViewOptions { Block = "nearest" });
        Assert.That(v.E("scroller").ScrollTop, Is.EqualTo(60), "Nearest must account for offsets changed since the last layout.");
        b.ScrollIntoView(new ScrollIntoViewOptions { Block = "start" }); Assert.That(v.E("scroller").ScrollTop, Is.EqualTo(120));
        Assert.Throws<ArgumentException>(() => b.ScrollIntoView(new ScrollIntoViewOptions { Block = "unknown" }));
    }
    [TestCase(80, 0, 20)]
    [TestCase(200, 0, 170)]
    [TestCase(200, 220, 200)]
    [TestCase(200, 400, 170)]
    public void FocusUsesChromePartialNearestAndOutsideCenter(double gap, double offset, double expected)
    {
        using var v = new View("<div id=scroller><div id=gap></div><button id=target></button><div id=tail></div></div>",
            $"#scroller{{height:100px;width:220px;overflow:auto}}#gap{{height:{gap}px}}button{{height:40px;width:180px}}#tail{{height:600px}}");
        v.E("scroller").ScrollTop = offset;
        ((HTMLElement)v.E("target")).Focus();
        Assert.That(v.E("scroller").ScrollTop, Is.EqualTo(expected));
    }
    [Test]
    public void FocusCentersFullyOutsideInlineTargetAsWellAsBlockTarget()
    {
        using var v = new View("<div id=scroller><button id=target></button><div id=tail></div></div>",
            "#scroller{height:80px;width:100px;overflow:auto}#target{margin-left:200px;width:40px;height:20px}#tail{width:800px;height:200px}");
        ((HTMLElement)v.E("target")).Focus();
        Assert.That(v.E("scroller").ScrollLeft, Is.EqualTo(170));
    }
    [Test]
    public void NestedFocusScrollsEveryAncestorAndGamepadReachesOffViewportRows()
    {
        using var v = new View("<div id=outer><div id=spacer></div><div id=inner><button id=a></button><div id=gap></div><button id=b></button><div id=tail></div></div><div id=end></div></div>",
            "#outer{height:100px;width:120px;overflow:auto}#spacer{height:220px}#inner{height:80px;overflow:auto}button{height:20px;width:100px}#gap{height:400px}#tail{height:80px}#end{height:200px}");
        ((HTMLElement)v.E("a")).Focus(); v.Update();
        Assert.That(v.Input.Navigate("down"), Is.True);
        Assert.That(v.Doc.FocusedElement?.Id, Is.EqualTo("b"));
        Assert.That(v.E("inner").ScrollTop, Is.EqualTo(390)); Assert.That(v.E("outer").ScrollTop, Is.GreaterThan(0));
        v.Update();
        Assert.That(v.Display.HitTest(20,80)?.Id, Is.EqualTo("b"));
    }
    [Test]
    public void ScrollEventsAreTrustedCoalescedNonbubblingAndSafeAfterDetach()
    {
        using var v = new View("<div id=scroller><div id=content></div></div>", "#scroller{height:80px;overflow:auto}#content{height:300px}");
        int count = 0, parentCount = 0;
        v.E("scroller").AddEventListener("scroll", new DelegateEventListener(e => { Assert.That(e.IsTrusted, Is.True); Assert.That(e.Cancelable, Is.False); count++; }));
        v.Doc.Body!.AddEventListener("scroll", new DelegateEventListener(_ => parentCount++));
        v.E("scroller").ScrollTop = 10; v.E("scroller").ScrollTop = 20; v.E("scroller").ScrollTop = 20;
        Assert.That(count, Is.Zero); v.Input.DispatchScrollEvents(); Assert.That((count,parentCount), Is.EqualTo((1,0)));
        v.E("scroller").ScrollTop = 30; v.E("scroller").Remove(); v.Input.DispatchScrollEvents(); Assert.That(count, Is.EqualTo(1));
    }
    [Test]
    public void AuthoredScrollbarDragAndTrackPageDoNotActivateUnderlyingContent()
    {
        using var v = new View("<div id=scroller><button id=row></button></div>",
            "#scroller{height:80px;width:120px;overflow:auto;scrollbar-width:thin;--necronomidom-scrollbar-size:8px;--necronomidom-scrollbar-thumb:rgba(214,170,88,.22)}#row{height:320px;width:100px}");
        int clicks = 0; v.E("row").AddEventListener("click", new DelegateEventListener(_ => clicks++));
        var bar = v.Display.Scrollbars.Single(); Assert.That(bar.Track.Width, Is.EqualTo(8));
        double x = bar.Thumb.X + 2, y = bar.Thumb.Y + 2;
        v.Input.PointerButton(x,y,true); v.Input.PointerMove(x,y+20,1); v.Input.PointerButton(x,y+20,false);
        Assert.That(v.E("scroller").ScrollTop, Is.EqualTo(80)); Assert.That(clicks, Is.Zero);
        v.Update(); v.Input.PointerButton(116,70,true); v.Input.PointerButton(116,70,false);
        Assert.That(v.E("scroller").ScrollTop, Is.EqualTo(150)); Assert.That(clicks, Is.Zero);
        v.Input.ReleaseInput();
    }
}
