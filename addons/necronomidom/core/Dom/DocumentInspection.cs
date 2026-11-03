using System;
using System.Collections.Generic;
using NecronomiDom.Paint;

namespace NecronomiDom.Dom;

/// <summary>On-demand developer queries. Picking shares the renderer's transformed/clipped geometry,
/// but deliberately includes pointer-events:none so non-interactive rendered elements are inspectable.</summary>
public static class DocumentInspection
{
    public static Element? Pick(DisplayList display, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(display);
        for (int i = display.Boxes.Count - 1; i >= 0; i--)
        {
            LayoutBox box = display.Boxes[i];
            if (box.Element.IsConnected && box.ContainsPoint(x, y)) return box.Element;
        }
        return null;
    }

    /// <summary>Selected element first, followed by DOM parents and shadow hosts.</summary>
    public static IReadOnlyList<Element> Ancestry(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);
        var result = new List<Element>();
        for (Element? current = element; current != null;
            current = current.ParentElement ?? (current.ParentNode as ShadowRoot)?.Host)
            result.Add(current);
        return result.AsReadOnly();
    }

    public static string Label(Element? element)
    {
        if (element == null) return "<none>";
        string id = element.GetAttribute("id") ?? "";
        string classes = element.GetAttribute("class") ?? "";
        return element.LocalName + (id.Length == 0 ? "" : "#" + id)
            + (classes.Length == 0 ? "" : "." + string.Join(".", classes.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)));
    }
}
