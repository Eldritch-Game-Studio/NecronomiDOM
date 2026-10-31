using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NecronomiDom.Dom
{
    /// <summary>HTML § 4.13 C# custom element name metadata, read once at explicit attributed registration.</summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class CustomElementAttribute : Attribute
    {
        public CustomElementAttribute(string name) => Name = name;
        public string Name { get; }
    }
    /// <summary>HTML § 4.13.3 lifecycle, observed-attribute and disabled-internals metadata preserving existing DOM identity on upgrade.</summary>
    public sealed class CustomElementDefinition
    {
        private readonly HashSet<string> _observed;
        public CustomElementDefinition(Func<Document, string, HTMLElement> factory, IEnumerable<string>? observedAttributes = null,
            Action<Element>? connected = null, Action<Element>? disconnected = null,
            Action<Element, Document, Document>? adopted = null,
            Action<Element, string, string?, string?, string?>? attributeChanged = null,
            Action<Element>? upgraded = null, bool disableInternals = false)
        { Factory = factory; _observed = observedAttributes == null ? new() : new(observedAttributes, StringComparer.Ordinal); ConnectedCallback = connected; DisconnectedCallback = disconnected; AdoptedCallback = adopted; AttributeChangedCallback = attributeChanged; UpgradedCallback = upgraded; DisableInternals = disableInternals; }
        public Func<Document, string, HTMLElement> Factory { get; }
        public IReadOnlyCollection<string> ObservedAttributes => _observed;
        public Action<Element>? ConnectedCallback { get; }
        public Action<Element>? DisconnectedCallback { get; }
        public Action<Element, Document, Document>? AdoptedCallback { get; }
        public Action<Element, string, string?, string?, string?>? AttributeChangedCallback { get; }
        public Action<Element>? UpgradedCallback { get; }
        /// <summary>HTML § 4.13 custom definition's disabled internals feature.</summary>
        public bool DisableInternals { get; }
        internal bool Observes(string name) => AttributeChangedCallback != null && _observed.Contains(name);
        internal void Connected(Element e) { if (ConnectedCallback != null) e.NodeDocument.EnqueueReaction(() => { if (ReferenceEquals(e.CustomDefinition, this)) ConnectedCallback(e); }); }
        internal void Disconnected(Element e) { if (DisconnectedCallback != null) e.NodeDocument.EnqueueReaction(() => { if (ReferenceEquals(e.CustomDefinition, this)) DisconnectedCallback(e); }); }
        internal void Adopted(Element e, Document old, Document current) { if (AdoptedCallback != null) current.EnqueueReaction(() => { if (ReferenceEquals(e.CustomDefinition, this)) AdoptedCallback(e, old, current); }); }
        internal void Attribute(Element e, string name, string? old, string? value, string? ns)
        { if (Observes(name)) e.NodeDocument.EnqueueReaction(() => { if (ReferenceEquals(e.CustomDefinition, this)) AttributeChangedCallback!(e, name, old, value, ns); }); }
    }
    /// <summary>HTML § 4.13.4 autonomous custom element registry and synchronous C# reactions.</summary>
    public sealed class CustomElementRegistry
    {
        private readonly Document _document;
        private readonly Dictionary<string, CustomElementDefinition> _definitions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TaskCompletionSource<CustomElementDefinition>> _waiting = new(StringComparer.Ordinal);
        private string? _constructingName;
        private CustomElementDefinition? _constructingDefinition;
        private HTMLElement? _constructingElement;
        internal CustomElementRegistry(Document document) => _document = document;
        public void Define(string name, CustomElementDefinition definition)
        {
            using var reactions = _document.Reactions();
            if (!IsCustomName(name)) throw DomException.Syntax("Invalid custom element name.");
            if (_definitions.ContainsKey(name)) throw DomException.NotSupported("Custom element name is already registered.");
            foreach (var existing in _definitions.Values) if (ReferenceEquals(existing.Factory, definition.Factory)) throw DomException.NotSupported("Factory is already registered.");
            _definitions.Add(name, definition); Upgrade(_document);
            if (_waiting.Remove(name, out var pending)) pending.SetResult(definition);
        }
        /// <summary>Registers a C# definition under its class's CustomElementAttribute name; no reflection runs during DOM mutation.</summary>
        public void Define<T>(CustomElementDefinition definition) where T : HTMLElement
        {
            var metadata = Attribute.GetCustomAttribute(typeof(T), typeof(CustomElementAttribute), false) as CustomElementAttribute;
            if (metadata == null) throw new ArgumentException("Custom element class has no name metadata.", nameof(T));
            Define(metadata.Name, definition);
        }
        public CustomElementDefinition? Get(string name) => _definitions.GetValueOrDefault(name);
        public Task<CustomElementDefinition> WhenDefined(string name)
        {
            if (!IsCustomName(name)) return Task.FromException<CustomElementDefinition>(DomException.Syntax("Invalid custom element name."));
            if (Get(name) is { } definition) return Task.FromResult(definition);
            if (!_waiting.TryGetValue(name, out var pending)) { pending = new(TaskCreationOptions.RunContinuationsAsynchronously); _waiting.Add(name, pending); }
            return pending.Task;
        }
        public void Upgrade(Node root)
        {
            using var reactions = root.NodeDocument.Reactions();
            foreach (var node in root.ShadowIncludingInclusiveDescendants()) if (node is Element e) TryUpgrade(e);
        }
        internal void TryUpgrade(Element element)
        {
            if (element.NamespaceURI != Names.Html || element.CustomFailed || element.CustomDefinition != null || Get(element.LocalName) is not { } definition) return;
            element.CustomDefinition = definition; element.NotifyStateChanged();
            if (definition.UpgradedCallback != null) element.NodeDocument.EnqueueReaction(() =>
            {
                try { definition.UpgradedCallback(element); }
                catch (Exception) { element.CustomDefinition = null; element.CustomFailed = true; element.NotifyStateChanged(); throw; }
            });
            foreach (var attr in element.Attributes) definition.Attribute(element, attr.LocalName, null, attr.Value, attr.NamespaceURI);
            if (element.IsConnected) definition.Connected(element);
        }
        internal HTMLElement? Create(string name)
        {
            if (Get(name) is not { } definition) return null;
            var previousName = _constructingName; var previousDefinition = _constructingDefinition; var previousElement = _constructingElement;
            _constructingName = name; _constructingDefinition = definition; _constructingElement = null;
            try
            {
                var element = definition.Factory(_document, name);
                if (!ReferenceEquals(element.OwnerDocument, _document) || element.LocalName != name || element.NamespaceURI != Names.Html || element.ParentNode != null || element.HasAttributes() || element.HasChildNodes())
                    throw DomException.NotSupported("Custom element factory must return an empty, unattached element with the requested name and document.");
                element.CustomDefinition = definition; element.CustomConstructing = false; element.NotifyStateChanged(); return element;
            }
            catch
            {
                if (_constructingElement is { } failed) { failed.CustomDefinition = null; failed.CustomConstructing = false; failed.CustomFailed = true; failed.NotifyStateChanged(); }
                throw;
            }
            finally { _constructingName = previousName; _constructingDefinition = previousDefinition; _constructingElement = previousElement; }
        }
        internal void InitializeConstruction(HTMLElement element)
        {
            if (_constructingDefinition == null || element.LocalName != _constructingName) return;
            _constructingElement = element;
            element.CustomDefinition = _constructingDefinition; element.CustomConstructing = true;
        }
        internal static bool IsCustomName(string name)
        {
            if (name.Length == 0 || name[0] is < 'a' or > 'z' || !name.Contains('-') || name is "annotation-xml" or "color-profile" or "font-face" or "font-face-src" or "font-face-uri" or "font-face-format" or "font-face-name" or "missing-glyph") return false;
            for (var i = 0; i < name.Length; i++)
            {
                var c = name[i];
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 >= name.Length || !char.IsLowSurrogate(name[i + 1])) return false;
                    if (char.ConvertToUtf32(c, name[++i]) > 0xEFFFF) return false;
                    continue;
                }
                if (!(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '.' or '_' or '\u00B7'
                    or >= '\u00C0' and <= '\u00D6' or >= '\u00D8' and <= '\u00F6'
                    or >= '\u00F8' and <= '\u037D' or >= '\u037F' and <= '\u1FFF'
                    or >= '\u200C' and <= '\u200D' or >= '\u203F' and <= '\u2040'
                    or >= '\u2070' and <= '\u218F' or >= '\u2C00' and <= '\u2FEF'
                    or >= '\u3001' and <= '\uD7FF' or >= '\uF900' and <= '\uFDCF'
                    or >= '\uFDF0' and <= '\uFFFD')) return false;
            }
            return true;
        }
    }
}
