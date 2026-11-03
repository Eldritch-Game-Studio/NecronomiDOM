using System;
using System.Collections.Generic;
using System.Text;
using NecronomiDom.Css.Om;
using NecronomiDom.Dom;

namespace NecronomiDom.Html;

/// <summary>Developer-only detached validation snapshot. Nothing in the preview borrows mutable DOM/style state
/// from the live document, so real layout/resource validation cannot disturb its input, geometry or paint.</summary>
internal static class DocumentReloadPreview
{
    internal static Document Create(Document source, IReadOnlyList<CSSStyleSheet> previous, IReadOnlyList<CSSStyleSheet> replacements)
    {
        var preview = (Document)source.CloneNode(true);
        var nodes = new Dictionary<Node, Node>();
        Map(source, preview, nodes);
        var installed = new HashSet<CSSStyleSheet>();
        foreach (var pair in nodes)
        {
            if (pair.Key is Document document && pair.Value is Document copy)
            {
                CopySheets(document.StyleSheets, copy.StyleSheets, nodes, previous, replacements, installed);
                CopyAdopted(document.AdoptedStyleSheets, copy.AdoptedStyleSheets);
            }
            else if (pair.Key is ShadowRoot shadow && pair.Value is ShadowRoot copyShadow)
            {
                CopySheets(shadow.StyleSheets, copyShadow.StyleSheets, nodes, previous, replacements, installed);
                CopyAdopted(shadow.AdoptedStyleSheets, copyShadow.AdoptedStyleSheets);
            }
        }
        foreach (var replacement in replacements)
            if (!installed.Contains(replacement)) preview.StyleSheets.Add(CopySheet(replacement, nodes));
        return preview;
    }

    private static void Map(Node source, Node copy, Dictionary<Node, Node> nodes)
    {
        nodes.Add(source, copy);
        if (source is Element element && copy is Element copyElement)
        {
            copyElement.PseudoState = element.PseudoState;
            if (element.ScrollState is { } scroll)
                copyElement.ScrollState = new ElementScrollState
                {
                    Left = scroll.Left, Top = scroll.Top, Width = scroll.Width, Height = scroll.Height,
                    ClientWidth = scroll.ClientWidth, ClientHeight = scroll.ClientHeight,
                    LayoutLeft = scroll.LayoutLeft, LayoutTop = scroll.LayoutTop,
                    GutterRight = scroll.GutterRight, GutterBottom = scroll.GutterBottom,
                    X = scroll.X, Y = scroll.Y, UserX = scroll.UserX, UserY = scroll.UserY,
                    HoverX = scroll.HoverX, HoverY = scroll.HoverY
                };
            if (element.ShadowRootInternal is { } shadow)
            {
                var copyShadow = copyElement.ShadowRootInternal;
                if (copyShadow == null)
                {
                    copyShadow = copyElement.AttachShadow(new ShadowRootInit
                    {
                        Mode = shadow.Mode, DelegatesFocus = shadow.DelegatesFocus, SlotAssignment = shadow.SlotAssignment,
                        Clonable = shadow.Clonable, Serializable = shadow.Serializable
                    });
                    foreach (var child in shadow.ChildNodes) copyShadow.AppendChild(child.CloneInto(copyElement.NodeDocument, true));
                }
                Map(shadow, copyShadow, nodes);
            }
        }
        var childSource = source.FirstChild; var childCopy = copy.FirstChild;
        while (childSource != null && childCopy != null)
        {
            Map(childSource, childCopy, nodes);
            childSource = childSource.NextSibling; childCopy = childCopy.NextSibling;
        }
    }

    private static void CopySheets(IList<CSSStyleSheet> source, IList<CSSStyleSheet> target, Dictionary<Node, Node> nodes,
        IReadOnlyList<CSSStyleSheet> previous, IReadOnlyList<CSSStyleSheet> replacements, HashSet<CSSStyleSheet> installed)
    {
        foreach (var original in source)
        {
            var sheet = original;
            bool fileSheet = false;
            for (int i = 0; i < previous.Count; i++) if (ReferenceEquals(previous[i], original)) { fileSheet = true; break; }
            if (fileSheet)
            {
                CSSStyleSheet? replacement = null;
                foreach (var candidate in replacements)
                    if (ReferenceEquals(candidate.OwnerNode, original.OwnerNode)) { replacement = candidate; break; }
                if (replacement == null) continue;
                sheet = replacement; installed.Add(sheet);
            }
            if (sheet.OwnerNode is HTMLStyleElement owner && ReferenceEquals(owner.Sheet, sheet) &&
                nodes.TryGetValue(owner, out var node) && node is HTMLStyleElement inline)
            {
                inline.TextContent = Serialize(sheet);
                CopyMetadata(sheet, inline.Sheet!);
            }
            else target.Add(CopySheet(sheet, nodes));
        }
    }

    private static void CopyAdopted(IList<CSSStyleSheet> source, IList<CSSStyleSheet> target)
    {
        foreach (var sheet in source)
        {
            var copy = CSSStyleSheet.Parse(Serialize(sheet), sheet.SourcePath, sheet.BaseUrl);
            CopyMetadata(sheet, copy); target.Add(copy);
        }
    }
    private static CSSStyleSheet CopySheet(CSSStyleSheet sheet, Dictionary<Node, Node> nodes)
    {
        var copy = CSSStyleSheet.Parse(Serialize(sheet), sheet.SourcePath, sheet.BaseUrl);
        if (sheet.OwnerNode != null && nodes.TryGetValue(sheet.OwnerNode, out var owner)) copy.OwnerNode = owner;
        CopyMetadata(sheet, copy);
        return copy;
    }
    private static void CopyMetadata(CSSStyleSheet source, CSSStyleSheet copy)
    { copy.Media.MediaText = source.Media.MediaText; copy.Disabled = source.Disabled; copy.Origin = source.Origin; copy.Title = source.Title; }
    private static string Serialize(CSSStyleSheet sheet)
    {
        var css = new StringBuilder();
        foreach (var rule in sheet.CssRules) css.Append(rule.CssText).Append('\n');
        return css.ToString();
    }
}
