#nullable enable
using System;
using Godot;
using NecronomiDom.Css.Cascade;
using NecronomiDom.Html;

namespace NecronomiDom.Runtime;

public partial class DocumentHost
{
    public void LoadDocument(string path) => LoadPreparedDocument(PrepareDocument(path));

    /// <summary>Unattached HTML/CSS only; the owner transfers this result from its worker before mounting it.</summary>
    internal readonly record struct PreparedDocument(string Path, HtmlImportResult Content, DocumentFileReload? Files = null);

    internal static PreparedDocument PrepareDocument(string path) => new(path, DocumentFileReload.Load(path, Read));

    internal void LoadPreparedDocument(PreparedDocument prepared)
    {
        foreach (var warning in prepared.Content.Diagnostics) GD.PushWarning(warning.ToString());
        var document = prepared.Content.Document;
        WithdrawDocumentResources();
        ResetInput();
        Document = document;
        // Build the viewing-context style sink below host invalidation and accessibility observers.
        _ = document.DefaultView;
        Styles = new StyleEngine(document) { IncrementalReconciliation = RetainedUpdates };
        DocumentPath = prepared.Path;
        _reloadFiles = DeveloperReloadEnabled ? prepared.Files : null;
        if (_entryActive) CreateDocumentResources();
        if (DeveloperReloadEnabled && OS.IsDebugBuild() && _reloadFiles == null) CaptureDeveloperReloadBaseline();
    }
    private static string Read(string path) => ReadSource(path, false);
    private static string ReadDeveloperSource(string path) => ReadSource(path, true);
    private static string ReadSource(string path, bool developerOperation)
    {
        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        if (file == null) throw new InvalidOperationException("Cannot load " + path + ": " + Godot.FileAccess.GetOpenError());
        if (developerOperation && file.GetLength() > (ulong)DocumentFileReload.MaxFileCharacters * 4)
            throw new InvalidOperationException("Document source byte limit exceeded: " + path);
        return file.GetAsText();
    }
}
