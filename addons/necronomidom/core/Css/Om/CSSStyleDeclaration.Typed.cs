using System;
using NecronomiDom.Css.Values;

namespace NecronomiDom.Css.Om
{
    public sealed partial class CSSStyleDeclaration
    {
        /// <summary>Typed OM integration: caller validates the canonical longhand and value; no CSS text is parsed or serialized.</summary>
        internal void SetTypedProperty(string name, CssValue value)
        {
            EnsureWritable();
            Read();
            _computedIndex = null;
            for (var i = 0; i < _declarations.Count; i++)
            {
                var previous = _declarations[i];
                if (previous.Name != name) continue;
                var append = false;
                for (var j = i + 1; j < _declarations.Count; j++)
                    if (MappingConflicts(name, _declarations[j].Name)) { append = true; break; }
                BeforeChange?.Invoke();
                var declaration = ReferenceEquals(previous.Value, value) && !previous.Important ? previous : new CssDeclaration(name, value, false);
                if (append) { _declarations.RemoveAt(i); _declarations.Add(declaration); }
                else _declarations[i] = declaration;
                Changed?.Invoke();
                return;
            }
            BeforeChange?.Invoke();
            _declarations.Add(new CssDeclaration(name, value, false));
            Changed?.Invoke();
        }

        /// <summary>Typed OM deletion of a canonical property name, including shorthand longhands, without serializing an old value.</summary>
        internal void RemoveTypedProperty(string name)
        {
            EnsureWritable();
            Read();
            var definition = _propertyGrammar ? CssPropertyRegistry.Get(name) : null;
            var changed = false;
            if (definition?.IsShorthand == true)
                foreach (var longhand in Longhands(definition)) changed |= Remove(longhand, !changed);
            else changed = Remove(name, true);
            if (changed) Changed?.Invoke();
        }

        /// <summary>Typed OM clearing without CSS text parsing or serialization; observers fire once only when nonempty.</summary>
        internal void ClearTypedProperties()
        {
            EnsureWritable();
            Read();
            if (_declarations.Count == 0) return;
            _computedIndex = null;
            BeforeChange?.Invoke();
            _declarations.Clear();
            Changed?.Invoke();
        }
    }
}
