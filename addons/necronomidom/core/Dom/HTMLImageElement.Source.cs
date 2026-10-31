using System;
using NecronomiDom.Url;

namespace NecronomiDom.Dom;

public partial class HTMLImageElement
{
    private string _decodedReference = "", _decodedBase = "", _currentSrc = "";
    private int _naturalWidth, _naturalHeight;
    public string Src
    {
        get
        {
            string? reference = GetAttribute("src");
            return reference == null ? "" : WebUrl.Parse(reference, NodeDocument.BaseUrl)?.Href ?? reference;
        }
        set => SetAttribute("src", value);
    }
    public string Alt { get => GetAttribute("alt") ?? ""; set => SetAttribute("alt", value); }
    private bool Decoded => _currentSrc.Length != 0 && _decodedReference == GetAttribute("src") && _decodedBase == BaseURI;
    public string CurrentSrc => Decoded ? _currentSrc : "";
    public int NaturalWidth => Decoded ? _naturalWidth : 0;
    public int NaturalHeight => Decoded ? _naturalHeight : 0;
    public bool Complete => !HasAttribute("src") || GetAttribute("src") == "" || Decoded;
    /// <summary>The synchronous native source adapter publishes successful intrinsic decoding, not synthetic dimensions.</summary>
    public void SetDecodedSource(string url, int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        _decodedReference = GetAttribute("src") ?? ""; _decodedBase = BaseURI;
        _currentSrc = url; _naturalWidth = width; _naturalHeight = height;
    }
}
