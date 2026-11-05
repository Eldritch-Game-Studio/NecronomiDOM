using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Godot;
using NecronomiDom.Runtime;

namespace NecronomiDomDemo;

/// <summary>A linear-composition document draws each triangle/quad clip-path whose clip equals its own fill as one
/// fused polygon, without an isolation SubViewport, and keeps it through moving geometry. The default sRGB document
/// keeps the general isolated path. With a drawing display, the fused interior must match an equal plain box within
/// one 8-bit step and pixels outside the polygon edge stay background.</summary>
public partial class SingleFillSmoke : Godot.Node
{
    private const int Polygons = 6;
    private int _failures;
    private string _directory = "";

    public override async void _Ready()
    {
        try { await Run(); }
        catch (Exception error) { _failures++; GD.PushError(error.ToString()); }
        finally
        {
            if (_directory.Length > 0 && Directory.Exists(_directory)) Directory.Delete(_directory, true);
            GD.Print("SINGLE_FILL_SMOKE_RESULT " + (_failures == 0 ? "PASS" : "FAIL"));
            GetTree().Quit(_failures == 0 ? 0 : 1);
        }
    }

    private void Check(bool condition, string label)
    {
        if (!condition) _failures++;
        GD.Print("SINGLE_FILL_SMOKE " + (condition ? "PASS " : "FAIL ") + label);
    }

    private static bool Rendering => DisplayServer.GetName() != "headless";

    private async Task Frame()
    {
        if (Rendering) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        else await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static string Markup()
    {
        var css = new StringBuilder("html,body,div{display:block;margin:0;padding:0;border:0;box-sizing:border-box}" +
            "html,body{width:100%;height:100%;background-color:#243343}" +
            ".p{position:absolute;top:40px;width:18%;height:160px}" +
            "#ref{position:absolute;left:40px;top:260px;width:120px;height:80px;background-color:rgba(200,120,40,.73);" +
            "clip-path:polygon(0 0,50% 0,100% 0,100% 100%,0 100%)}");
        var body = new StringBuilder();
        string[] shapes = { "0 0,100% 0,100% 100%,0 100%", "0 0,100% 0,0 100%", "-12% 17%,87.3% -8%,115% 89%,23.2% 108%" };
        string[] colors = { "rgba(200,120,40,.73)", "rgba(60,180,220,.17)", "rgba(240,40,90,.00008)" };
        for (int i = 0; i < Polygons; i++)
        {
            css.Append("#q").Append(i).Append("{left:").Append(2 + i * 16).Append("%;background-color:").Append(colors[i % 3])
                .Append(";clip-path:polygon(").Append(shapes[i % 3]).Append(")}");
            body.Append("<div class=\"p\" id=\"q").Append(i).Append("\"></div>");
        }
        return "<!doctype html><html><head><style>" + css + "</style></head><body>" + body + "<div id=\"ref\"></div></body></html>";
    }

    private static T[] Nodes<T>(Node host) where T : Node => host.FindChildren("*", "", true, false).OfType<T>().ToArray();

    private async Task Run()
    {
        string path = "user://ui/single-fill-" + Guid.NewGuid().ToString("N");
        _directory = ProjectSettings.GlobalizePath(path);
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "polygons.html"), Markup());

        // Default sRGB composition keeps the general isolated path; it is measured, then withdrawn.
        var srgb = new DocumentHost { DocumentPath = path + "/polygons.html", Size = new Vector2(1280, 720) };
        AddChild(srgb);
        for (int i = 0; i < 10; i++) await Frame();
        srgb.FlushPendingLayout();
        Check(Nodes<PolygonClipGroup>(srgb).Length == Polygons + 1 && !Nodes<PolygonPaint>(srgb).Any(p => p.IsSingleFill),
            "default sRGB composition keeps isolated polygon clips");
        srgb.QueueFree();
        await Frame(); await Frame();

        var layer = new CanvasLayer(); AddChild(layer);
        var host = new DocumentHost { DocumentPath = path + "/polygons.html", Size = new Vector2(1280, 720), LinearComposition = true };
        layer.AddChild(host);
        for (int i = 0; i < 10; i++) await Frame();
        host.FlushPendingLayout();
        await Frame();
        var fills = Nodes<PolygonPaint>(host).Where(p => p.IsSingleFill).ToArray();
        Check(fills.Length == Polygons && Nodes<PolygonClipGroup>(host).Length == 1,
            $"linear composition fuses each triangle/quad clip with its fill ({fills.Length} fused)");
        var viewports = Nodes<SubViewport>(host).Select(node => node.GetInstanceId()).ToHashSet();

        int paint = host.PaintRevision, layout = host.LayoutRevision;
        for (int frame = 0; frame < 60; frame++)
        {
            host.Size = new Vector2(1280 + (frame + 1) % 2, 720);
            host.FlushPendingLayout();
            await Frame();
        }
        Check(host.LayoutRevision - layout >= 60 && host.PaintRevision == paint, "moving fused polygons update retained paint in place");
        Check(Nodes<PolygonPaint>(host).Where(p => p.IsSingleFill).Select(p => p.GetInstanceId()).ToHashSet()
            .SetEquals(fills.Select(p => p.GetInstanceId())), "moving fused polygons keep their nodes");
        Check(Nodes<SubViewport>(host).Select(node => node.GetInstanceId()).ToHashSet().SetEquals(viewports),
            "moving fused polygons create no viewports");

        host.Size = new Vector2(1280, 720);
        host.FlushPendingLayout();
        for (int i = 0; i < 4; i++) await Frame();
        if (!Rendering)
        {
            GD.Print("SINGLE_FILL_SMOKE SKIP pixel fidelity needs a drawing display");
            return;
        }
        using var image = GetViewport().GetTexture().GetImage();
        // #q0 is a full-box quad; #ref has the same color and box coverage but five vertices, so it keeps the general
        // isolated clip path. #q1 is the upper-left triangle of its box.
        Check(Nodes<PolygonClipGroup>(host).Length == 1, "a five-vertex clip keeps the general isolated path");
        Color fused = image.GetPixel(25 + 115, 120), general = image.GetPixel(100, 300), background = image.GetPixel(1200, 680);
        Check(Within(fused, general, 1), $"fused interior matches the general isolated path within one step (fused={fused}, general={general})");
        Color outside = image.GetPixel((int)(1280 * 0.18 + 1280 * 0.18 * 0.85), 40 + 136);
        Check(Within(outside, background, 0), $"pixels outside the fused triangle stay background ({outside} vs {background})");
        Check(!Within(fused, background, 8), "the fused polygon actually painted");
    }

    private static bool Within(Color a, Color b, int steps) => Math.Abs(a.R8 - b.R8) <= steps
        && Math.Abs(a.G8 - b.G8) <= steps && Math.Abs(a.B8 - b.B8) <= steps;
}
