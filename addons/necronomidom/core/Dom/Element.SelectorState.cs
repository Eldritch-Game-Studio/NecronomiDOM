using System;

namespace NecronomiDom.Dom
{
    /// <summary>Host-maintained Selectors 4 transient states. Form states are read from the form controls themselves.</summary>
    [Flags]
    public enum ElementPseudoState
    {
        None = 0, Hover = 1, Active = 2, Focus = 4, FocusVisible = 8, Modal = 16, PopoverOpen = 32,
        Indeterminate = 64, UserValid = 128, UserInvalid = 256,
    }
    public partial class Element
    {
        private ElementPseudoState _pseudoState;
        public ElementPseudoState PseudoState
        {
            get => _pseudoState;
            set
            {
                if (_pseudoState == value) return;
                _pseudoState = value;
                NodeDocument.ChangeSink?.AttributeChanged(this, ":state", null);
            }
        }
    }
}
