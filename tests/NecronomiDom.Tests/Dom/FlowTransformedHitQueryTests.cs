using NUnit.Framework;

namespace NecronomiDom.Tests.Dom;

public sealed partial class FlowPositionedInputTests
{
    [TestCase(8, 3, 5, 340, 210, "overlap")]
    [TestCase(-5, 7, 2, 340, 200, "clip")]
    [TestCase(-5, 7, 2, 375, 200, "overlap")]
    public void ActualTransformedPaddingBoundaryIntersectsViewportQueryNotOnlyInversePoint(
        int degrees, int translateX, int translateY, int x, int y, string target)
    {
        var (document, input, display) = Fixture("<div id=frame><div id=clip><div id=fill><div id=overlap></div></div></div></div>", $$"""
            #frame{position:absolute;left:240px;top:200px;width:150px;height:100px;
                transform:translate({{translateX}}px,{{translateY}}px) rotate({{degrees}}deg);transform-origin:center}
            #clip{position:relative;width:130px;height:90px;padding:4px;overflow:hidden;background-color:blue;
                border-style:solid;border-width:3px 7px 9px 5px;border-color:black;
                border-radius:36px 12px 24px 18px / 18px 24px 12px 30px}
            #fill{position:absolute;left:-12px;top:-12px;width:190px;height:130px;background-color:green}
            #overlap{position:absolute;left:55px;top:-8px;width:100px;height:130px;background-color:red}
            """);
        using var owner = input;
        // These are Chrome 155 event/elementFromPoint observations for the same elliptical-corner markup,
        // not rounded-corner tolerance or mathematical point guesses.
        Assert.That(display.HitTest(x, y)?.GetAttribute("id"), Is.EqualTo(target));
        Assert.That(Click(document, input, x, y), Is.EqualTo(target));
    }
}
