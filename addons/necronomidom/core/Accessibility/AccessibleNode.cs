using System;
using System.Collections.Generic;
using NecronomiDom.Dom;

namespace NecronomiDom.Accessibility;

/// <summary>ARIA tokens are retained verbatim; engine role reduction belongs to the publication adapter.</summary>
public enum AccessibleLive { Off, Polite, Assertive }
public enum AccessibleChecked { Undefined, False, True, Mixed }
public readonly record struct AccessibleStates(bool Disabled, bool ReadOnly, bool Required, bool Busy,
    bool Modal, bool Multiselectable, bool Focusable, bool Focused, AccessibleChecked Checked,
    AccessibleChecked Pressed, bool? Selected, bool? Expanded, string Invalid, string HasPopup,
    string Value, double? ValueNow, double? ValueMin, double? ValueMax, string Orientation,
    int? PositionInSet, int? SetSize);

/// <summary>Stable identity is bound to the actual DOM node, not its author-controlled id attribute.</summary>
public sealed class AccessibleNode
{
    internal AccessibleNode(long identity, Node source) { Identity = identity; Source = source; }
    public long Identity { get; }
    public Node Source { get; }
    public AccessibleNode? Parent { get; internal set; }
    internal readonly List<AccessibleNode> MutableChildren = new();
    public IReadOnlyList<AccessibleNode> Children => MutableChildren;
    public string Role { get; internal set; } = "generic";
    public string Name { get; internal set; } = "";
    public string Description { get; internal set; } = "";
    public string RoleDescription { get; internal set; } = "";
    public int? Level { get; internal set; }
    public AccessibleLive Live { get; internal set; }
    public bool LiveAtomic { get; internal set; }
    public string LiveRelevant { get; internal set; } = "additions text";
    public AccessibleStates States { get; internal set; }
    public bool Exposed { get; internal set; }
    public bool Ignored { get; internal set; }
    public int Revision { get; internal set; }
    internal AccessibleNode? PlannedParent;
    internal int PlannedChildCount;
    internal readonly Dictionary<string, List<AccessibleNode>> MutableRelations = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, List<AccessibleNode>> Relations => MutableRelations;
}
