using System;
using System.Collections;
using System.Collections.Generic;

namespace NecronomiDom.Dom
{
    /// <summary>DOM § 7.1 live ordered-set token reflection and mutation.</summary>
    public sealed class DOMTokenList : IEnumerable<string>
    {
        private readonly Element _element;
        private readonly string _attribute;
        internal DOMTokenList(Element element, string attribute) { _element = element; _attribute = attribute; }
        public string Value { get => _element.GetAttribute(_attribute) ?? ""; set => _element.SetAttribute(_attribute, value); }
        public int Length => Parse(Value).Count;
        public string? Item(int index) { var tokens = Parse(Value); return index >= 0 && index < tokens.Count ? tokens[index] : null; }
        [System.Runtime.CompilerServices.IndexerName("Indexed")]
        public string? this[int index] => Item(index);
        public bool Contains(string token) => Parse(Value).Contains(token);
        private static void Validate(string token)
        {
            if (token.Length == 0) throw DomException.Syntax("Empty token.");
            foreach (var c in token) if (IsSpace(c)) throw DomException.InvalidCharacter("Whitespace in token.");
        }
        private void Update(List<string> tokens)
        { if (tokens.Count == 0 && !_element.HasAttribute(_attribute)) return; Value = string.Join(" ", tokens); }
        public void Add(params string[] tokens)
        {
            foreach (var token in tokens) Validate(token);
            var set = Parse(Value); foreach (var token in tokens) if (!set.Contains(token)) set.Add(token); Update(set);
        }
        public void Remove(params string[] tokens)
        {
            foreach (var token in tokens) Validate(token);
            var set = Parse(Value); foreach (var token in tokens) set.Remove(token); Update(set);
        }
        public bool Toggle(string token, bool? force = null)
        {
            Validate(token); var tokens = Parse(Value); var contains = tokens.Contains(token);
            if (contains) { if (force != true) { tokens.Remove(token); Update(tokens); return false; } return true; }
            if (force != false) { tokens.Add(token); Update(tokens); return true; } return false;
        }
        public bool Replace(string token, string newToken)
        {
            Validate(token); Validate(newToken); var set = Parse(Value); var index = set.IndexOf(token);
            if (index < 0) return false;
            var existing = set.IndexOf(newToken);
            if (existing == index) { Update(set); return true; }
            if (existing < 0) set[index] = newToken;
            else { set.RemoveAt(Math.Max(index, existing)); set[Math.Min(index, existing)] = newToken; }
            Update(set); return true;
        }
        public bool Supports(string token)
        {
            if (_attribute != "rel") throw new DomTypeError("This attribute has no supported tokens.");
            return Names.AsciiLower(token) is "stylesheet" or "alternate" or "preload" or "icon";
        }
        internal static bool IsSpace(char c) => c is ' ' or '\t' or '\n' or '\r' or '\f';
        internal static List<string> Parse(string text)
        {
            var result = new List<string>(); var start = 0;
            while (start < text.Length)
            {
                while (start < text.Length && IsSpace(text[start])) start++;
                var end = start; while (end < text.Length && !IsSpace(text[end])) end++;
                if (end > start) { var token = text.Substring(start, end - start); if (!result.Contains(token)) result.Add(token); }
                start = end;
            }
            return result;
        }
        public IEnumerator<string> GetEnumerator() => Parse(Value).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
