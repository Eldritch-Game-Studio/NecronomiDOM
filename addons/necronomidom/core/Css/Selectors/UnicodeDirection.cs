using System;
using System.Text;
using NecronomiDom.Dom;

namespace NecronomiDom.Css.Selectors
{
    /// <summary>HTML § 3.2.6.4 dir=auto first-strong directionality using pinned Unicode UAX #9 Bidi_Class data.</summary>
    internal static partial class UnicodeDirection
    {
        internal static int Strong(int codePoint)
        {
            ReadOnlySpan<int> ranges = Ranges;
            int low = 0, high = ranges.Length / 3 - 1;
            while (low <= high)
            {
                int middle = low + (high - low) / 2;
                int index = middle * 3;
                if (codePoint < ranges[index]) high = middle - 1;
                else if (codePoint > ranges[index + 1]) low = middle + 1;
                else return ranges[index + 2];
            }
            return 0;
        }
        internal static string Direction(Element element)
        {
            for (Element? current = element; current != null; current = InheritedParent(current))
            {
                string? dir = current.GetAttribute("dir") is { } value ? Names.AsciiLower(value) : null;
                if (dir is "ltr" or "rtl") return dir;
                if (dir == "auto" || dir == null && current.LocalName == "bdi")
                {
                    int strong = FirstStrong(current);
                    return strong == 2 ? "rtl" : "ltr";
                }
                if (current is HTMLInputElement { Type: "tel" }) return "ltr";
            }
            return "ltr";
        }
        private static Element? InheritedParent(Element element) => element.AssignedSlotInternal
            ?? element.ParentElement ?? (element.ParentNode as ShadowRoot)?.Host;
        private static int FirstStrong(Element element)
        {
            if (element is HTMLInputElement input) return FirstStrong(input.Value);
            if (element is HTMLTextAreaElement area) return FirstStrong(area.Value);
            for (Node? node = element.FirstChild; node != null;)
            {
                if (node is Element child && (child.LocalName is "bdi" or "script" or "style" or "textarea"
                    || child.GetAttribute("dir") is { } direction && (Names.AsciiEqual(direction, "ltr") || Names.AsciiEqual(direction, "rtl") || Names.AsciiEqual(direction, "auto"))))
                { node = node.NextSkippingChildren(element); continue; }
                if (node is Text text)
                {
                    int strong = FirstStrong(text.Data);
                    if (strong != 0) return strong;
                }
                node = node.NextInTreeOrder(element);
            }
            return 0;
        }
        private static int FirstStrong(string text)
        {
            foreach (var rune in text.EnumerateRunes())
            {
                int strong = Strong(rune.Value);
                if (strong != 0) return strong;
            }
            return 0;
        }
    }
}
