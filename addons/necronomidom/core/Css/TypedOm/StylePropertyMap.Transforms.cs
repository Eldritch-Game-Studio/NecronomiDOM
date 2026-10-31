using NecronomiDom.Dom;

namespace NecronomiDom.Css.TypedOm
{
    public sealed partial class StylePropertyMap
    {
        public void Set(string property, CSSTransformValue value)
        {
            string name = PropertyName(property);
            if (name != "transform" || value.Components.Count == 0) throw new DomTypeError("A transform value requires the transform property and a nonempty list.");
            for (int i = 0; i < value.Components.Count; i++) value.Components[i].Validate();
            var declaration = Declaration;
            var old = Find(declaration, name) as TypedTransformSlot;
            declaration.BeforeChange?.Invoke();
            TypedTransformSlot slot = old != null && old.SameShape(value) ? old : new TypedTransformSlot(value);
            slot.Update(value);
            declaration.SetTypedProperty(name, slot);
        }
    }
}
