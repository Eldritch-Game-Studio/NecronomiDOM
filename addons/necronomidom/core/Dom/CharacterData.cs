using System;
using System.Text;

namespace NecronomiDom.Dom
{
    /// <summary>DOM § 4.10 CharacterData mutation and observer records.</summary>
    public abstract partial class CharacterData : Node
    {
        private string _data;
        private protected CharacterData(Document doc, string data) : base(doc) => _data = data;
        public string Data { get => _data; set => ReplaceData(0, Length, value); }
        public int Length => _data.Length;
        public string SubstringData(int offset, int count)
        { Check(offset, count); return _data.Substring(offset, Math.Min(count, Length - offset)); }
        public void AppendData(string data) => ReplaceData(Length, 0, data);
        public void InsertData(int offset, string data) => ReplaceData(offset, 0, data);
        public void DeleteData(int offset, int count) => ReplaceData(offset, count, string.Empty);
        public void ReplaceData(int offset, int count, string data)
        {
            Check(offset, count);
            var old = _data; count = Math.Min(count, Length - offset);
            if (offset == 0 && count == Length) _data = data;
            else if (count != 0 || data.Length != 0)
                _data = string.Concat(_data.AsSpan(0, offset), data.AsSpan(), _data.AsSpan(offset + count));
            MutationObserver.Queue(this, "characterData", oldValue: old);
            NodeDocument.ChangeSink?.CharacterDataChanged(this);
            SelectedContentProjection.Refresh(this);
        }
        private void Check(int offset, int count)
        { if (offset < 0 || offset > Length || count < 0) throw DomException.IndexSize("CharacterData range is out of bounds."); }
    }
    /// <summary>DOM § 4.11 Text split and contiguous whole text.</summary>
    public class Text : CharacterData
    {
        internal Text(Document doc, string data) : base(doc, data) { }
        public override ushort NodeType => TextNode;
        public override string NodeName => "#text";
        public HTMLSlotElement? AssignedSlot => AssignedSlotInternal?.Root is ShadowRoot { Mode: ShadowRootMode.Closed } ? null : AssignedSlotInternal;
        public Text SplitText(int offset)
        {
            if (offset < 0 || offset > Length) throw DomException.IndexSize("Offset exceeds text length.");
            var result = NodeDocument.CreateTextNode(Data.Substring(offset));
            ParentNode?.InsertBefore(result, NextSibling); DeleteData(offset, Length - offset); return result;
        }
        public string WholeText
        {
            get
            {
                Node start = this;
                while (start.PreviousSibling is Text) start = start.PreviousSibling;
                var b = new StringBuilder();
                for (Node? n = start; n is Text t; n = n.NextSibling) b.Append(t.Data);
                return b.ToString();
            }
        }
    }
    /// <summary>DOM § 4.12 CDATASection (XML documents only).</summary>
    public sealed class CDATASection : Text
    {
        internal CDATASection(Document doc, string data) : base(doc, data) { }
        public override ushort NodeType => CDATASectionNode;
        public override string NodeName => "#cdata-section";
    }
    /// <summary>DOM § 4.14 Comment.</summary>
    public sealed class Comment : CharacterData
    {
        internal Comment(Document doc, string data) : base(doc, data) { }
        public override ushort NodeType => CommentNode;
        public override string NodeName => "#comment";
    }
    /// <summary>DOM § 4.13 ProcessingInstruction.</summary>
    public sealed class ProcessingInstruction : CharacterData
    {
        internal ProcessingInstruction(Document doc, string target, string data) : base(doc, data) => Target = target;
        public string Target { get; }
        public override ushort NodeType => ProcessingInstructionNode;
        public override string NodeName => Target;
    }
    /// <summary>DOM § 4.9.2 Attr, with ownership and attribute mutation hooks.</summary>
    public sealed partial class Attr : Node
    {
        private string _value;
        internal Func<string>? ValueProvider { get; set; }
        internal Attr(Document doc, string? ns, string? prefix, string local, string value) : base(doc)
        { NamespaceURI = ns; Prefix = prefix; LocalName = local; _value = value; }
        public string? NamespaceURI { get; }
        public string? Prefix { get; internal set; }
        public string LocalName { get; }
        public string Name => Prefix == null ? LocalName : Prefix + ":" + LocalName;
        public Element? OwnerElement { get; internal set; }
        public bool Specified => true;
        public string Value
        {
            get => ValueProvider?.Invoke() ?? _value;
            set { var old = Value; ValueProvider = null; _value = value; OwnerElement?.NotifyAttribute(this, old, value); }
        }
        public override ushort NodeType => AttributeNode;
        public override string NodeName => Name;
    }
}
