using System;
using NecronomiDom.Css.Values;
using NecronomiDom.Dom;

namespace NecronomiDom.Css.TypedOm
{
    /// <summary>Typed OM reification over parser-owned values, without a second tokenizer or string round trip.</summary>
    internal static class TypedTransformDecoder
    {
        internal static bool TryDecode(CssValue value, out CSSTransformValue result)
        {
            result = default;
            if (value is TypedTransformSlot slot) { result = slot.Snapshot(); return true; }
            if (value is not CssFunctionValue && value is not CssListValue { Separator: CssListSeparator.Space }) return false;
            int count = value is CssListValue list ? list.Items.Count : 1;
            if (count == 0) return false;
            var components = new CSSTransformComponent[count];
            for (int i = 0; i < count; i++)
            {
                var function = (value is CssListValue functions ? functions.Items[i] : value) as CssFunctionValue;
                if (function == null || !TryComponent(function, out components[i])) return false;
            }
            result = new CSSTransformValue(components);
            return true;
        }
        private static CSSNumericValue Numeric(CssFunctionValue function, int index)
        {
            CssValue argument = function.Arguments is CssListValue list ? list.Items[index] : function.Arguments!;
            if (!CSSNumericValue.TryFrom(argument, out var number)) throw new DomTypeError("Transform argument is not a numeric value.");
            return number;
        }
        private static int Count(CssFunctionValue function) => function.Arguments is CssListValue list ? list.Items.Count : function.Arguments == null ? 0 : 1;
        private static CSSNumericValue ZeroLength => new CSSUnitValue(0, "px");
        private static CSSNumericValue ZeroAngle => new CSSUnitValue(0, "deg");
        private static CSSNumericValue ZeroNumber => new CSSUnitValue(0, "number");
        private static CSSNumericValue One => new CSSUnitValue(1, "number");
        private static bool TryComponent(CssFunctionValue function, out CSSTransformComponent component)
        {
            component = default;
            switch (function.Name)
            {
                case "translate": component = CSSTransformComponent.Translate(Numeric(function, 0), Count(function) == 1 ? ZeroLength : Numeric(function, 1)); break;
                case "translatex": case "translateX": component = CSSTransformComponent.Translate(Numeric(function, 0), ZeroLength); break;
                case "translatey": case "translateY": component = CSSTransformComponent.Translate(ZeroLength, Numeric(function, 0)); break;
                case "translatez": case "translateZ": component = CSSTransformComponent.Translate(ZeroLength, ZeroLength, Numeric(function, 0)); break;
                case "translate3d": component = CSSTransformComponent.Translate(Numeric(function, 0), Numeric(function, 1), Numeric(function, 2)); break;
                case "scale": component = CSSTransformComponent.Scale(Numeric(function, 0), Count(function) == 1 ? Numeric(function, 0) : Numeric(function, 1)); break;
                case "scalex": case "scaleX": component = CSSTransformComponent.Scale(Numeric(function, 0), One); break;
                case "scaley": case "scaleY": component = CSSTransformComponent.Scale(One, Numeric(function, 0)); break;
                case "scalez": case "scaleZ": component = CSSTransformComponent.Scale(One, One, Numeric(function, 0)); break;
                case "scale3d": component = CSSTransformComponent.Scale(Numeric(function, 0), Numeric(function, 1), Numeric(function, 2)); break;
                case "rotate": component = CSSTransformComponent.Rotate(Numeric(function, 0)); break;
                case "rotatex": case "rotateX": component = CSSTransformComponent.Rotate(One, ZeroNumber, ZeroNumber, Numeric(function, 0)); break;
                case "rotatey": case "rotateY": component = CSSTransformComponent.Rotate(ZeroNumber, One, ZeroNumber, Numeric(function, 0)); break;
                case "rotatez": case "rotateZ": component = CSSTransformComponent.Rotate(ZeroNumber, ZeroNumber, One, Numeric(function, 0)); break;
                case "rotate3d": component = CSSTransformComponent.Rotate(Numeric(function, 0), Numeric(function, 1), Numeric(function, 2), Numeric(function, 3)); break;
                case "skew": component = CSSTransformComponent.Skew(Numeric(function, 0), Count(function) == 1 ? ZeroAngle : Numeric(function, 1)); break;
                case "skewx": case "skewX": component = CSSTransformComponent.Skew(Numeric(function, 0), ZeroAngle); break;
                case "skewy": case "skewY": component = CSSTransformComponent.Skew(ZeroAngle, Numeric(function, 0)); break;
                case "perspective":
                    component = function.Arguments is CssKeywordValue { Value: "none" } ? CSSTransformComponent.Perspective("none") : CSSTransformComponent.Perspective(Numeric(function, 0)); break;
                case "matrix": case "matrix3d":
                    int length = function.Name == "matrix" ? 6 : 16;
                    var matrix = new DOMMatrix();
                    Span<double> entries = stackalloc double[16];
                    for (int i = 0; i < length; i++) entries[i] = Numeric(function, i).To("number").Value;
                    if (length == 6) { matrix.M11 = entries[0]; matrix.M12 = entries[1]; matrix.M21 = entries[2]; matrix.M22 = entries[3]; matrix.M41 = entries[4]; matrix.M42 = entries[5]; }
                    else
                    {
                        matrix.M11 = entries[0]; matrix.M12 = entries[1]; matrix.M13 = entries[2]; matrix.M14 = entries[3];
                        matrix.M21 = entries[4]; matrix.M22 = entries[5]; matrix.M23 = entries[6]; matrix.M24 = entries[7];
                        matrix.M31 = entries[8]; matrix.M32 = entries[9]; matrix.M33 = entries[10]; matrix.M34 = entries[11];
                        matrix.M41 = entries[12]; matrix.M42 = entries[13]; matrix.M43 = entries[14]; matrix.M44 = entries[15]; matrix.Mark3D();
                    }
                    component = CSSTransformComponent.FromMatrix(matrix, length == 6); break;
                default: return false;
            }
            return true;
        }
    }
}
