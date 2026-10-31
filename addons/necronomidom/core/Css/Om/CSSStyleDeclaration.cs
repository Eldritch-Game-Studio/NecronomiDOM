using System;
using System.Collections.Generic;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values;

namespace NecronomiDom.Css.Om
{
    /// <summary>CSSOM § 6.6 declaration record; ordered longhands and case-sensitive custom properties.</summary>
    public sealed record CssDeclaration(string Name, CssValue Value, bool Important);
    /// <summary>CSSOM § 6.6 CSSStyleDeclaration mutation and property-value APIs.</summary>
    public sealed partial class CSSStyleDeclaration
    {
        private readonly List<CssDeclaration> _declarations = new List<CssDeclaration>();
        // Computed declarations are replaced wholesale with unique names; their lookups are by name, not scans.
        private Dictionary<string, int>? _computedIndex;
        private object?[] _computedText = Array.Empty<object?>();
        private readonly string? _descriptorRule;
        private readonly bool _propertyGrammar = true;
        public CSSStyleDeclaration(CSSRule? parentRule = null) { ParentRule = parentRule; }
        internal CSSStyleDeclaration(CSSRule parentRule, string descriptorRule)
        { ParentRule = parentRule; _descriptorRule = descriptorRule; _propertyGrammar = descriptorRule == "position-try"; }
        public CSSRule? ParentRule { get; internal set; }
        public Action? Changed { get; set; }
        internal Action? BeforeChange { get; set; }
        public bool ReadOnly { get; set; }
        internal bool IsComputed { get; set; }
        internal Action? BeforeRead { get; set; }
        private bool _reading;
        public int Length { get { Read(); return _declarations.Count; } }
        internal IReadOnlyList<CssDeclaration> Declarations { get { Read(); return _declarations; } }
        public IEnumerable<CssDeclaration> EnumerateLonghands() { Read(); return _declarations; }
        public string Item(int index) => index >= 0 && index < Length ? _declarations[index].Name : "";
        [System.Runtime.CompilerServices.IndexerName("Indexed")]
        public string this[int index] => Item(index);
        internal void SetComputedProperty(string name, CssValue value) => Store(new CssDeclaration(name, value, false), false);
        internal CssValue? GetSpecifiedValue(string name) { Read(); return Find(name)?.Value; }
        private void Read()
        {
            if (_reading || BeforeRead == null) return;
            _reading = true;
            try { BeforeRead(); }
            finally { _reading = false; }
        }
        public string CssFloat { get => GetPropertyValue("float"); set => SetProperty("float", value); }
        public static CSSStyleDeclaration Parse(string css, string sourcePath = "", IList<WebDiagnostic>? diagnostics = null)
        {
            var result = new CSSStyleDeclaration();
            result.Load(CssParser.ParseBlockContents(css, diagnostics, sourcePath), diagnostics, sourcePath);
            return result;
        }
        public string GetPropertyValue(string property)
        {
            Read();
            // Computed snapshots already index canonical longhands/custom names, never shorthand records.
            if (IsComputed && _computedIndex != null && _computedIndex.TryGetValue(property, out int canonical))
                return ComputedText(canonical);
            property = Normalize(property);
            var definition = _propertyGrammar ? CssPropertyRegistry.Get(property) : null;
            if (definition?.IsShorthand == true) return ShorthandText(property, definition);
            if (_computedIndex != null && _computedIndex.TryGetValue(property, out int index)) return ComputedText(index);
            return Find(property)?.Value.Serialize() ?? "";
        }
        private string ComputedText(int index)
        {
            var value = _declarations[index].Value;
            if (CacheableComputedText(value))
                return _computedText[index] as string ?? (string)(_computedText[index] = value.Serialize());
            if (value.GetType() == typeof(CssListValue)) return FlatComputedText(index, (CssListValue)value);
            return value.Serialize();
        }
        private string ShorthandText(string property, PropertyDefinition definition)
        {
            // Keep the capturing callback off the longhand path: its display class otherwise allocates on every read.
            if (!SamePriority(Longhands(definition), out _)) return "";
            return ShorthandSerializer.TrySerialize(property, name => ShorthandValue(property, name), out var value) ? value : "";
        }
        private static bool CacheableComputedText(CssValue value) =>
            value is CssKeywordValue or CssStringValue or CssUrlValue or Values.Colors.CssAbsoluteColorValue ||
            value.GetType() == typeof(CssNumberValue) || value.GetType() == typeof(CssIntegerValue) ||
            value.GetType() == typeof(CssDimensionValue) || value.GetType() == typeof(CssPercentageValue);
        private sealed class ComputedListText(CssListValue value, string text)
        {
            internal readonly CssListValue Value = value;
            internal readonly CssValue[] Items = new CssValue[value.Items.Count];
            internal string Text = text;
        }
        private string FlatComputedText(int index, CssListValue value)
        {
            var items = value.Items;
            // Known backing only: custom indexers and derived serializers may be live.
            Type type = items.GetType();
            if (type != typeof(CssValue[]) && type != typeof(List<CssValue>)) return value.Serialize();
            var cached = _computedText[index] as ComputedListText;
            bool sameShape = cached != null && ReferenceEquals(cached.Value, value) && cached.Items.Length == items.Count;
            bool unchanged = sameShape;
            for (int i = 0; i < items.Count; i++)
            {
                // Nested lists and Typed OM slots are deliberately excluded, not recursively cached.
                if (!CacheableComputedText(items[i])) { _computedText[index] = null; return value.Serialize(); }
                if (sameShape && !ReferenceEquals(cached!.Items[i], items[i])) unchanged = false;
            }
            if (unchanged) return cached!.Text;
            string text = value.Serialize();
            if (!sameShape) _computedText[index] = cached = new ComputedListText(value, text);
            else cached!.Text = text;
            for (int i = 0; i < items.Count; i++) cached!.Items[i] = items[i];
            return text;
        }
        private CssValue? ShorthandValue(string shorthand, string name)
        {
            CssValue? value = Find(name)?.Value;
            // In computed CSSOM the initial currentcolor is already the computed element color.
            if (IsComputed && shorthand == "text-decoration" && name == "text-decoration-color" && value != null &&
                Find("color")?.Value is { } color && value.Serialize() == color.Serialize())
                return new CssKeywordValue("currentcolor");
            return value;
        }
        public string GetPropertyPriority(string property)
        {
            Read();
            property = Normalize(property);
            var definition = _propertyGrammar ? CssPropertyRegistry.Get(property) : null;
            if (definition?.IsShorthand == true) return SamePriority(Longhands(definition), out var important) && important ? "important" : "";
            return Find(property)?.Important == true ? "important" : "";
        }
        public void SetProperty(string property, string? value, string? priority = "")
        {
            EnsureWritable();
            property = Normalize(property);
            if (_propertyGrammar && !property.StartsWith("--", StringComparison.Ordinal) && CssPropertyRegistry.Get(property) == null) return;
            if (string.IsNullOrEmpty(value)) { RemoveProperty(property); return; }
            if (!string.IsNullOrEmpty(priority) && !Dom.Names.AsciiEqual(priority, "important")) return;
            var declaration = CssParser.ParseDeclaration(CssSerialization.SerializeIdentifier(property) + ":" + value);
            if (declaration == null || declaration.Important) return;
            if (Apply(declaration with { Important = !string.IsNullOrEmpty(priority) }, false, out var changed, true) && changed) Changed?.Invoke();
        }
        public string RemoveProperty(string property)
        {
            EnsureWritable();
            property = Normalize(property);
            var previous = GetPropertyValue(property);
            var definition = _propertyGrammar ? CssPropertyRegistry.Get(property) : null;
            var changed = false;
            if (definition?.IsShorthand == true) foreach (var name in Longhands(definition)) changed |= Remove(name, !changed);
            else changed = Remove(property, true);
            if (changed) Changed?.Invoke();
            return previous;
        }
        internal void Load(CssBlockContents contents, IList<WebDiagnostic>? diagnostics = null, string sourcePath = "")
        {
            foreach (var item in contents.Items)
            {
                if (item is not RawDeclaration declaration) continue;
                if (!Apply(declaration, true, out _)) diagnostics?.Add(new WebDiagnostic(WebDiagnosticSeverity.Warning, sourcePath, declaration.Line, declaration.Column, "css-invalid-declaration", "Unknown property or invalid value for " + declaration.Name + "."));
            }
        }
        private bool Apply(RawDeclaration declaration, bool preserveImportant, out bool changed, bool notifyBefore = false)
        {
            changed = false;
            if (ParentRule is CSSKeyframeRule && declaration.Important) return false;
            if (!_propertyGrammar)
            {
                if (declaration.Important || !CssDescriptorParser.TryParse(_descriptorRule!, declaration.Name, declaration.Value, out var descriptor) || descriptor == null) return false;
                changed = Store(new CssDeclaration(declaration.Name, descriptor, false), false, notifyBefore);
                return true;
            }
            if (_descriptorRule == "position-try" && (declaration.Important || !CssDescriptorParser.IsPositionTryProperty(declaration.Name))) return false;
            var parsed = CssPropertyParser.Parse(declaration.Name, declaration.Value);
            if (parsed == null) return false;
            foreach (var (name, value) in parsed.Longhands) changed |= Store(new CssDeclaration(name, value, declaration.Important), preserveImportant, notifyBefore && !changed);
            return true;
        }
        private bool Store(CssDeclaration declaration, bool preserveImportant, bool notifyBefore = false)
        {
            _computedIndex = null;
            for (var i = 0; i < _declarations.Count; i++)
            {
                var previous = _declarations[i];
                if (previous.Name != declaration.Name) continue;
                if (preserveImportant && previous.Important && !declaration.Important) return false;
                var append = false;
                for (var j = i + 1; j < _declarations.Count; j++)
                    if (MappingConflicts(declaration.Name, _declarations[j].Name)) { append = true; break; }
                if (append) { if (notifyBefore) BeforeChange?.Invoke(); _declarations.RemoveAt(i); _declarations.Add(declaration); return true; }
                if (previous.Important == declaration.Important && SameValue(previous.Value, declaration.Value)) return false;
                if (notifyBefore) BeforeChange?.Invoke();
                _declarations[i] = declaration;
                return true;
            }
            if (notifyBefore) BeforeChange?.Invoke();
            _declarations.Add(declaration);
            return true;
        }
        private static bool SameValue(CssValue first, CssValue second)
        {
            if (ReferenceEquals(first, second)) return true;
            if (first is CssNumberValue number)
                return second is CssNumberValue otherNumber && number.Value.Equals(otherNumber.Value) && number.IsInteger == otherNumber.IsInteger;
            if (second is CssNumberValue) return false;
            if (first is CssSystemFontValue font)
                return second is CssSystemFontValue otherFont && font.SystemFont == otherFont.SystemFont && font.Longhand == otherFont.Longhand;
            if (second is CssSystemFontValue) return false;
            if (first is CssPendingSubstitutionValue pending)
                return second is CssPendingSubstitutionValue otherPending && pending.Shorthand == otherPending.Shorthand && pending.SourceText == otherPending.SourceText;
            if (second is CssPendingSubstitutionValue) return false;
            return first.Serialize() == second.Serialize();
        }
        private static bool MappingConflicts(string first, string second)
        {
            var group = CssPropertyRegistry.Get(first)?.LogicalGroup;
            return group != null && group == CssPropertyRegistry.Get(second)?.LogicalGroup && Logical(first) != Logical(second);
        }
        private static bool Logical(string property) => property.Contains("inline", StringComparison.Ordinal) || property.Contains("block", StringComparison.Ordinal) ||
            property.Contains("-start", StringComparison.Ordinal) || property.Contains("-end", StringComparison.Ordinal);
        private CssDeclaration? Find(string name)
        {
            if (_computedIndex != null) return _computedIndex.TryGetValue(name, out int index) ? _declarations[index] : null;
            foreach (var declaration in _declarations) if (declaration.Name == name) return declaration;
            return null;
        }
        private bool Remove(string name, bool notifyBefore = false)
        {
            _computedIndex = null;
            for (var i = 0; i < _declarations.Count; i++)
                if (_declarations[i].Name == name) { if (notifyBefore) BeforeChange?.Invoke(); _declarations.RemoveAt(i); return true; }
            return false;
        }
        private bool SamePriority(IReadOnlyList<string> names, out bool important)
        {
            important = false;
            if (names.Count == 0) return false;
            for (var i = 0; i < names.Count; i++)
            {
                var declaration = Find(names[i]);
                if (declaration == null || (i > 0 && declaration.Important != important)) return false;
                important = declaration.Important;
            }
            return true;
        }
        private string Normalize(string property)
        {
            if (property.StartsWith("--", StringComparison.Ordinal)) return property;
            property = Dom.Names.AsciiLower(property);
            return _propertyGrammar ? CssPropertyRegistry.Get(property)?.Name ?? property : property;
        }
        private void EnsureWritable() { if (ReadOnly) throw new Dom.DomException("Declaration is read-only.", "NoModificationAllowedError"); }
    }
}
