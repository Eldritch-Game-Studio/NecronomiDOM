using NecronomiDom.Dom;

namespace NecronomiDom.Css.TypedOm;

public sealed partial class StylePropertyMap
{
    /// <summary>Assigns a managed nonzero polygon, copying caller coordinates into declaration-owned storage.</summary>
    public void Set(string property, CSSPolygonValue value)
    {
        string name = PropertyName(property);
        if (name != "clip-path") throw new DomTypeError("A polygon value requires the clip-path property.");
        // The caller can reuse or edit its list after constructing the wrapper. Validate every
        // coordinate before either notifying observers or touching the previous declaration.
        value.Validate();
        var declaration = Declaration;
        var old = Find(declaration, name) as TypedPolygonSlot;
        declaration.BeforeChange?.Invoke();
        TypedPolygonSlot slot;
        if (old != null && old.SameShape(value)) { slot = old; slot.Update(value); }
        else slot = new TypedPolygonSlot(value);
        declaration.SetTypedProperty(name, slot);
    }
}
