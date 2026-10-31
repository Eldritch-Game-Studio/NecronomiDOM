namespace NecronomiDom.Dom
{
    internal static class SelectedContentProjection
    {
        internal static void Refresh(Node changed)
        {
            for (Node? node = changed; node != null; node = node.ParentNode)
                if (node is HTMLSelectElement select) { select.RefreshSelectedContent(changed); return; }
        }
        internal static Node? Next(Node root, Node node)
        {
            if (node.FirstChild != null) return node.FirstChild;
            while (!ReferenceEquals(node, root) && node.NextSibling == null) node = node.ParentNode!;
            return ReferenceEquals(node, root) ? null : node.NextSibling;
        }
        internal static bool Within(Node? node, Node? ancestor)
        {
            if (ancestor == null) return false;
            for (; node != null; node = node.ParentNode) if (ReferenceEquals(node, ancestor)) return true;
            return false;
        }
    }
    /// <summary>HTML § 4.10 select selected-option projection into its first button's selectedcontent.</summary>
    public sealed partial class HTMLSelectElement
    {
        private bool _projectingSelectedContent;
        private HTMLSelectedContentElement? _projectionTarget;
        private HTMLOptionElement? _projectedOption;
        internal void RefreshSelectedContent(Node? changed)
        {
            if (_projectingSelectedContent) return;
            HTMLSelectedContentElement? target = null;
            if (!Multiple && FirstElementChild is HTMLButtonElement button)
                for (var node = button.FirstChild; node != null; node = SelectedContentProjection.Next(button, node))
                    if (node is HTMLSelectedContentElement content) { target = content; break; }
            if (target == null) { _projectionTarget = null; _projectedOption = null; return; }
            HTMLOptionElement? selected = null;
            var fallback = DefaultSelected;
            for (var node = FirstChild; node != null; node = SelectedContentProjection.Next(this, node))
                if (node is HTMLOptionElement option && ReferenceEquals(option.SelectOwner, this) && (option.DirtySelected ?? ReferenceEquals(option, fallback)))
                { selected = option; break; }
            if (ReferenceEquals(target, _projectionTarget) && ReferenceEquals(selected, _projectedOption) && !SelectedContentProjection.Within(changed, selected)) return;
            _projectionTarget = target; _projectedOption = selected;
            _projectingSelectedContent = true;
            try { target.CopyOption(selected); }
            finally { _projectingSelectedContent = false; }
        }
    }
    /// <summary>HTML § 4.10 selectedcontent copies selected option children; cloned nodes are not aliases.</summary>
    public partial class HTMLSelectedContentElement
    {
        internal void CopyOption(HTMLOptionElement? option)
        {
            ReplaceChildren();
            if (option == null) return;
            for (var child = option.FirstChild; child != null; child = child.NextSibling) AppendChild(child.CloneNode(true));
        }
    }
}
