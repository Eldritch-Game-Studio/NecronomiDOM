namespace NecronomiDom.Dom
{
    /// <summary>DOM § 4.2.6 ParentNode and § 4.5 descendant collections on Document.</summary>
    public partial class Document
    {
        public new HTMLCollection Children => base.Children;
        public new Element? FirstElementChild => base.FirstElementChild;
        public new Element? LastElementChild => base.LastElementChild;
        public new int ChildElementCount => base.ChildElementCount;
        public new void Append(params NodeOrString[] nodes) => base.Append(nodes);
        public new void Prepend(params NodeOrString[] nodes) => base.Prepend(nodes);
        public new void ReplaceChildren(params NodeOrString[] nodes) => base.ReplaceChildren(nodes);
        public new HTMLCollection GetElementsByTagName(string name) => base.GetElementsByTagName(name);
        public new HTMLCollection GetElementsByTagNameNS(string? ns, string local) => base.GetElementsByTagNameNS(ns, local);
        public new HTMLCollection GetElementsByClassName(string names) => base.GetElementsByClassName(names);
    }
    /// <summary>DOM § 4.2.6 ParentNode on DocumentFragment, inherited by ShadowRoot.</summary>
    public partial class DocumentFragment
    {
        public new HTMLCollection Children => base.Children;
        public new Element? FirstElementChild => base.FirstElementChild;
        public new Element? LastElementChild => base.LastElementChild;
        public new int ChildElementCount => base.ChildElementCount;
        public new void Append(params NodeOrString[] nodes) => base.Append(nodes);
        public new void Prepend(params NodeOrString[] nodes) => base.Prepend(nodes);
        public new void ReplaceChildren(params NodeOrString[] nodes) => base.ReplaceChildren(nodes);
    }
    /// <summary>DOM § 4.2.6 ParentNode, § 4.2.8 ChildNode, and § 4.9 descendants on Element.</summary>
    public partial class Element
    {
        public new HTMLCollection Children => base.Children;
        public new Element? FirstElementChild => base.FirstElementChild;
        public new Element? LastElementChild => base.LastElementChild;
        public new int ChildElementCount => base.ChildElementCount;
        public new void Append(params NodeOrString[] nodes) => base.Append(nodes);
        public new void Prepend(params NodeOrString[] nodes) => base.Prepend(nodes);
        public new void ReplaceChildren(params NodeOrString[] nodes) => base.ReplaceChildren(nodes);
        public new Element? PreviousElementSibling => base.PreviousElementSibling;
        public new Element? NextElementSibling => base.NextElementSibling;
        public new void Before(params NodeOrString[] nodes) => base.Before(nodes);
        public new void After(params NodeOrString[] nodes) => base.After(nodes);
        public new void ReplaceWith(params NodeOrString[] nodes) => base.ReplaceWith(nodes);
        public new void Remove() => base.Remove();
        public new HTMLCollection GetElementsByTagName(string name) => base.GetElementsByTagName(name);
        public new HTMLCollection GetElementsByTagNameNS(string? ns, string local) => base.GetElementsByTagNameNS(ns, local);
        public new HTMLCollection GetElementsByClassName(string names) => base.GetElementsByClassName(names);
    }
    /// <summary>DOM § 4.2.8 ChildNode and § 4.2.5 NonDocumentTypeChildNode on CharacterData.</summary>
    public abstract partial class CharacterData
    {
        public new Element? PreviousElementSibling => base.PreviousElementSibling;
        public new Element? NextElementSibling => base.NextElementSibling;
        public new void Before(params NodeOrString[] nodes) => base.Before(nodes);
        public new void After(params NodeOrString[] nodes) => base.After(nodes);
        public new void ReplaceWith(params NodeOrString[] nodes) => base.ReplaceWith(nodes);
        public new void Remove() => base.Remove();
    }
    /// <summary>DOM § 4.2.8 ChildNode on DocumentType (not NonDocumentTypeChildNode).</summary>
    public sealed partial class DocumentType
    {
        public new void Before(params NodeOrString[] nodes) => base.Before(nodes);
        public new void After(params NodeOrString[] nodes) => base.After(nodes);
        public new void ReplaceWith(params NodeOrString[] nodes) => base.ReplaceWith(nodes);
        public new void Remove() => base.Remove();
    }
}
