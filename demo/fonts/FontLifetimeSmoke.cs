using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using NecronomiDom.Paint;
using NecronomiDom.Runtime;

namespace NecronomiDomDemo;

/// <summary>Imported fonts are borrowed from Godot's resource cache, whose canonical wrapper every consumer of the
/// same path shares (a game console, another document). Loading, replacing and retiring documents and fallback faces
/// must leave that wrapper usable.</summary>
public partial class FontLifetimeSmoke : Godot.Node
{
    private const string Markup = "<!doctype html><html><head><style>html,body,p { display:block; margin:0; } " +
        "body { font-size:20px; color:white; }</style></head><body><p id=\"text\">Borrowed</p></body></html>";
    private string _directory = "";
    private int _failures;

    public override async void _Ready()
    {
        SmokeIsolation.IgnoreHostControllers();
        try { await Run(); }
        catch (Exception error) { _failures++; GD.PushError(error.ToString()); }
        finally
        {
            if (_directory.Length > 0 && Directory.Exists(_directory)) Directory.Delete(_directory, true);
            GD.Print("FONT_LIFETIME_SMOKE_RESULT " + (_failures == 0 ? "PASS" : "FAIL"));
            GetTree().Quit(_failures == 0 ? 0 : 1);
        }
    }

    private async Task Frames(int count = 3)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    private void Check(bool value, string label)
    {
        if (!value) _failures++;
        GD.Print("FONT_LIFETIME_SMOKE " + (value ? "PASS " : "FAIL ") + label);
    }
    private static bool Usable(FontFile font, string path) =>
        GodotObject.IsInstanceValid(font) && font.GetFontName().Length > 0 && ReferenceEquals(GD.Load<FontFile>(path), font);
    private static bool Paints(DocumentHost host, string text) => host.DisplayList.Runs
        .Select(run => run is TextEffectsRun effects ? effects.Text : run).OfType<TextRun>().Any(run => run.Text == text);
    private async Task<DocumentHost> Open(Node layer, string path)
    {
        var host = new DocumentHost { DocumentPath = path };
        layer.AddChild(host);
        await Frames();
        return host;
    }

    private async Task Run()
    {
        string path = "user://ui/font-lifetime-" + Guid.NewGuid().ToString("N");
        _directory = ProjectSettings.GlobalizePath(path); Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "screen.html"), Markup);
        var layer = new CanvasLayer(); AddChild(layer);
        const string fontPath = "res://addons/necronomidom/fonts/NotoSans-Regular.otf"; // DocumentHost.FontPath default
        // Held exactly as another consumer of the imported asset would hold it.
        var borrowed = GD.Load<FontFile>(fontPath);

        var first = await Open(layer, path + "/screen.html");
        Check(Paints(first, "Borrowed"), "first document paints with the imported font");
        Check(Usable(borrowed, fontPath), "loading a document leaves the cached font wrapper usable");
        var second = await Open(layer, path + "/screen.html");
        first.QueueFree(); await Frames();
        Check(Usable(borrowed, fontPath), "retiring one document leaves the cached font wrapper usable");
        second.Document.GetElementById("text")!.TextContent = "Reshaped";
        await Frames();
        Check(Paints(second, "Reshaped"), "a surviving document keeps shaping after its peer exits");
        second.QueueFree(); await Frames();
        Check(Usable(borrowed, fontPath), "retiring every document leaves the cached font wrapper usable");

        // Fallback faces load lazily through the same cache; a code point the primary face lacks forces the load.
        string fallbackPath = path + "/fallback.res";
        Check(ResourceSaver.Save(new FontFile { Data = borrowed.Data }, fallbackPath) == Error.Ok, "fallback font resource saved");
        var fallback = GD.Load<FontFile>(fallbackPath);
        using (var shaper = new AdvancedTextShaper(borrowed, new[] { fallbackPath }))
        {
            bool refused = false;
            try { shaper.Measure("\u4E2D", 20); }
            catch (NotSupportedException) { refused = true; }
            Check(refused, "a code point no face maps is refused after consulting the fallback face");
            Check(Usable(fallback, fallbackPath), "loading a fallback face leaves its cached font wrapper usable");
        }
        Check(Usable(fallback, fallbackPath) && Usable(borrowed, fontPath), "disposing a shaper leaves borrowed fonts usable");
    }
}
