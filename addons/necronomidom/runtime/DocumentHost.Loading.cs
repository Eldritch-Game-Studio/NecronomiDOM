#nullable enable
using System;
using Godot;
using NecronomiDom.Css.Cascade;
using NecronomiDom.Css.Om;
using NecronomiDom.Layout;
using NecronomiDom.Dom;
using NecronomiDom.Html;
using NecronomiDom.Url;

namespace NecronomiDom.Runtime;

public partial class DocumentHost
{
    public void LoadDocument(string path) => LoadPreparedDocument(PrepareDocument(path));

    /// <summary>Unattached HTML/CSS only; the owner transfers this result from its worker before mounting it.</summary>
    internal readonly record struct PreparedDocument(string Path, HtmlImportResult Content);

    internal static PreparedDocument PrepareDocument(string path)
    {
        if (!path.StartsWith("res://", StringComparison.Ordinal) && !path.StartsWith("user://ui/", StringComparison.Ordinal)) throw new ArgumentException("Document path must be res:// or user://ui/.", nameof(path));
        WebUrl url = WebUrl.Parse(path.Replace("://", ":///", StringComparison.Ordinal)) ?? throw new ArgumentException("Invalid document URL.");
        var imported = HtmlImporter.Import(Read(path), path, url);
        var document = imported.Document;
        foreach (Element link in document.QuerySelectorAll("link"))
        {
            if (link.GetAttribute("rel") != "stylesheet") throw new NotSupportedException("Runtime supports only stylesheet links.");
            var resolved = ResourceUrlPolicy.Default.Resolve(link.GetAttribute("href") ?? "", url, ResourceKind.Stylesheet);
            if (!resolved.Allowed || resolved.ResourcePath == null) throw new NotSupportedException("Runtime stylesheet URL: " + resolved);
            var sheet = CSSStyleSheet.Parse(Read(resolved.ResourcePath), resolved.ResourcePath, resolved.Url);
            sheet.OwnerNode = link;
            if (link.GetAttribute("media") is { } media) sheet.Media.MediaText = media;
            document.StyleSheets.Add(sheet);
        }
        RuntimeStyleProfile.Validate(document);
        return new PreparedDocument(path, imported);
    }

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
        if (_entryActive) CreateDocumentResources();
    }
    private static string Read(string path)
    {
        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        if (file == null) throw new InvalidOperationException("Cannot load " + path + ": " + Godot.FileAccess.GetOpenError());
        return file.GetAsText();
    }
}
