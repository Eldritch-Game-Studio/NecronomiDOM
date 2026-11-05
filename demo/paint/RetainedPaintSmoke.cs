using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using NecronomiDom.Dom;
using NecronomiDom.Paint;
using NecronomiDom.Runtime;

namespace NecronomiDomDemo;

/// <summary>Collects the engine's "synchronizations on every frame" warnings. Godot reports them only while a
/// separate render thread is drawing real frames, so headless runs cannot observe them.</summary>
public partial class RenderSyncWarnings : Logger
{
    private readonly object _gate = new();
    private readonly List<string> _messages = new();
    public string[] Messages { get { lock (_gate) return _messages.ToArray(); } }
    public override void _LogError(string function, string file, int line, string code, string rationale,
        bool editorNotify, int errorType, Godot.Collections.Array<ScriptBacktrace> scriptBacktraces)
    {
        string message = code + " " + rationale;
        if (!message.Contains("synchronizations on every frame", StringComparison.Ordinal)) return;
        lock (_gate) if (_messages.Count < 16) _messages.Add(message.Trim());
    }
}

/// <summary>Real-engine regression for retained paint. Moving clips, borders and background images and an animated box
/// style update the existing native paint in place; changed glyph-effect text and structural repaints reuse the
/// same glyph mask/blur targets; and no paint path makes a RenderingServer round trip on every frame.</summary>
public partial class RetainedPaintSmoke : Godot.Node
{
    private const int Frames = 120;
    private static readonly Color Twin = new(0.2f, 0.4f, 0.6f);
    private static readonly Color Restyled = new(0.6f, 0.4f, 0.2f);
    private DocumentHost _host = null!;
    private RenderSyncWarnings? _warnings;
    private string _directory = "";
    private int _failures;

    public override async void _Ready()
    {
        try { await Run(); }
        catch (Exception error) { _failures++; GD.PushError(error.ToString()); }
        finally
        {
            if (_host != null && GodotObject.IsInstanceValid(_host)) { _host.QueueFree(); await NextFrames(3); }
            if (_warnings != null) OS.RemoveLogger(_warnings);
            if (_directory.Length > 0 && Directory.Exists(_directory)) Directory.Delete(_directory, true);
            GD.Print("RETAINED_PAINT_SMOKE_RESULT " + (_failures == 0 ? "PASS" : "FAIL"));
            GetTree().Quit(_failures == 0 ? 0 : 1);
        }
    }

    private async Task NextFrames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private void Check(bool condition, string label)
    {
        if (!condition) _failures++;
        GD.Print("RETAINED_PAINT_SMOKE " + (condition ? "PASS " : "FAIL ") + label);
    }

    private static bool Rendering => DisplayServer.GetName() != "headless";

    private static string Markup(string keycap) => "<!doctype html><html><head><style>" +
        "html,body,div,p,input{display:flex;flex-direction:column;flex-shrink:0;margin:0;padding:0;border:0 solid black;appearance:none;box-sizing:border-box;font-size:18px;line-height:1.3} " +
        "html,body{width:100%;height:100%;background-color:#202c39;color:white} [hidden]{display:none} " +
        "#card{position:absolute;left:24px;top:24px;width:360px;height:200px;padding:18px;border:2px solid #c0a55c;border-radius:12px;overflow:hidden;background-color:#332e27} " +
        "p{margin-bottom:8px;text-shadow:1px 1px 2px black} " +
        ".title{font-size:24px;-webkit-text-stroke-width:1px;-webkit-text-stroke-color:black} " +
        "#field{position:absolute;left:24px;top:260px;width:360px;height:42px;color:white;background-color:#332e27;text-shadow:1px 1px 2px black} " +
        "#keycap{position:absolute;left:24px;top:340px;width:64px;height:48px;background-image:url('" + keycap + "');background-size:cover;background-repeat:no-repeat;border-radius:8px} " +
        "#frame{position:absolute;left:420px;top:24px;width:120px;height:80px;border:3px solid #c0a55c;border-left-color:#5ab0ff} " +
        "#orb{position:absolute;left:420px;top:140px;width:160px;height:80px;border-radius:50%;overflow:hidden;background-color:#3a6e4c} " +
        ".twin{position:absolute;top:340px;width:80px;height:60px;background-color:#336699} " +
        "#twin-a{left:420px} #twin-b{left:520px;transition:background-color .2s linear} " +
        "#spawned{position:absolute;left:620px;top:340px;width:40px;height:40px;background-color:#b46c39} " +
        "#prefix{position:absolute;left:640px;top:40px;width:40px;height:40px;background-color:#00ffff;opacity:.5} " +
        ".shard{position:absolute;top:440px;width:160px;height:110px;background-color:#d2501e;clip-path:polygon(0 15%,85% 0,100% 85%,20% 100%)} " +
        "#shard-a{left:24px} #shard-b{left:200px} " +
        "#fade{position:absolute;left:420px;top:440px;width:220px;height:120px;opacity:.6} " +
        "#fade div{position:absolute;width:120px;height:100px} #fade-a{background-color:#ff0000} #fade-b{left:60px;top:20px;background-color:#0000ff} " +
        "@media (min-width:1281px){#card{left:25px} #keycap{left:96px;border-radius:16px} #frame{left:421px} #orb{left:421px} #twin-b{background-color:#996633}}" +
        "</style></head><body><div id=\"prefix\" hidden></div><div class=\"shard\" id=\"shard-a\"></div><div class=\"shard\" id=\"shard-b\"></div>" +
        "<div id=\"fade\"><div id=\"fade-a\"></div><div id=\"fade-b\"></div></div>" +
        "<div id=\"card\"><p class=\"title\">Retained card</p><p>Moving clips keep their glyph targets.</p>" +
        "<p>Shadowed text is not repainted from scratch.</p></div><input id=\"field\" value=\"Retained glyphs\" aria-label=\"Glyph effects\">" +
        "<div id=\"keycap\"></div><div id=\"frame\"></div><div id=\"orb\"><p>Ellipse</p></div>" +
        "<div class=\"twin\" id=\"twin-a\"></div><div class=\"twin\" id=\"twin-b\"></div><div id=\"spawned\" hidden></div></body></html>";

    private static string KeycapUrl()
    {
        using var image = Image.CreateEmpty(2, 2, false, Image.Format.Rgba8);
        image.Fill(new Color(0.1f, 0.7f, 0.2f));
        image.SetPixel(1, 0, new Color(0.9f, 0.2f, 0.1f));
        image.SetPixel(0, 1, new Color(0.1f, 0.3f, 0.9f));
        image.SetPixel(1, 1, new Color(0.9f, 0.8f, 0.1f));
        return "data:image/png;base64," + Marshalls.RawToBase64(image.SavePngToBuffer());
    }

    private HashSet<ulong> PaintTargets() => _host.FindChildren("*", nameof(SubViewport), true, false)
        .Select(node => node.GetInstanceId()).ToHashSet();

    /// <summary>One changed layout per drawn frame; a headless display never draws, so it advances process frames.</summary>
    private async Task Settle(int width)
    {
        _host.Size = new Vector2(width, 720);
        _host.FlushPendingLayout();
        if (Rendering) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        else await NextFrames(1);
    }

    private async Task Run()
    {
        // Godot's dummy headless window is 64x64; clip fences intersect it. Give headless runs the drawn size.
        if (!Rendering)
        {
            GetTree().Root.ContentScaleSize = new Vector2I(1440, 1000);
            GetTree().Root.ContentScaleMode = Godot.Window.ContentScaleModeEnum.Viewport;
        }
        _warnings = new RenderSyncWarnings();
        OS.AddLogger(_warnings);
        string path = "user://ui/retained-paint-" + Guid.NewGuid().ToString("N");
        _directory = ProjectSettings.GlobalizePath(path);
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "retained.html"), Markup(KeycapUrl()));
        var layer = new CanvasLayer(); AddChild(layer);
        _host = new DocumentHost { DocumentPath = path + "/retained.html" };
        layer.AddChild(_host);
        await NextFrames(10);
        await Settle(1280);
        await NextFrames(10);

        var runs = _host.DisplayList.Runs;
        Check(runs.OfType<BorderBoxRun>().Any(), "fixture paints an asymmetric border");
        Check(runs.OfType<BackgroundImageRun>().Any(), "fixture paints a clipped background image");
        Check(runs.OfType<BeginRoundedRectClipRun>().Any(), "fixture paints a rounded overflow clip");
        Check(runs.OfType<BeginEllipticalClipRun>().Any(), "fixture paints an elliptical overflow clip");
        Check(runs.OfType<BeginPolygonClipRun>().Count() >= 2 && runs.OfType<BeginOpacityRun>().Any(),
            "fixture paints polygon clip-paths and an opacity group");
        Check(runs.OfType<BoxRun>().Count() >= 2, "fixture paints styled boxes");
        Check(runs.OfType<TextEffectsRun>().Any(run => run.Effects.Shadows.Span.ToArray().Any(shadow => shadow.BlurRadius > 0)),
            "fixture paints blurred glyph shadows");
        var targets = PaintTargets();
        Check(targets.Count > 0, "fixture owns native glyph mask/blur targets");

        int paint = _host.PaintRevision, layout = _host.LayoutRevision;
        // Unmoved clip-path and opacity composites must not recapture because unrelated content moved.
        var statics = _host.FindChildren("*", "", true, false).OfType<PolygonComposite>()
            .Where(context => context is PolygonClipGroup or PolygonOpacityGroup).ToArray();
        int staticCaptures = statics.Sum(context => context.TargetUpdateCount);
        var keycap = runs.OfType<BackgroundImageRun>().First().Clip.Bounds;
        bool moved = false;
        for (int frame = 0; frame < Frames; frame++)
        {
            await Settle(1280 + (frame + 1) % 2);
            moved |= _host.DisplayList.Runs.OfType<BackgroundImageRun>().First().Clip.Bounds != keycap;
        }
        Check(_host.LayoutRevision - layout >= Frames && moved, "geometry changed on every frame");
        Check(_host.PaintRevision == paint, "moving clips, borders, backgrounds and box styles update retained paint in place");
        Check(PaintTargets().SetEquals(targets), "moving content keeps the existing glyph and clip targets");
        // A headless display never sizes isolation targets, so only a drawing display can observe recaptures.
        bool staticsKept = statics.Length >= 3 && statics.Sum(context => context.TargetUpdateCount) == staticCaptures;

        paint = _host.PaintRevision;
        var field = (HTMLInputElement)_host.Document.GetElementById("field")!;
        for (int frame = 0; frame < Frames; frame++)
        {
            field.Value = frame % 2 == 0 ? "Retained glyphs!" : "Retained glyphs";
            await Settle(1280);
        }
        Check(_host.PaintRevision == paint, "changed glyph-effect text updates retained paint in place");
        Check(PaintTargets().SetEquals(targets), "changed glyph-effect text reuses its mask and blur targets");

        paint = _host.PaintRevision;
        var spawned = _host.Document.GetElementById("spawned")!;
        for (int frame = 0; frame < Frames / 2; frame++)
        {
            if (frame % 2 == 0) spawned.RemoveAttribute("hidden"); else spawned.SetAttribute("hidden", "");
            await Settle(1280);
        }
        Check(_host.PaintRevision - paint >= Frames / 2, "topology changes take the structural repaint path");
        Check(PaintTargets().SetEquals(targets), "structural repaint reuses existing glyph and clip targets");

        // A different-kind context inserted before unchanged polygon clips, a rounded clip and an opacity group
        // must not evict their composites: pooled targets are matched by kind, not by position.
        paint = _host.PaintRevision;
        var prefix = _host.Document.GetElementById("prefix")!;
        bool kept = true, grew = false;
        for (int frame = 0; frame < Frames / 2; frame++)
        {
            if (frame % 2 == 0) prefix.RemoveAttribute("hidden"); else prefix.SetAttribute("hidden", "");
            await Settle(1280);
            var current = PaintTargets();
            kept &= current.IsSupersetOf(targets);
            grew |= current.Count > targets.Count;
        }
        Check(_host.PaintRevision - paint >= Frames / 2 && grew, "inserting a different-kind context repaints structurally");
        Check(kept && PaintTargets().SetEquals(targets), "mixed-kind insertion keeps every existing polygon, clip and opacity target");

        // Inline clip-path points and an absolute fill on a childless polygon update the retained clip and fill
        // directly: no layout pass, no structural repaint, and the painted run carries the new values. Documents
        // with text controls keep the full path, so this uses its own small document.
        await RetainedPolygonPaint(path);
        // Moving an absolutely positioned subtree through numeric insets translates its existing boxes and paint.
        await RetainedAbsolutePosition(path);
        // A same-length digit change in a single-line text node replaces its glyph run without relayout.
        await RetainedNumericText(path);

        await Settle(1281);
        await ToSignal(GetTree().CreateTimer(0.4), SceneTreeTimer.SignalName.Timeout);
        await Settle(1281);
        string[] warnings = _warnings.Messages;
        foreach (string warning in warnings) GD.Print("RETAINED_PAINT_SMOKE sync warning: " + warning);
        // Godot reports these only from the main thread while a separate render thread draws real frames.
        bool observable = Rendering && !RenderingServer.IsOnRenderThread();
        if (OS.GetCmdlineUserArgs().Contains("--require-render-sync-check"))
            Check(observable, "a drawing display and separate render thread make synchronization observable");
        if (!observable)
        {
            GD.Print("RETAINED_PAINT_SMOKE SKIP render-thread synchronization, capture counts and pixels need a drawing display and --render-thread separate");
            return;
        }
        Check(warnings.Length == 0, "no RenderingServer synchronization on every frame");
        Check(staticsKept, "unmoved clip-path and opacity targets are not recaptured while other content moves");
        using var image = GetViewport().GetTexture().GetImage();
        Color a = image.GetPixel(460, 370), b = image.GetPixel(560, 370);
        Check(Near(a, Twin) && Near(b, Restyled), $"independently restyled boxes keep separate styles (a={a}, b={b})");
        string output = OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith("--proof-out=", StringComparison.Ordinal))?[12..] ?? "";
        if (output.Length > 0)
        {
            Directory.CreateDirectory(output);
            Check(image.SavePng(Path.Combine(output, "retained-paint.png")) == Error.Ok, "screenshot");
        }
    }

    private static bool Near(Color actual, Color expected) => Math.Abs(actual.R - expected.R) < 0.03f
        && Math.Abs(actual.G - expected.G) < 0.03f && Math.Abs(actual.B - expected.B) < 0.03f;

    private async Task RetainedPolygonPaint(string path)
    {
        File.WriteAllText(Path.Combine(_directory, "polygons.html"), "<!doctype html><html><head><style>" +
            "html,body,div{display:block;margin:0;padding:0;border:0;box-sizing:border-box} html,body{width:100%;height:100%;background-color:#202c39} " +
            "#wrap{position:absolute;left:40px;top:40px;width:400px;height:300px;overflow:hidden;opacity:.8} " +
            "#sector{position:absolute;left:20px;top:20px;width:200px;height:160px} #other{position:absolute;left:240px;top:20px;width:60px;height:60px;background-color:#3366aa}" +
            "</style></head><body><div id=\"wrap\"><div id=\"sector\" style=\"clip-path:polygon(0% 0%, 90% 0%, 90% 90%);background-color:rgb(20, 120, 30)\"></div>" +
            "<div id=\"other\"></div></div></body></html>");
        var layer = new CanvasLayer(); AddChild(layer);
        var host = new DocumentHost { DocumentPath = path + "/polygons.html", Size = new Vector2(1280, 720) };
        layer.AddChild(host);
        await NextFrames(10);
        host.FlushPendingLayout();
        await NextFrames(2);
        int paint = host.PaintRevision, layout = host.LayoutRevision, geometry = host.GeometryRevision;
        var targets = host.FindChildren("*", nameof(SubViewport), true, false).Select(node => node.GetInstanceId()).ToHashSet();
        var sector = (HTMLElement)host.Document.GetElementById("sector")!;
        bool painted = true;
        for (int frame = 0; frame < Frames / 2; frame++)
        {
            int red = frame % 2 == 0 ? 10 : 200;
            sector.Style.SetProperty("clip-path", frame % 2 == 0 ? "polygon(0% 10%, 90% 0%, 100% 90%)" : "polygon(0% 0%, 90% 0%, 90% 90%)");
            sector.Style.SetProperty("background-color", $"rgb({red}, 120, 30)");
            host.FlushPendingLayout();
            await NextFrames(1);
            painted &= host.DisplayList.Runs.OfType<PolygonRun>().Any(run => Math.Abs(run.Color.R - red / 255f) < 0.002f);
        }
        Check(host.LayoutRevision == layout && host.PaintRevision == paint && host.GeometryRevision - geometry >= Frames / 2,
            $"inline polygon clip and fill changes skip layout and repaint (layout +{host.LayoutRevision - layout}, paint +{host.PaintRevision - paint}, geometry +{host.GeometryRevision - geometry})");
        Check(painted, "retained polygon fill carries each new color");
        Check(host.FindChildren("*", nameof(SubViewport), true, false).Select(node => node.GetInstanceId()).ToHashSet().SetEquals(targets),
            "retained polygon clip and fill changes keep every target");
        host.QueueFree(); layer.QueueFree();
        await NextFrames(2);
    }

    private async Task RetainedAbsolutePosition(string path)
    {
        File.WriteAllText(Path.Combine(_directory, "positions.html"), "<!doctype html><html><head><style>" +
            "html,body,div{display:block;margin:0;padding:0;border:0;box-sizing:border-box} html,body{width:100%;height:100%;background-color:#202c39} " +
            "#track{position:absolute;left:40px;top:40px;width:600px;height:200px;overflow:hidden;background-color:#1a2430} " +
            "#marker{position:absolute;width:120px;height:80px;background-color:#c0a55c} " +
            "#bar{display:flex;width:100px;height:20px;background-color:#3366aa} " +
            "#dot{position:absolute;left:10px;top:40px;width:16px;height:16px;background-color:#aa3333}" +
            "</style></head><body><div id=\"track\"><div id=\"marker\" style=\"left:20px;top:20px;right:auto;bottom:auto\">" +
            "<div id=\"bar\"></div><div id=\"dot\"></div></div></div></body></html>");
        var layer = new CanvasLayer(); AddChild(layer);
        var host = new DocumentHost { DocumentPath = path + "/positions.html", Size = new Vector2(1280, 720) };
        layer.AddChild(host);
        await NextFrames(10);
        host.FlushPendingLayout();
        await NextFrames(2);
        int paint = host.PaintRevision, layout = host.LayoutRevision, geometry = host.GeometryRevision;
        var marker = (HTMLElement)host.Document.GetElementById("marker")!;
        bool moved = true;
        for (int frame = 0; frame < Frames / 2; frame++)
        {
            int left = 27 + frame * 7;
            marker.Style.SetProperty("left", left + "px");
            host.FlushPendingLayout();
            await NextFrames(1);
            var dot = host.DisplayList.Boxes.First(box => box.Element.Id == "dot").Bounds;
            moved &= Math.Abs(dot.X - (40 + left + 10)) < 0.01;
        }
        Check(host.LayoutRevision == layout && host.PaintRevision == paint && host.GeometryRevision - geometry >= Frames / 2,
            $"absolute inset moves translate the subtree without layout or repaint (layout +{host.LayoutRevision - layout}, paint +{host.PaintRevision - paint}, geometry +{host.GeometryRevision - geometry})");
        Check(moved, "translated descendants land where a fresh layout would place them");
        host.QueueFree(); layer.QueueFree();
        await NextFrames(2);
    }

    private async Task RetainedNumericText(string path)
    {
        File.WriteAllText(Path.Combine(_directory, "numbers.html"), "<!doctype html><html><head><style>" +
            "html,body,div,p{display:block;margin:0;padding:0;border:0;box-sizing:border-box} html,body{width:100%;height:100%;background-color:#202c39;color:white} " +
            "#hud{position:absolute;left:40px;top:40px;width:400px;height:120px;font-size:24px;line-height:32px} " +
            "#speed{position:absolute;left:10px;top:10px;width:300px} #shadowed{position:absolute;left:10px;top:60px;width:300px;text-shadow:1px 1px 2px black}" +
            "</style></head><body><div id=\"hud\"><p id=\"speed\">SPD 12.3</p><p id=\"shadowed\">18%</p></div></body></html>");
        var layer = new CanvasLayer(); AddChild(layer);
        var host = new DocumentHost { DocumentPath = path + "/numbers.html", Size = new Vector2(1280, 720) };
        layer.AddChild(host);
        await NextFrames(10);
        host.FlushPendingLayout();
        await NextFrames(2);
        int paint = host.PaintRevision, layout = host.LayoutRevision;
        var targets = host.FindChildren("*", nameof(SubViewport), true, false).Select(node => node.GetInstanceId()).ToHashSet();
        var speed = host.Document.GetElementById("speed")!;
        var shadowed = host.Document.GetElementById("shadowed")!;
        bool painted = true;
        for (int frame = 0; frame < Frames / 2; frame++)
        {
            string value = "SPD " + (10 + frame % 90).ToString("00") + "." + (frame % 10);
            string percent = (10 + (frame * 7) % 90).ToString("00") + "%";
            speed.TextContent = value;
            shadowed.TextContent = percent;
            host.FlushPendingLayout();
            await NextFrames(1);
            var texts = host.DisplayList.Runs.Select(run => run is TextEffectsRun effects ? effects.Text : run).OfType<TextRun>().Select(run => run.Text).ToArray();
            painted &= texts.Contains(value) && texts.Contains(percent);
        }
        Check(host.LayoutRevision == layout && host.PaintRevision == paint,
            $"same-length digit changes replace glyph runs without layout or repaint (layout +{host.LayoutRevision - layout}, paint +{host.PaintRevision - paint})");
        Check(painted, "updated digit runs carry each new value");
        Check(host.FindChildren("*", nameof(SubViewport), true, false).Select(node => node.GetInstanceId()).ToHashSet().SetEquals(targets),
            "digit changes keep shadowed text glyph targets");
        host.QueueFree(); layer.QueueFree();
        await NextFrames(2);
    }
}
