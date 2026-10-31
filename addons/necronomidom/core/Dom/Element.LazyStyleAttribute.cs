using System;

namespace NecronomiDom.Dom
{
    public partial class Element
    {
        internal bool StyleOldValueRequired()
        {
            if (NodeDocument.ChangeSink?.RequiresAttributeOldValue == true || CustomDefinition?.Observes("style") == true) return true;
            for (Node? node = this; node != null; node = node.ParentNode)
            {
                if (node.RegisteredObservers == null) continue;
                foreach (var registration in node.RegisteredObservers)
                {
                    var options = registration.Options;
                    if ((!ReferenceEquals(node, this) && !options.Subtree) || !options.ObserveAttributes || options.AttributeOldValue != true) continue;
                    if (options.AttributeFilter == null) return true;
                    foreach (string name in options.AttributeFilter) if (name == "style") return true;
                }
            }
            return false;
        }
        internal void ReflectStyleAttribute(Func<string> provider, string? oldValue)
        {
            using var reactions = NodeDocument.Reactions();
            Attr? attribute = GetAttributeNode("style");
            if (attribute == null)
            {
                attribute = new Attr(NodeDocument, null, null, "style", "") { OwnerElement = this };
                AttributeList.Add(attribute);
            }
            attribute.ValueProvider = provider;
            // The declaration is already updated: avoid reparsing the CSSOM result through intrinsic attribute hooks.
            MutationObserver.Queue(this, "attributes", attributeName: "style", oldValue: oldValue);
            NodeDocument.ChangeSink?.AttributeChanged(this, "style", oldValue);
            if (CustomDefinition?.Observes("style") == true) CustomDefinition.Attribute(this, "style", oldValue, attribute.Value, null);
        }
    }
}
