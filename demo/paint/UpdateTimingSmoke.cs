using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Godot;
using NecronomiDom.Runtime;

namespace NecronomiDomDemo;

/// <summary>Collects engine log messages that carry document-update timing records.</summary>
public partial class UpdateTimingMessages : Logger
{
    private readonly object _gate = new();
    private int _count;
    public int Count { get { lock (_gate) return _count; } }
    private void Observe(string message)
    {
        if (!message.Contains("NecronomiDOM document-update", StringComparison.Ordinal)) return;
        lock (_gate) _count++;
    }
    public override void _LogMessage(string message, bool error) => Observe(message);
    public override void _LogError(string function, string file, int line, string code, string rationale,
        bool editorNotify, int errorType, Godot.Collections.Array<ScriptBacktrace> scriptBacktraces) => Observe(code + " " + rationale);
}

/// <summary>Opt-in phase timing (NECRONOMIDOM_UPDATE_TIMING=1) covers synchronous FlushPendingLayout calls and writes
/// qualifying records to a per-host user:// file, never to stdout or a captured console, and closes it on withdrawal.</summary>
public partial class UpdateTimingSmoke : Godot.Node
{
    private int _failures;

    public override async void _Ready()
    {
        var messages = new UpdateTimingMessages();
        OS.AddLogger(messages);
        try { await Run(messages); }
        catch (Exception error) { _failures++; GD.PushError(error.ToString()); }
        finally
        {
            OS.RemoveLogger(messages);
            GD.Print("UPDATE_TIMING_SMOKE_RESULT " + (_failures == 0 ? "PASS" : "FAIL"));
            GetTree().Quit(_failures == 0 ? 0 : 1);
        }
    }

    private void Check(bool condition, string label)
    {
        if (!condition) _failures++;
        GD.Print("UPDATE_TIMING_SMOKE " + (condition ? "PASS " : "FAIL ") + label);
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static bool Locked(string path)
    {
        // .NET takes an advisory shared lock for the host's writer; an exclusive open fails until it is closed.
        try { using var probe = new FileStream(path, FileMode.Open, System.IO.FileAccess.Read, FileShare.None); return false; }
        catch (IOException) { return true; }
    }

    private async Task Run(UpdateTimingMessages messages)
    {
        Check(System.Environment.GetEnvironmentVariable("NECRONOMIDOM_UPDATE_TIMING") == "1", "run with NECRONOMIDOM_UPDATE_TIMING=1");
        string path = "user://ui/update-timing-" + Guid.NewGuid().ToString("N");
        string directory = ProjectSettings.GlobalizePath(path);
        Directory.CreateDirectory(directory);
        var rows = new StringBuilder();
        for (int i = 0; i < 400; i++) rows.Append("<p class=\"row\">Row ").Append(i).Append(" of a large timed document</p>");
        File.WriteAllText(Path.Combine(directory, "timed.html"), "<!doctype html><html><head><style>" +
            "html,body,p{display:flex;flex-direction:column;flex-shrink:0;margin:0;padding:0;font-size:14px;line-height:1.2}" +
            "html,body{width:100%;height:100%;background-color:#202c39;color:white} .wide p{font-size:15px}" +
            "</style></head><body>" + rows + "</body></html>");
        var host = new DocumentHost { DocumentPath = path + "/timed.html", Size = new Vector2(1280, 720) };
        var entries = new List<double>();
        host.UpdateMeasured += update => entries.Add(update.TotalMs);
        AddChild(host);
        await Frames(10);

        entries.Clear();
        host.Document.Body!.ClassList.Toggle("wide");
        host.FlushPendingLayout();
        Check(entries.Count == 1, "a synchronous FlushPendingLayout publishes one timed update");

        string log = Path.Combine(OS.GetUserDataDir(), $"necronomidom-document-updates-{System.Environment.ProcessId}-{host.GetInstanceId()}.log");
        Check(File.Exists(log) && File.ReadLines(log).Any(line => line.Contains("entry=flush", StringComparison.Ordinal)),
            "qualifying flush timing is appended to the per-host user:// file");
        Check(File.Exists(log) && File.ReadLines(log).All(line => line.StartsWith("NecronomiDOM document-update ", StringComparison.Ordinal)),
            "every file record is a document-update record");
        Check(File.Exists(log) && Locked(log), "the host keeps its timing writer open while live");
        Check(messages.Count == 0, "no timing record reaches stdout or engine loggers");

        host.QueueFree();
        await Frames(3);
        Check(File.Exists(log) && !Locked(log), "withdrawing the host closes its timing file");
        if (File.Exists(log)) File.Delete(log);
        Directory.Delete(directory, true);
    }
}
