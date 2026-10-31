using System;
using System.Collections.Generic;
using NecronomiDom.Css.Values.Math;

namespace NecronomiDom.Css.Values
{
    // CSS Transforms 1 §§ 9–10 and Transforms 2 § 5: specified-value serialization.
    internal static class CssTransformSpecifiedValues
    {
        internal static CssValue Normalize(string property, CssValue value) => property switch
        {
            "scale" => Scale(value),
            "translate" => Translate(value),
            "rotate" => Rotate(value),
            "transform-origin" or "perspective-origin" => Origin(value),
            _ => value,
        };

        internal static CssValue FunctionArguments(string name, CssValue value)
        {
            name = NecronomiDom.Dom.Names.AsciiLower(name);
            if (name is not ("scale" or "scalex" or "scaley" or "scalez" or "scale3d")) return value;
            if (value is not CssListValue list) return NumberPercentage(value);
            var items = new CssValue[list.Items.Count];
            for (int i = 0; i < items.Length; i++) items[i] = NumberPercentage(list.Items[i]);
            return new CssListValue(items, list.Separator);
        }

        private static CssValue NumberPercentage(CssValue value)
        {
            if (value is CssPercentageValue percentage) return new CssNumberValue(percentage.Value / 100, false);
            if (value is CssMathValue math)
            {
                CssMathValue normalized = math.Simplify(CssMathEvaluationContext.NumberPercentage);
                return new CssMathValue(normalized.Root, normalized.Root.Type, math.WasCalc)
                    .WithGrammarConstraints(math.IsIntegerTarget, math.GrammarMinimum, math.GrammarMaximum);
            }
            return value;
        }

        private static CssValue Scale(CssValue value)
        {
            if (value is CssKeywordValue) return value;
            List<CssValue> items = Items(value);
            for (int i = 0; i < items.Count; i++) items[i] = NumberPercentage(items[i]);
            if (items.Count == 3 && items[2] is CssNumberValue { Value: 1 }) items.RemoveAt(2);
            if (items.Count == 2 && items[0].Serialize() == items[1].Serialize()) items.RemoveAt(1);
            return Pack(items);
        }

        private static CssValue Translate(CssValue value)
        {
            if (value is CssKeywordValue) return value;
            List<CssValue> items = Items(value);
            while (items.Count > 1 && ZeroLength(items[^1])) items.RemoveAt(items.Count - 1);
            return Pack(items);
        }

        internal static CssValue Rotate(CssValue value)
        {
            if (value is not CssListValue list) return value;
            var axes = new List<CssValue>();
            CssValue? angle = null;
            foreach (CssValue item in list.Items)
            {
                if (item is CssDimensionValue d && CssTypeParser.UnitType(d.Unit) == "angle"
                    || item is CssMathValue math && math.Fits("angle")) angle = item;
                else if (item is CssListValue nested) axes.AddRange(nested.Items);
                else axes.Add(item);
            }
            if (angle == null) return value;
            string? axis = null;
            bool reverse = false;
            if (axes.Count == 1 && axes[0] is CssKeywordValue keyword) axis = keyword.Value;
            else if (axes.Count == 3 && axes[0] is CssNumberValue x && axes[1] is CssNumberValue y && axes[2] is CssNumberValue z)
            {
                int nonzero = (x.Value != 0 ? 1 : 0) + (y.Value != 0 ? 1 : 0) + (z.Value != 0 ? 1 : 0);
                if (nonzero == 1)
                {
                    axis = x.Value != 0 ? "x" : y.Value != 0 ? "y" : "z";
                    reverse = (x.Value != 0 ? x.Value : y.Value != 0 ? y.Value : z.Value) < 0;
                }
            }
            if (reverse) angle = Negate(angle);
            if (axis == "z") return angle;
            if (axis != null) return new CssListValue(new CssValue[] { new CssKeywordValue(axis), angle }, CssListSeparator.Space);
            axes.Add(angle);
            return Pack(axes);
        }

        private static CssValue Negate(CssValue value)
        {
            if (value is CssDimensionValue dimension) return new CssDimensionValue(-dimension.Value, dimension.Unit);
            if (value is CssMathValue math)
            {
                var factor = new CssMathLiteral(-1, "", CssNumericType.For(CssNumericDimension.Number));
                var root = new CssMathOperation("*", new CssMathExpression[] { factor, math.Root }, math.Type);
                return new CssMathValue(root, math.Type, true)
                    .WithGrammarConstraints(math.IsIntegerTarget, math.GrammarMinimum, math.GrammarMaximum)
                    .Simplify(CssMathEvaluationContext.Specified);
            }
            return value;
        }

        /// <summary>
        /// CSSOM position serialization supplies the omitted centered axis and orders physical
        /// edge-offset pairs horizontally first. The grammar has already rejected invalid axes;
        /// this must not resolve percentages, which Typed OM retains until a used-value consumer.
        /// Shared by transform/perspective origins, object-position and each mask-position layer.
        /// </summary>
        internal static CssValue Origin(CssValue value)
        {
            List<CssValue> items = Items(value);
            if (items.Count == 1)
            {
                if (Vertical(items[0])) items.Insert(0, new CssKeywordValue("center"));
                else items.Add(new CssKeywordValue("center"));
            }
            else if (items.Count == 4 && Vertical(items[0]))
            {
                (items[0], items[2]) = (items[2], items[0]);
                (items[1], items[3]) = (items[3], items[1]);
            }
            else if (items.Count >= 2 && (Vertical(items[0]) || Horizontal(items[1])))
                (items[0], items[1]) = (items[1], items[0]);
            return Pack(items);
        }

        private static bool Horizontal(CssValue value) => value is CssKeywordValue keyword && (keyword.Is("left") || keyword.Is("right"));
        private static bool Vertical(CssValue value) => value is CssKeywordValue keyword && (keyword.Is("top") || keyword.Is("bottom"));
        private static bool ZeroLength(CssValue value) => value is CssNumberValue { Value: 0 } || value is CssDimensionValue { Value: 0 };
        private static List<CssValue> Items(CssValue value) => value is CssListValue list && list.Separator == CssListSeparator.Space
            ? new List<CssValue>(list.Items) : new List<CssValue> { value };
        private static CssValue Pack(List<CssValue> items) => items.Count == 1 ? items[0] : new CssListValue(items, CssListSeparator.Space);
    }
}
