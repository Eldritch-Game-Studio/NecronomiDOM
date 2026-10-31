using System;
using System.Collections.Generic;
using NecronomiDom.Css.Values.Math;

namespace NecronomiDom.Css.Values
{
    // CSSOM § 6.7.2, CSS Grid 2 § 7 and CSS Box Alignment 3 § 4: canonical specified forms.
    internal static class CssLayoutSpecifiedValues
    {
        internal static CssValue Normalize(string property, CssValue value)
        {
            if (property == "display") return Display(value);
            if (property == "contain") return Containment(value);
            if (property == "mask-position") return MaskPosition(value);
            if (property is "mask-repeat" or "background-repeat") return Repeat(value);
            if (property == "overflow-clip-margin") return ClipMargin(value);
            if (property == "grid-template-areas") return Areas(value);
            if (property is "grid-template-rows" or "grid-template-columns" && !Contains(value, "subgrid")) return Tracks(value);
            if (property is "counter-increment" or "counter-reset" or "counter-set") return Counters(value, property);
            if (property == "aspect-ratio") return Ratio(value);
            if (value is not CssListValue { Separator: CssListSeparator.Space } list) return value;
            if (property == "object-fit" && list.Items.Count == 2)
            {
                if (Is(list.Items[0], "contain") && Is(list.Items[1], "scale-down")) return list.Items[1];
                if (Is(list.Items[0], "scale-down") && Is(list.Items[1], "contain")) return list.Items[0];
                if (Is(list.Items[0], "scale-down") && Is(list.Items[1], "cover")) return Space(list.Items[1], list.Items[0]);
            }
            if ((property.StartsWith("align-", StringComparison.Ordinal) || property.StartsWith("justify-", StringComparison.Ordinal))
                && list.Items.Count == 2 && Is(list.Items[0], "first") && Is(list.Items[1], "baseline")) return list.Items[1];
            if (property == "grid-auto-flow" && Contains(list, "dense") && Contains(list, "row")) return new CssKeywordValue("dense");
            if (property == "scroll-snap-align" && list.Items.Count == 2 && Equal(list.Items[0], list.Items[1])) return list.Items[0];
            if (property == "text-decoration-inset" && list.Items.Count == 2 && Equal(list.Items[0], list.Items[1])) return list.Items[0];
            if (property == "scroll-snap-type" && list.Items.Count == 2 && Is(list.Items[1], "proximity")) return list.Items[0];
            if (property == "text-box-edge" && list.Items.Count == 2)
            {
                if (Equal(list.Items[0], list.Items[1]) || (Is(list.Items[0], "cap") || Is(list.Items[0], "ex")) && Is(list.Items[1], "text")) return list.Items[0];
                if (Is(list.Items[0], "text") && Is(list.Items[1], "alphabetic")) return list.Items[1];
            }
            if (property is "grid-row-start" or "grid-row-end" or "grid-column-start" or "grid-column-end") return Line(list);
            if (property is "border-image-slice" or "mask-border-slice" or "border-image-width" or "border-image-outset" or "mask-border-width" or "mask-border-outset")
                return Quad(list);
            return value;
        }

        private static CssValue MaskPosition(CssValue value)
        {
            if (value is CssListValue { Separator: CssListSeparator.Comma }) return value;
            if (!ShorthandExpansion.Position(NecronomiDom.Css.Syntax.CssParser.ParseComponentValueList(value.Serialize()), out CssValue x, out CssValue y))
                return value;
            return Space(x, y);
        }

        private static CssValue Repeat(CssValue value)
        {
            if (value is not CssListValue { Separator: CssListSeparator.Space, Items.Count: 2 } list) return value;
            if (Equal(list.Items[0], list.Items[1])) return list.Items[0];
            if (Is(list.Items[0], "repeat") && Is(list.Items[1], "no-repeat")) return Keyword("repeat-x");
            if (Is(list.Items[0], "no-repeat") && Is(list.Items[1], "repeat")) return Keyword("repeat-y");
            return value;
        }

        private static CssValue ClipMargin(CssValue value)
        {
            IReadOnlyList<CssValue> items = value is CssListValue list ? list.Items : new[] { value };
            CssValue? box = null, margin = null;
            foreach (CssValue item in items)
                if (item is CssKeywordValue) box = item;
                else margin = item;
            if (box == null || Is(box, "padding-box")) return margin ?? new CssDimensionValue(0, "px");
            if (margin == null || margin is CssDimensionValue { Value: 0 }) return box;
            return Space(box, margin);
        }

        private static CssValue Areas(CssValue value)
        {
            if (value is CssStringValue row)
            {
                List<string>? cells = CssValueConstraints.GridCells(row.Value);
                return cells == null ? value : new CssStringValue(string.Join(" ", cells));
            }
            if (value is not CssListValue list) return value;
            var rows = new List<CssValue>(list.Items.Count);
            foreach (CssValue item in list.Items) rows.Add(Areas(item));
            return new CssListValue(rows, list.Separator);
        }

        private static CssValue Line(CssListValue value)
        {
            CssValue? span = null, number = null, name = null;
            foreach (CssValue item in value.Items)
            {
                if (Is(item, "span")) span = item;
                else if (item is CssNumberValue || item is CssMathValue) number = item;
                else name = item;
            }
            var parts = new List<CssValue>(3);
            if (span != null) parts.Add(span);
            if (number != null && !(span != null && name != null && number is CssNumberValue { Value: 1 })) parts.Add(number);
            if (name != null) parts.Add(name);
            return Pack(parts);
        }

        internal static CssValue Quad(CssListValue list)
        {
            var values = new List<CssValue>(4);
            CssValue? fill = null;
            foreach (CssValue item in list.Items)
                if (Is(item, "fill")) fill = item;
                else values.Add(item);
            int count = values.Count;
            if (count == 4 && Equal(values[1], values[3])) count--;
            if (count == 3 && Equal(values[0], values[2])) count--;
            if (count == 2 && Equal(values[0], values[1])) count--;
            if (count < values.Count) values.RemoveRange(count, values.Count - count);
            if (fill != null) values.Add(fill);
            return Pack(values);
        }

        internal static CssValue Tracks(CssValue value)
        {
            if (value is CssFunctionValue { Arguments: not null } function)
                return new CssFunctionValue(function.Name, Tracks(function.Arguments));
            if (value is not CssListValue list) return value;
            var items = new List<CssValue>(list.Items.Count);
            foreach (CssValue source in list.Items)
            {
                CssValue item = Tracks(source);
                if (item is CssBlockValue block)
                {
                    if (block.Content == null || block.Content is CssListValue { Items.Count: 0 }) continue;
                    if (items.Count > 0 && items[^1] is CssBlockValue previous)
                    {
                        var names = new List<CssValue>();
                        Add(names, previous.Content); Add(names, block.Content);
                        items[^1] = new CssBlockValue(Pack(names));
                        continue;
                    }
                }
                items.Add(item);
            }
            return list.Separator == CssListSeparator.Space ? Pack(items) : new CssListValue(items, list.Separator);
        }

        private static void Add(List<CssValue> values, CssValue? value)
        {
            if (value is CssListValue { Separator: CssListSeparator.Space } list) values.AddRange(list.Items);
            else if (value != null) values.Add(value);
        }
        internal static CssValue Display(CssValue value, bool blockify = false)
        {
            IReadOnlyList<CssValue> items = value is CssListValue list ? list.Items : new[] { value };
            string? outer = null, inner = null;
            bool listItem = false;
            foreach (CssValue item in items)
            {
                if (item is not CssKeywordValue keyword) return value;
                switch (keyword.Value)
                {
                    case "none": case "contents": return value;
                    case "block": case "inline": case "run-in": outer = keyword.Value; break;
                    case "flow": case "flow-root": case "flex": case "grid": case "table": case "ruby": inner = keyword.Value; break;
                    case "list-item": listItem = true; break;
                    case "inline-block": outer = "inline"; inner = "flow-root"; break;
                    case "inline-flex": outer = "inline"; inner = "flex"; break;
                    case "inline-grid": outer = "inline"; inner = "grid"; break;
                    case "inline-table": outer = "inline"; inner = "table"; break;
                    case "table-row-group": case "table-header-group": case "table-footer-group": case "table-row":
                    case "table-column-group": case "table-column": case "table-cell": case "table-caption":
                    case "ruby-base": case "ruby-text": case "ruby-base-container": case "ruby-text-container":
                        return blockify ? Keyword("block") : value;
                    default: return value;
                }
            }
            outer ??= inner == "ruby" ? "inline" : "block";
            inner ??= "flow";
            if (blockify)
            {
                // CSS Display §2.7: legacy inline-block/run-in flow-root lose flow-root;
                // explicit block flow-root and flow-root list items retain it.
                if (!listItem && inner == "flow-root" && outer is "inline" or "run-in") inner = "flow";
                outer = "block";
            }
            if (listItem)
            {
                if (outer == "block") return inner == "flow" ? Keyword("list-item") : Space(Keyword(inner), Keyword("list-item"));
                return inner == "flow" ? Space(Keyword(outer), Keyword("list-item")) : Space(Keyword(outer), Keyword(inner), Keyword("list-item"));
            }
            if (inner == "flow") return Keyword(outer);
            if (outer == "inline") return inner switch
            {
                "flow-root" => Keyword("inline-block"), "flex" => Keyword("inline-flex"),
                "grid" => Keyword("inline-grid"), "table" => Keyword("inline-table"), "ruby" => Keyword("ruby"),
                _ => Space(Keyword(outer), Keyword(inner))
            };
            return outer == "block" && inner != "ruby" ? Keyword(inner) : Space(Keyword(outer), Keyword(inner));
        }

        internal static CssValue Containment(CssValue value, bool computed = false)
        {
            if (value is CssKeywordValue) return value;
            if (computed && Contains(value, "layout") && Contains(value, "style") && Contains(value, "paint")
                && !Contains(value, "inline-size")) return Keyword(Contains(value, "size") ? "strict" : "content");
            var ordered = new List<CssValue>(4);
            foreach (string name in new[] { "size", "inline-size", "layout", "style", "paint" })
                if (Contains(value, name)) ordered.Add(Keyword(name));
            return Pack(ordered);
        }
        private static CssValue Ratio(CssValue value)
        {
            if (value is CssNumberValue or CssMathValue) return new CssListValue(new[] { value, new CssNumberValue(1, true) }, CssListSeparator.Slash);
            if (value is CssListValue { Separator: CssListSeparator.Space } list)
            {
                var values = new List<CssValue>(list.Items.Count);
                foreach (CssValue item in list.Items) values.Add(Ratio(item));
                return Pack(values);
            }
            return value;
        }
        internal static CssValue Counters(CssValue value, string property)
        {
            if (Is(value, "none")) return value;
            IReadOnlyList<CssValue> items = value is CssListValue list ? list.Items : new[] { value };
            var values = new List<CssValue>();
            for (int i = 0; i < items.Count; i++)
            {
                CssValue name = items[i];
                if (name is CssListValue nested)
                {
                    CssValue normalized = Counters(nested, property);
                    Add(values, normalized);
                    continue;
                }
                values.Add(name);
                if (i + 1 < items.Count && items[i + 1] is CssNumberValue or CssMathValue) values.Add(items[++i]);
                else if (name is not CssFunctionValue { Name: "reversed" }) values.Add(new CssNumberValue(property == "counter-increment" ? 1 : 0, true));
            }
            return Pack(values);
        }
        private static CssKeywordValue Keyword(string value) => new(value);
        private static CssValue Space(params CssValue[] values) => new CssListValue(values, CssListSeparator.Space);

        private static CssValue Pack(List<CssValue> values) => values.Count == 1 ? values[0] : new CssListValue(values, CssListSeparator.Space);
        private static bool Equal(CssValue a, CssValue b) => a.Serialize() == b.Serialize();
        private static bool Is(CssValue value, string keyword) => value is CssKeywordValue word && word.Is(keyword);
        private static bool Contains(CssValue value, string keyword)
        {
            if (Is(value, keyword)) return true;
            if (value is CssListValue list) foreach (CssValue item in list.Items) if (Contains(item, keyword)) return true;
            return false;
        }
    }
}
