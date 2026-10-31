namespace NecronomiDom.Css.Values.Colors
{
    /// <summary>
    /// CSS Color 4 §6.2 system-color roles shared by UA declarations and color computation.
    /// These are the product's default palettes, not OS samples; hosts can supply their own
    /// role values or an authoritative CssColorContext.SystemColor provider.
    /// </summary>
    public sealed record CssSystemColorPalette
    {
        public static CssSystemColorPalette Light { get; } = new();
        public static CssSystemColorPalette Dark { get; } = new()
        {
            Canvas=Rgb(18,18,18), CanvasText=Rgb(255,255,255),
            ButtonFace=Rgb(52,52,52), ButtonText=Rgb(255,255,255), ButtonBorder=Rgb(160,160,160),
            Field=Rgb(33,33,33), FieldText=Rgb(255,255,255), GrayText=Rgb(160,160,160),
            LinkText=Rgb(158,158,255), ActiveText=Rgb(255,128,128), VisitedText=Rgb(208,160,255),
        };

        public CssResolvedColor Canvas { get; init; } = Rgb(255,255,255);
        public CssResolvedColor CanvasText { get; init; } = Rgb(0,0,0);
        public CssResolvedColor ButtonFace { get; init; } = Rgb(240,240,240);
        public CssResolvedColor ButtonText { get; init; } = Rgb(0,0,0);
        public CssResolvedColor ButtonBorder { get; init; } = Rgb(118,118,118);
        public CssResolvedColor Field { get; init; } = Rgb(255,255,255);
        public CssResolvedColor FieldText { get; init; } = Rgb(0,0,0);
        public CssResolvedColor GrayText { get; init; } = Rgb(109,109,109);
        public CssResolvedColor LinkText { get; init; } = Rgb(0,0,238);
        public CssResolvedColor ActiveText { get; init; } = Rgb(255,0,0);
        public CssResolvedColor VisitedText { get; init; } = Rgb(85,26,139);
        public CssResolvedColor Mark { get; init; } = Rgb(255,255,0);
        public CssResolvedColor MarkText { get; init; } = Rgb(0,0,0);
        public CssResolvedColor Highlight { get; init; } = Rgb(0,96,192);
        public CssResolvedColor HighlightText { get; init; } = Rgb(255,255,255);
        public CssResolvedColor SelectedItem { get; init; } = Rgb(0,96,192);
        public CssResolvedColor SelectedItemText { get; init; } = Rgb(255,255,255);
        public CssResolvedColor AccentColor { get; init; } = Rgb(0,96,192);
        public CssResolvedColor AccentColorText { get; init; } = Rgb(255,255,255);

        /// <summary>Resolve a system keyword, including Color 4's mandatory deprecated aliases.</summary>
        public CssResolvedColor? Resolve(string keyword) => keyword.ToLowerInvariant() switch
        {
            "canvas" or "activecaption" or "appworkspace" or "background" or "inactivecaption" or "infobackground" or "menu" or "scrollbar" or "window" => Canvas,
            "canvastext" or "captiontext" or "infotext" or "menutext" or "windowtext" => CanvasText,
            "buttonface" or "buttonhighlight" or "buttonshadow" or "threedface" => ButtonFace,
            "buttontext" => ButtonText,
            "buttonborder" or "activeborder" or "inactiveborder" or "threeddarkshadow" or "threedhighlight" or "threedlightshadow" or "threedshadow" or "windowframe" => ButtonBorder,
            "field" => Field,
            "fieldtext" => FieldText,
            "graytext" or "inactivecaptiontext" => GrayText,
            "linktext" => LinkText,
            "activetext" => ActiveText,
            "visitedtext" => VisitedText,
            "mark" => Mark,
            "marktext" => MarkText,
            "highlight" => Highlight,
            "highlighttext" => HighlightText,
            "selecteditem" => SelectedItem,
            "selecteditemtext" => SelectedItemText,
            "accentcolor" => AccentColor,
            "accentcolortext" => AccentColorText,
            _ => null,
        };
        private static CssResolvedColor Rgb(double r,double g,double b) => new("srgb",r/255,g/255,b/255);
    }
}
