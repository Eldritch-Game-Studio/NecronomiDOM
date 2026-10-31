using System.Collections.Generic;

namespace NecronomiDom.Css.Values
{
    // CSS Shapes 1 §§ 3–4 and Shapes 2 § 5: defaults and basic-shape serialization.
    internal static class CssShapeSpecifiedValues
    {
        internal static CssValue? FunctionArguments(string name, CssValue? value) => name switch
        {
            "circle" or "ellipse" => Radial(value, name == "ellipse"),
            "polygon" => Polygon(value),
            "path" => Path(value),
            "shape" => Shape(value),
            "inset" or "xywh" or "rect" => Rounded(value, name == "inset"),
            _ => value,
        };

        private static CssValue? Radial(CssValue? value, bool ellipse)
        {
            if (value == null) return null;
            List<CssValue> items = Items(value);
            int at = items.FindIndex(item => Is(item, "at"));
            int radii = at < 0 ? items.Count : at;
            if ((!ellipse && radii == 1 && Is(items[0], "closest-side"))
                || ellipse && radii == 2 && Is(items[0], "closest-side") && Is(items[1], "closest-side"))
            {
                items.RemoveRange(0, radii);
                at -= radii;
            }
            if (at >= 0)
            {
                int position = at + 1;
                int count = items.Count - position;
                if (count == 2 && (Vertical(items[position]) || Horizontal(items[position + 1])))
                    (items[position], items[position + 1]) = (items[position + 1], items[position]);
                if (count == 4 && Vertical(items[position]))
                {
                    (items[position], items[position + 2]) = (items[position + 2], items[position]);
                    (items[position + 1], items[position + 3]) = (items[position + 3], items[position + 1]);
                }
            }
            return Pack(items);
        }

        private static CssValue? Polygon(CssValue? value)
        {
            if (value is not CssListValue { Separator: CssListSeparator.Comma } list) return value;
            // Canonical polygons are common both at parse time and after unit computation.
            // Preserve immutable groups instead of allocating two lists and a replacement function on every refresh.
            if (list.Items.Count > 1)
            {
                if (list.Items[0] is CssListValue { Separator: CssListSeparator.Space } first)
                {
                    bool unchanged = first.Items.Count > 1 && !Is(first.Items[0], "nonzero");
                    for (int i = 0; unchanged && i + 1 < first.Items.Count; i++)
                        if (Is(first.Items[i], "round") && Zero(first.Items[i + 1])) unchanged = false;
                    if (unchanged) return value;
                }
                else if (!Is(list.Items[0], "nonzero")) return value;
            }
            var groups = new List<CssValue>(list.Items);
            List<CssValue> prefix = Items(groups[0]);
            if (prefix.Count > 0 && Is(prefix[0], "nonzero")) prefix.RemoveAt(0);
            int round = prefix.FindIndex(item => Is(item, "round"));
            if (round >= 0 && round + 1 < prefix.Count && Zero(prefix[round + 1])) prefix.RemoveRange(round, 2);
            if (prefix.Count == 0) groups.RemoveAt(0); else groups[0] = Pack(prefix)!;
            return groups.Count == 1 ? groups[0] : new CssListValue(groups, CssListSeparator.Comma);
        }

        private static CssValue? Path(CssValue? value)
        {
            if (value is CssStringValue single) return new CssStringValue(CssPathData.Serialize(single.Value));
            if (value is not CssListValue { Separator: CssListSeparator.Comma } list) return value;
            var groups = new List<CssValue>(list.Items);
            for (int i = 0; i < groups.Count; i++)
                if (groups[i] is CssStringValue path) groups[i] = new CssStringValue(CssPathData.Serialize(path.Value));
            if (groups.Count > 0 && Is(groups[0], "nonzero")) groups.RemoveAt(0);
            return groups.Count == 1 ? groups[0] : new CssListValue(groups, CssListSeparator.Comma);
        }

        private static CssValue? Shape(CssValue? value)
        {
            if (value is not CssListValue { Separator: CssListSeparator.Comma } list) return value;
            var groups = new List<CssValue>(list.Items.Count);
            for (int i = 0; i < list.Items.Count; i++)
            {
                List<CssValue> items = Items(list.Items[i]);
                if (i == 0 && items.Count > 0 && Is(items[0], "nonzero")) items.RemoveAt(0);
                if (items.Count > 0 && Is(items[0], "arc"))
                {
                    items.RemoveAll(item => Is(item, "small"));
                    int rotate = items.FindIndex(item => Is(item, "rotate"));
                    if (rotate >= 0 && rotate + 1 < items.Count && Zero(items[rotate + 1])) items.RemoveRange(rotate, 2);
                }
                groups.Add(Pack(items)!);
            }
            return new CssListValue(groups, CssListSeparator.Comma);
        }

        private static CssValue? Rounded(CssValue? value, bool inset)
        {
            if (value == null || value is CssListValue { Separator: CssListSeparator.Comma }) return value;
            bool slash = value is CssListValue { Separator: CssListSeparator.Slash };
            IReadOnlyList<CssValue>? groups = slash ? ((CssListValue)value).Items : null;
            List<CssValue> items = Items(slash ? groups![0] : value);
            int round = items.FindIndex(item => Is(item, "round"));
            if (round < 0) return inset ? Quad(items) : value;
            List<CssValue> first = items.GetRange(round + 1, items.Count - round - 1);
            List<CssValue>? second = slash ? Items(groups![1]) : null;
            items.RemoveRange(round, items.Count - round);
            CssValue prefix = inset ? Quad(items) : Pack(items)!;
            if (AllZero(first) && (second == null || AllZero(second))) return prefix;
            CssValue x = Quad(first);
            CssValue? y = second == null ? null : Quad(second);
            List<CssValue> output = Items(prefix);
            output.Add(new CssKeywordValue("round"));
            if (x is CssListValue { Separator: CssListSeparator.Space } radii) output.AddRange(radii.Items); else output.Add(x);
            CssValue start = Pack(output)!;
            return y == null || x.Serialize() == y.Serialize() ? start : new CssListValue(new[] { start, y }, CssListSeparator.Slash);
        }

        private static CssValue Quad(List<CssValue> items) => CssLayoutSpecifiedValues.Quad(new CssListValue(items, CssListSeparator.Space));
        private static bool AllZero(List<CssValue> items)
        {
            foreach (CssValue item in items) if (!Zero(item)) return false;
            return true;
        }
        private static bool Zero(CssValue value) => value is CssNumberValue { Value: 0 } or CssDimensionValue { Value: 0 };
        private static bool Is(CssValue value, string keyword) => value is CssKeywordValue k && k.Is(keyword);
        private static bool Horizontal(CssValue value) => Is(value, "left") || Is(value, "right");
        private static bool Vertical(CssValue value) => Is(value, "top") || Is(value, "bottom");
        private static List<CssValue> Items(CssValue value) => value is CssListValue { Separator: CssListSeparator.Space } list
            ? new List<CssValue>(list.Items) : new List<CssValue> { value };
        private static CssValue? Pack(List<CssValue> items) => items.Count == 0 ? null : items.Count == 1 ? items[0] : new CssListValue(items, CssListSeparator.Space);
    }
}
