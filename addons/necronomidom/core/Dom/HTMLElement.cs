using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace NecronomiDom.Dom
{
    /// <summary>HTML § 3.2.3 HTMLElement global attribute reflections (no scripting).</summary>
    public partial class HTMLElement : Element
    {
        private DOMStringMap? _dataset;
        protected internal HTMLElement(Document doc, string local) : base(doc, Names.Html, local) => doc.CustomElements.InitializeConstruction(this);
        public string Title { get => GetAttribute("title") ?? ""; set => SetAttribute("title", value); }
        public string Lang { get => GetAttribute("lang") ?? ""; set => SetAttribute("lang", value); }
        public string Dir
        {
            get { var v = GetAttribute("dir"); return Names.AsciiEqual(v, "ltr") ? "ltr" : Names.AsciiEqual(v, "rtl") ? "rtl" : Names.AsciiEqual(v, "auto") ? "auto" : ""; }
            set => SetAttribute("dir", value);
        }
        public bool Hidden { get => HasAttribute("hidden"); set => ToggleAttribute("hidden", value); }
        public bool Inert { get => HasAttribute("inert"); set => ToggleAttribute("inert", value); }
        public int TabIndex
        {
            get => int.TryParse(GetAttribute("tabindex"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : LocalName is "input" or "select" or "textarea" or "button" or "summary" || LocalName == "a" && HasAttribute("href") ? 0 : -1;
            set => SetAttribute("tabindex", value.ToString(CultureInfo.InvariantCulture));
        }
        public string AccessKey { get => GetAttribute("accesskey") ?? ""; set => SetAttribute("accesskey", value); }
        public bool Translate
        {
            get { var v = GetAttribute("translate"); return v == "" || Names.AsciiEqual(v, "yes") ? true : Names.AsciiEqual(v, "no") ? false : (ParentElement as HTMLElement)?.Translate ?? true; }
            set => SetAttribute("translate", value ? "yes" : "no");
        }
        public string? Popover
        {
            get { var v = GetAttribute("popover"); return v == null ? null : Names.AsciiEqual(v, "manual") ? "manual" : Names.AsciiEqual(v, "hint") ? "hint" : "auto"; }
            set { if (value == null) RemoveAttribute("popover"); else SetAttribute("popover", value); }
        }
        public DOMStringMap Dataset => _dataset ??= new DOMStringMap(this);
    }
    /// <summary>HTML § 3.2.6.6 DOMStringMap data-* name conversion and reflection.</summary>
    public sealed class DOMStringMap
    {
        private readonly Element _element;
        internal DOMStringMap(Element element) => _element = element;
        public IEnumerable<string> Keys
        {
            get
            {
                foreach (var a in _element.Attributes)
                {
                    if (a.NamespaceURI != null || !a.Name.StartsWith("data-", StringComparison.Ordinal)) continue;
                    var tail = a.Name.Substring(5); var upper = false;
                    foreach (var c in tail) if (c is >= 'A' and <= 'Z') upper = true;
                    if (upper) continue;
                    var b = new StringBuilder();
                    for (var i = 0; i < tail.Length; i++)
                    { if (tail[i] == '-' && i + 1 < tail.Length && tail[i + 1] is >= 'a' and <= 'z') b.Append((char)(tail[++i] - 32)); else b.Append(tail[i]); }
                    yield return b.ToString();
                }
            }
        }
        private static string Attribute(string key)
        {
            var b = new StringBuilder("data-");
            for (var i = 0; i < key.Length; i++)
            {
                var c = key[i];
                if (c == '-' && i + 1 < key.Length && key[i + 1] is >= 'a' and <= 'z') throw DomException.Syntax("Dataset key contains hyphen followed by lowercase ASCII.");
                if (c is >= 'A' and <= 'Z') b.Append('-').Append((char)(c + 32)); else b.Append(c);
            }
            return b.ToString();
        }
        private static bool CanMap(string key)
        {
            for (var i = 0; i + 1 < key.Length; i++)
                if (key[i] == '-' && key[i + 1] is >= 'a' and <= 'z') return false;
            return true;
        }
        public string? this[string key]
        {
            get => CanMap(key) ? _element.GetAttribute(Attribute(key)) : null;
            set => _element.SetAttribute(Attribute(key), value ?? "null");
        }
        public bool Remove(string key)
        {
            if (!CanMap(key)) return false;
            var name = Attribute(key); var had = _element.HasAttribute(name); _element.RemoveAttribute(name); return had;
        }
    }
}
