namespace NecronomiDom.Dom
{
    /// <summary>Import provenance: original element start-token position, 1-based line/column and 0-based offset.</summary>
    public readonly record struct DomSourceLocation(int Line, int Column, int Offset);
    public partial class Element
    {
        public DomSourceLocation? SourceLocation { get; internal set; }
    }
}
