using System;
using NecronomiDom.Css.Om;
using NecronomiDom.Css.Values;
using NecronomiDom.Css.Values.Colors;
using NecronomiDom.Dom;
using NecronomiDom.Paint;

namespace NecronomiDom.Layout;

public sealed partial class FlowLayout
{
    private void PaintScrollbars(Element element, CSSStyleDeclaration style, Rect port)
    {
        if (element.ScrollState is not { } state || !state.UserX && !state.UserY) return;
        bool x = state.GutterBottom > 0, y = state.GutterRight > 0;
        if (!x && !y) return;
        double size = Math.Max(state.GutterRight, state.GutterBottom);
        double inset = Math.Min(size / 2, ScrollbarLength(style, "--necronomidom-scrollbar-inset", 1));
        double radius = ScrollbarLength(style, "--necronomidom-scrollbar-radius", 4);
        Rgba foreground = Color(style, "color");
        Rgba track = ScrollbarColor(style, "--necronomidom-scrollbar-track", new Rgba(0, 0, 0, 0.15f), foreground);
        Rgba thumb = ScrollbarColor(style, "--necronomidom-scrollbar-thumb", new Rgba(0.45f, 0.45f, 0.45f, 1), foreground);
        Rgba hover = ScrollbarColor(style, "--necronomidom-scrollbar-thumb-hover", thumb, foreground);
        if (y) PaintScrollbar(element, false, new Rect(port.X + port.Width, port.Y, state.GutterRight, port.Height),
            state.Top, state.Height, state.ClientHeight, inset, radius, track, state.HoverY ? hover : thumb, port);
        if (x) PaintScrollbar(element, true, new Rect(port.X, port.Y + port.Height, port.Width, state.GutterBottom),
            state.Left, state.Width, state.ClientWidth, inset, radius, track, state.HoverX ? hover : thumb, port);
    }
    private void PaintScrollbar(Element element, bool horizontal, Rect track, double offset, double content, double client,
        double inset, double radius, Rgba trackColor, Rgba thumbColor, Rect port)
    {
        double extent = horizontal ? track.Width : track.Height;
        double length = Math.Min(extent, Math.Max(16, content <= 0 ? extent : extent * client / content));
        double position = content <= client ? 0 : offset / (content - client) * (extent - length);
        Rect thumb = horizontal ? new Rect(track.X + position + inset, track.Y + inset, Math.Max(0, length - 2 * inset), Math.Max(0, track.Height - 2 * inset))
            : new Rect(track.X + inset, track.Y + position + inset, Math.Max(0, track.Width - 2 * inset), Math.Max(0, length - 2 * inset));
        _result.Runs.Add(new BoxRun(track, new BoxStyle(trackColor, trackColor, 0, radius)));
        _result.Runs.Add(new BoxRun(thumb, new BoxStyle(thumbColor, thumbColor, 0, radius)));
        _result.Scrollbars.Add(new ScrollbarGeometry(element, horizontal,
            Translate(track, -port.X, -port.Y), Translate(thumb, -port.X, -port.Y)));
    }
    private static double ScrollbarLength(CSSStyleDeclaration style, string name, double fallback)
    {
        string value = style.GetPropertyValue(name);
        return value.Length == 0 ? fallback : Math.Max(0, Number(value, 0));
    }
    private static Rgba ScrollbarColor(CSSStyleDeclaration style, string name, Rgba fallback, Rgba foreground)
    {
        var value = style.GetSpecifiedValue(name);
        if (value == null) return fallback;
        if (value is CssUnparsedValue tokens)
        {
            if (!CssColorParser.TryParse(tokens.Tokens, out value)) throw new NotSupportedException("Scrollbar color must parse: " + name);
        }
        if (value is not CssColorValue color || !color.TryResolve(new CssColorContext
            { CurrentColor = new CssResolvedColor("srgb", foreground.R, foreground.G, foreground.B, foreground.A) }, out var resolved))
            throw new NotSupportedException("Scrollbar color must resolve: " + name);
        var rgb = resolved.ToSrgb(); return new Rgba((float)rgb.C1, (float)rgb.C2, (float)rgb.C3, (float)rgb.Alpha);
    }
}
