using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using NecronomiDom.Css.Media;
using NecronomiDom.Css.Om;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values;
using NecronomiDom.Dom;

namespace NecronomiDom.Css.Cascade
{
    /// <summary>CSS Cascade 5/6 style resolution over the NecronomiDOM DOM, with live read-only CSSOM views.</summary>
    public sealed partial class StyleEngine : IDomChangeSink
    {
        /// <summary>Diagnostic full-reconciliation reference; never changes cascade results.</summary>
        public bool IncrementalReconciliation { get; init; } = true;
        private sealed class Entry
        {
            private readonly StyleEngine _owner;
            internal readonly Element Element;
            internal readonly string? Pseudo;
            internal readonly CSSStyleDeclaration Declaration = new();
            internal CSSStyleDeclaration? ComputedDeclaration;
            internal Dictionary<string, CssValue> Values = new(StringComparer.Ordinal);
            // Scratch belongs to this entry: ancestor refreshes may recurse while computing a child.
            internal readonly Dictionary<string, CssValue> Specified = new(StringComparer.Ordinal);
            internal ComputedReuse? ComputedReuse;
            internal readonly Dictionary<string, IReadOnlyList<ComponentValue>> RawCustom = new(StringComparer.Ordinal);
            internal readonly Dictionary<string, CssValue> PassedCustom = new(StringComparer.Ordinal);
            internal CustomPropertyResolver? Variables;
            internal Dictionary<(CssPendingSubstitutionValue, string), ParsedProperty?>? SubstitutedDeclarations;
            internal readonly List<CssDeclaration> Projection = new();
            internal readonly RuleCollection Rules = new();
            internal readonly Dictionary<string, List<Candidate>> Groups = new(StringComparer.Ordinal);
            internal readonly List<List<Candidate>> GroupBuffers = new();
            internal CssValue? InheritedStrokeColor;
            internal CssValue? InheritedTextShadow;
            internal Element? ForegroundParent;
            internal bool ForegroundInherited;
            internal CssValue? ForegroundExpression;
            internal long Generation = -1;
            internal bool Refreshing;
            internal bool InlineGeometryOnly, ReadsParentGeometry;
            internal long GeometryRefreshGeneration = -1;
            internal bool InlineOpacityChanged, InlineTransformChanged;
            internal bool ReuseRules;
            internal long RuleGeneration = -1;
            internal List<GeometryInlineValue>? GeometryInline;
            internal Entry(StyleEngine owner, Element element, string? pseudo)
            {
                _owner = owner; Element = element; Pseudo = pseudo;
                Declaration.ReadOnly = true; Declaration.IsComputed = true;
                Declaration.BeforeRead = Read;
            }
            private void Read() => _owner.Refresh(this);
        }
        private sealed class EntrySet
        {
            internal Entry? Normal;
            internal readonly Dictionary<string, Entry> Pseudos = new(StringComparer.Ordinal);
            internal void MarkStale(bool reuseRules = false, long generation = -1)
            {
                if (Normal != null)
                {
                    Normal.ReuseRules = reuseRules && Normal.RuleGeneration == generation &&
                        (Normal.Generation == generation || Normal.ReuseRules);
                    Normal.Generation = -1;
                    Normal.GeometryRefreshGeneration = -1;
                    Normal.InlineOpacityChanged = false;
                    Normal.InlineTransformChanged = false;
                }
                foreach (var pseudo in Pseudos.Values)
                {
                    pseudo.ReuseRules = reuseRules && pseudo.RuleGeneration == generation &&
                        (pseudo.Generation == generation || pseudo.ReuseRules);
                    pseudo.Generation = -1;
                }
            }
        }
        private sealed record SheetSource(CSSStyleSheet Sheet, Node Root, Element? Owner, int Depth);
        private static readonly Lazy<CSSStyleSheet> UserAgentSheet = new(LoadUserAgentSheet);
        private readonly Document _document;
        private readonly IDomChangeSink? _previousSink;
        private readonly ConditionalWeakTable<Element, EntrySet> _entries = new();
        private readonly List<SheetSource> _sources = new();
        private readonly Dictionary<CSSStyleSheet, long> _revisions = new();
        private long _generation;
        private long _observedSheetEpoch = -1;
        private bool _sourcesDirty = true;
        private MediaEnvironment _media = new();
        public StyleEngine(Document document)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
            _observeViewportUnit = ObserveViewportUnit;
            _previousSink = document.ChangeSink;
            document.ChangeSink = this;
        }
        /// <summary>Assigning an environment re-cascades every element; hosts assign only on an actual viewport change.</summary>
        public MediaEnvironment Media
        {
            get => _media;
            set { _media = value ?? throw new ArgumentNullException(nameof(value)); Invalidate(); }
        }
        /// <summary>Eligible container query environment supplied by layout; null means that no matching container exists.</summary>
        public Func<Element, string?, MediaEnvironment?>? ContainerEnvironment { get; set; }
        /// <summary>Real font/system-color metrics supplied by the host; absent layout/font bases remain unresolved.</summary>
        public Func<Element, ComputedValueContext>? ValueContext { get; set; }
        public IDictionary<string, string> EnvironmentVariables { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
        /// <summary>Re-cascades every element and rebuilds the style sources.</summary>
        public void Invalidate() { RestyleAll(); _sourcesDirty = true; }
        /// <summary>Number of element style recomputations so far (diagnostics and invalidation tests).</summary>
        internal long ComputedEntryCount { get; private set; }
        /// <summary>Context-free substituted declaration parses, excluding immutable-token cache hits.</summary>
        internal long SubstitutionParseCount { get; private set; }
        public CSSStyleDeclaration GetComputedStyle(Element element, string? pseudoElement = null)
        {
            ArgumentNullException.ThrowIfNull(element);
            string? pseudo = string.IsNullOrEmpty(pseudoElement) ? null : pseudoElement.TrimStart(':');
            var entries = _entries.GetValue(element, static _ => new EntrySet());
            Entry? entry = pseudo == null ? entries.Normal : entries.Pseudos.GetValueOrDefault(pseudo);
            if (entry == null)
            {
                entry = new Entry(this, element, pseudo);
                if (pseudo == null) entries.Normal = entry; else entries.Pseudos.Add(pseudo, entry);
            }
            Refresh(entry);
            return entry.Declaration;
        }
        private void Refresh(Entry entry)
        {
            if (entry.Refreshing) return;
            EnsureSources();
            if (_pendingChanges.Count != 0) FlushChanges();
            if (entry.Generation == _generation) return;
            ComputedEntryCount++;
            entry.Refreshing = true;
            try
            {
                if (entry.GeometryRefreshGeneration == _generation && entry.Element.IsConnected && TryRefreshGeometry(entry))
                {
                    entry.Generation = _generation;
                    entry.GeometryRefreshGeneration = -1;
                    return;
                }
                entry.GeometryRefreshGeneration = -1;
                entry.InlineOpacityChanged = false;
                entry.InlineTransformChanged = false;
                BeginComputedRefresh(entry);
                entry.InheritedStrokeColor = null;
                entry.InheritedTextShadow = null;
                entry.ForegroundParent = null;
                entry.ForegroundInherited = false;
                entry.ForegroundExpression = null;
                if (entry.Element.IsConnected) Compute(entry);
                entry.Declaration.ReplaceComputed(Declarations(entry, resolved: true));
                entry.ComputedDeclaration?.ReplaceComputed(Declarations(entry, resolved: false));
                entry.Generation = _generation;
            }
            finally { entry.Refreshing = false; }
        }
        private void EnsureSources()
        {
            long epoch = CSSStyleSheet.MutationEpoch;
            if (IncrementalReconciliation && !_sourcesDirty && _observedSheetEpoch == epoch) return;
            _observedSheetEpoch = epoch;
            bool revised = false;
            if (!_sourcesDirty)
            {
                foreach (var pair in _revisions)
                    if (pair.Key.Revision != pair.Value) { _sourcesDirty = revised = true; break; }
            }
            if (!_sourcesDirty) return;
            _sourcesDirty = false;
            _previousSources.Clear();
            _previousSources.AddRange(_sources);
            bool hadShadowTrees = _hasShadowTrees;
            _hasShadowTrees = false;
            _sources.Clear();
            _revisions.Clear();
            _scopeMedia.Clear();
            AddSource(UserAgentSheet.Value, _document, null, 0);
            AddTreeSheets((DocumentStyleSheetList)_document.StyleSheets, _document, 0);
            AddSheets(_document.AdoptedStyleSheets, _document, 0);
            foreach (Node node in _document.ShadowIncludingInclusiveDescendants())
                if (node is ShadowRoot shadow)
                {
                    _hasShadowTrees = true;
                    int depth = Depth(shadow);
                    AddTreeSheets((DocumentStyleSheetList)shadow.StyleSheets, shadow, depth);
                    AddSheets(shadow.AdoptedStyleSheets, shadow, depth);
                }
            // Structural changes rebuild the sources; only a different sheet set or edited rules re-cascade everything.
            if (!revised && hadShadowTrees == _hasShadowTrees && SameSources()) return;
            _observedMediaMatches.Clear();
            RestyleAll();
            ScanInvalidationFeatures();
        }
        private bool SameSources()
        {
            if (_previousSources.Count != _sources.Count) return false;
            for (int i = 0; i < _sources.Count; i++) if (!_previousSources[i].Equals(_sources[i])) return false;
            return true;
        }
        private void AddTreeSheets(DocumentStyleSheetList list, Node root, int depth)
        {
            IReadOnlyList<CSSStyleSheet> sheets = list.Snapshot();
            for (int i = 0; i < sheets.Count; i++) AddSource(sheets[i], root, sheets[i].OwnerNode as Element, depth);
        }
        private void AddSheets(IList<CSSStyleSheet> sheets, Node root, int depth)
        { for (int i = 0; i < sheets.Count; i++) AddSource(sheets[i], root, sheets[i].OwnerNode as Element, depth); }
        private void AddSource(CSSStyleSheet sheet, Node root, Element? owner, int depth)
        {
            foreach (var source in _sources) if (ReferenceEquals(source.Sheet, sheet) && ReferenceEquals(source.Root, root)) return;
            _sources.Add(new SheetSource(sheet, root, owner, depth));
            TrackImports(sheet);
        }
        private void TrackImports(CSSStyleSheet sheet)
        {
            if (!_revisions.TryAdd(sheet, sheet.Revision)) return;
            foreach (CSSRule rule in sheet.CssRules)
                if (rule is CSSImportRule { StyleSheet: { } imported }) TrackImports(imported);
        }
        private static int Depth(Node root)
        { int depth = 0; while (root is ShadowRoot shadow) { depth++; root = shadow.Host.Root; } return depth; }
        private static CSSStyleSheet LoadUserAgentSheet()
        {
            using Stream stream = typeof(StyleEngine).Assembly.GetManifestResourceStream("NecronomiDom.user-agent.css")
                ?? throw new InvalidOperationException("Missing HTML user-agent stylesheet.");
            using var reader = new StreamReader(stream);
            var sheet = CSSStyleSheet.Parse(reader.ReadToEnd(), "html-standard:user-agent", null, null);
            sheet.Origin = CssOrigin.UserAgent;
            return sheet;
        }
        bool IDomChangeSink.RequiresAttributeOldValue => _previousSink?.RequiresAttributeOldValue == true;
        // Structure may add or remove style sources; EnsureSources re-cascades everything only if the sheet set changed.
        void IDomChangeSink.ChildrenChanged(Node parent) { _sourcesDirty = true; RecordChange(parent, ChangeKind.Children); _previousSink?.ChildrenChanged(parent); }
        void IDomChangeSink.AttributeChanged(Element e, string localName, string? oldValue)
        {
            // Style and link attributes select, type or scope a whole sheet.
            if (e is HTMLStyleElement || e.LocalName == "link") Invalidate();
            else RecordChange(e, localName == ":state" ? ChangeKind.State :
                localName == "style" ? ChangeKind.InlineStyle : ChangeKind.Element);
            _previousSink?.AttributeChanged(e, localName, oldValue);
        }
        void IDomChangeSink.CharacterDataChanged(CharacterData n)
        {
            if (n.ParentNode is HTMLStyleElement) Invalidate();
            else if (n.ParentNode is Element parent) RecordChange(parent, ChangeKind.Children);
            _previousSink?.CharacterDataChanged(n);
        }
        void IDomChangeSink.NodeInserted(Node n) { _sourcesDirty = true; RecordChange(n, ChangeKind.Inserted); _previousSink?.NodeInserted(n); }
        void IDomChangeSink.NodeRemoved(Node n, Node oldParent) { _sourcesDirty = true; RecordChange(n, ChangeKind.Removed, oldParent); _previousSink?.NodeRemoved(n, oldParent); }
    }
}
