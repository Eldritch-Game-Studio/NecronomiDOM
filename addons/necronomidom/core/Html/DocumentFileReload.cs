using System;
using System.Collections.Generic;
using NecronomiDom.Css.Om;
using NecronomiDom.Dom;
using NecronomiDom.Layout;
using NecronomiDom.Url;

namespace NecronomiDom.Html;

/// <summary>Local HTML/linked-CSS loading and transactional, bounded developer reload preparation.
/// Ordinary loading retains its existing resource limits; additional bounds apply only to developer operations.
/// This uses the runtime style profile: imports and other unsupported rules are errors, not silently flattened.</summary>
public sealed class DocumentFileReload
{
    public const int MaxFiles = 128;
    public const int MaxFileCharacters = 4 * 1024 * 1024;
    public const int MaxTotalCharacters = 16 * 1024 * 1024;
    private Dictionary<string, string> _sources;
    private List<CSSStyleSheet> _linkedSheets;
    private List<Link> _links;
    public string Path { get; }
    public HtmlImportResult Content { get; }
    public Document Document => Content.Document;
    public int DependencyCount => _sources.Count;
    /// <summary>Detached DOM/CSS snapshot for real renderer preflight, including current runtime control/selector state.</summary>
    public Document CreateValidationDocument() => DocumentReloadPreview.Create(Document, Array.Empty<CSSStyleSheet>(), Array.Empty<CSSStyleSheet>());

    private DocumentFileReload(string path, HtmlImportResult content, Dictionary<string, string> sources, List<CSSStyleSheet> sheets, List<Link> links)
    { Path = path; Content = content; _sources = sources; _linkedSheets = sheets; _links = links; }

    /// <summary>Ordinary runtime loading, without reload snapshots or developer-only resource limits.</summary>
    public static HtmlImportResult Load(string path, Func<string, string> read)
    {
        var url = DocumentUrl(path);
        var content = HtmlImporter.Import(read(path), path, url);
        var diagnostics = content.Diagnostics as List<WebDiagnostic> ?? new List<WebDiagnostic>(content.Diagnostics);
        foreach (Element link in content.Document.QuerySelectorAll("link"))
        {
            if (link.GetAttribute("rel") != "stylesheet") throw new NotSupportedException("Runtime supports only stylesheet links.");
            var resolved = ResourceUrlPolicy.Default.Resolve(link.GetAttribute("href") ?? "", url, ResourceKind.Stylesheet);
            if (!resolved.Allowed || resolved.ResourcePath == null || resolved.Url == null) throw new NotSupportedException("Runtime stylesheet URL: " + resolved);
            var sheet = CSSStyleSheet.Parse(read(resolved.ResourcePath), resolved.ResourcePath, resolved.Url, diagnostics);
            sheet.OwnerNode = link;
            if (link.GetAttribute("media") is { } media) sheet.Media.MediaText = media;
            content.Document.StyleSheets.Add(sheet);
        }
        RuntimeStyleProfile.Validate(content.Document);
        return ReferenceEquals(content.Diagnostics, diagnostics) ? content : new HtmlImportResult(content.Document, diagnostics);
    }

    /// <summary>Start tracking an existing DOM without replacing its sheets or handles. Current disk contents
    /// establish the baseline; edits made before attachment require an explicit force reload.</summary>
    public static DocumentFileReload Attach(Document document, string path, Func<string, string> read)
    {
        var sources = new Sources(read);
        sources.Read(path);
        var links = ReadLinks(document, sources);
        var sheets = new List<CSSStyleSheet>(links.Count);
        foreach (var link in links)
            foreach (var sheet in document.StyleSheets)
                if (ReferenceEquals(sheet.OwnerNode, link.Owner) && sheet.SourcePath == link.Path && sheet.Href == link.Url.Href)
                { sheets.Add(sheet); break; }
        return new DocumentFileReload(path, new HtmlImportResult(document, Array.Empty<WebDiagnostic>()), sources.Files, sheets, links);
    }

    private static WebUrl DocumentUrl(string path)
    {
        if (!path.StartsWith("res://", StringComparison.Ordinal) && !path.StartsWith("user://ui/", StringComparison.Ordinal))
            throw new ArgumentException("Document path must be res:// or user://ui/.", nameof(path));
        return WebUrl.Parse(path.Replace("://", ":///", StringComparison.Ordinal)) ?? throw new ArgumentException("Invalid document URL.");
    }

    /// <summary>Prepare a fresh document together with a bounded developer source snapshot.</summary>
    public static DocumentFileReload Prepare(string path, Func<string, string> read)
    {
        var url = DocumentUrl(path);
        var sources = new Sources(read);
        var content = HtmlImporter.Import(sources.Read(path), path, url);
        var links = ReadLinks(content.Document, sources);
        var diagnostics = new List<WebDiagnostic>(content.Diagnostics);
        var sheets = ParseLinks(links, diagnostics);
        content = new HtmlImportResult(content.Document, diagnostics);
        foreach (var sheet in sheets) content.Document.StyleSheets.Add(sheet);
        RuntimeStyleProfile.Validate(content.Document);
        return new DocumentFileReload(path, content, sources.Files, sheets, links);
    }

    /// <summary>Reads only this document and its actual current linked stylesheets. No writes occur on failure.
    /// Force reload always prepares a new document; ordinary CSS changes retain the exact DOM and its listeners.</summary>
    public Update PrepareUpdate(Func<string, string> read, bool force = false)
    {
        var sources = new Sources(read);
        var markup = sources.Read(Path);
        if (force || !_sources.TryGetValue(Path, out var oldMarkup) || markup != oldMarkup)
            return new Update(this, Prepare(Path, p => p == Path ? markup : read(p)), null, null);
        var links = ReadLinks(Document, sources);
        bool changed = sources.Files.Count != _sources.Count;
        foreach (var source in sources.Files)
            if (!_sources.TryGetValue(source.Key, out var old) || old != source.Value) { changed = true; break; }
        // A runtime href/media/owner change is also a dependency change, even if the file contents match.
        if (links.Count != _links.Count || links.Count != _linkedSheets.Count) changed = true;
        for (int i = 0; !changed && i < links.Count; i++)
            if (!ReferenceEquals(links[i].Owner, _links[i].Owner) || links[i].Url.Href != _links[i].Url.Href ||
                links[i].Media != _links[i].Media) changed = true;
        if (!changed) return new Update(this, null, null, null);
        var diagnostics = new List<WebDiagnostic>();
        return new Update(this, null, ParseLinks(links, diagnostics), sources.Files, diagnostics, links);
    }

    public sealed class Update
    {
        private readonly DocumentFileReload _owner;
        private readonly List<CSSStyleSheet>? _sheets;
        private readonly Dictionary<string, string>? _sources;
        private readonly List<Link>? _links;
        public DocumentFileReload? Replacement { get; }
        public bool StylesChanged => _sheets != null;
        public IReadOnlyList<WebDiagnostic> Diagnostics { get; }
        internal Update(DocumentFileReload owner, DocumentFileReload? replacement, List<CSSStyleSheet>? sheets, Dictionary<string, string>? sources,
            IReadOnlyList<WebDiagnostic>? diagnostics = null, List<Link>? links = null)
        { _owner = owner; Replacement = replacement; _sheets = sheets; _sources = sources; _links = links; Diagnostics = diagnostics ?? Array.Empty<WebDiagnostic>(); }

        /// <summary>Commit on the owning thread. A renderer validator receives an isolated prospective document
        /// before any live sheet, source baseline, input state or geometry changes.</summary>
        public void ApplyStyles(Action<Document>? validate = null)
        {
            if (_sheets == null) return;
            validate?.Invoke(DocumentReloadPreview.Create(_owner.Document, _owner._linkedSheets, _sheets));
            var current = _owner.Document.StyleSheets;
            foreach (var old in _owner._linkedSheets)
            {
                int position = current.IndexOf(old);
                if (position < 0) { current.Remove(old); continue; }
                CSSStyleSheet? replacement = null;
                foreach (var candidate in _sheets)
                    if (ReferenceEquals(candidate.OwnerNode, old.OwnerNode)) { replacement = candidate; break; }
                if (replacement == null) current.RemoveAt(position);
                else current[position] = replacement;
            }
            foreach (var sheet in _sheets) if (!current.Contains(sheet)) current.Add(sheet);
            _owner._linkedSheets = _sheets;
            _owner._sources = _sources!;
            _owner._links = _links!;
        }
    }

    internal sealed record Link(Element Owner, string Path, WebUrl Url, string Text, string Media);
    private static List<Link> ReadLinks(Document document, Sources sources)
    {
        var links = new List<Link>();
        var url = document.BaseUrl ?? throw new InvalidOperationException("Missing document URL.");
        foreach (Element link in document.QuerySelectorAll("link"))
        {
            if (links.Count >= MaxFiles - 1) throw new InvalidOperationException("Developer reload stylesheet dependency limit exceeded.");
            if (link.GetAttribute("rel") != "stylesheet") throw new NotSupportedException("Runtime supports only stylesheet links.");
            var resolved = ResourceUrlPolicy.Default.Resolve(link.GetAttribute("href") ?? "", url, ResourceKind.Stylesheet);
            if (!resolved.Allowed || resolved.ResourcePath == null || resolved.Url == null) throw new NotSupportedException("Runtime stylesheet URL: " + resolved);
            links.Add(new Link(link, resolved.ResourcePath, resolved.Url, sources.Read(resolved.ResourcePath), link.GetAttribute("media") ?? ""));
        }
        return links;
    }
    private static List<CSSStyleSheet> ParseLinks(List<Link> links, IList<WebDiagnostic> diagnostics)
    {
        var sheets = new List<CSSStyleSheet>(links.Count);
        foreach (var link in links)
        {
            var sheet = CSSStyleSheet.Parse(link.Text, link.Path, link.Url, diagnostics);
            sheet.OwnerNode = link.Owner;
            sheet.Media.MediaText = link.Media;
            RuntimeStyleProfile.Validate(sheet);
            sheets.Add(sheet);
        }
        return sheets;
    }
    private sealed class Sources
    {
        private readonly Func<string, string> _read;
        private int _characters;
        internal readonly Dictionary<string, string> Files = new(StringComparer.Ordinal);
        internal Sources(Func<string, string> read) => _read = read;
        internal string Read(string path)
        {
            if (Files.TryGetValue(path, out var cached)) return cached;
            if (Files.Count >= MaxFiles) throw new InvalidOperationException("Developer reload file dependency limit exceeded.");
            string text = _read(path);
            if (text.Length > MaxFileCharacters || text.Length > MaxTotalCharacters - _characters)
                throw new InvalidOperationException("Developer reload source character limit exceeded.");
            _characters += text.Length;
            Files.Add(path, text);
            return text;
        }
    }
}
