namespace NecronomiDom.Dom
{
    /// <summary>HTML § 4.13.7 custom-state internals. Form-associated and accessibility APIs are not exposed by this scoped implementation.</summary>
    public sealed class ElementInternals
    {
        internal ElementInternals(HTMLElement target) => States = new CustomStateSet(target);
        /// <summary>HTML § 4.13.7.5 SameObject custom states associated with the target element.</summary>
        public CustomStateSet States { get; }
    }
    /// <summary>HTML § 4.13.7 autonomous custom-element internals attachment.</summary>
    public partial class HTMLElement
    {
        private ElementInternals? _internals;
        /// <summary>Attaches internals once to a custom/precustomized autonomous element; rejects unsupported or repeated attachment.</summary>
        public ElementInternals AttachInternals()
        {
            if (!CustomElementRegistry.IsCustomName(LocalName) || CustomFailed || CustomDefinition is not { } definition)
                throw DomException.NotSupported("Internals require an eligible autonomous custom element.");
            if (definition.DisableInternals) throw DomException.NotSupported("The custom definition disables internals.");
            if (_internals != null) throw DomException.NotSupported("Element internals are already attached.");
            return _internals = new ElementInternals(this);
        }
        internal bool HasCustomState(string state) => CustomDefinition != null && !CustomConstructing && !CustomFailed && _internals != null && _internals.States.Has(state);
    }
}
