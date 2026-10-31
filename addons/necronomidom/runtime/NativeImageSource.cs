#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Godot;
using NecronomiDom.Dom;
using NecronomiDom.Layout;
using NecronomiDom.Paint;
using NecronomiDom.Url;

namespace NecronomiDom.Runtime;

/// <summary>Document-scoped decoded images; all paths pass the document resource URL policy.</summary>
public sealed class NativeImageSource : IImageSource, IBackgroundImageSource, IDisposable
{
    private sealed record Loaded(Image Image, ImageTexture Texture);
    private readonly Dictionary<string, Loaded> _images = new(StringComparer.Ordinal);
    private readonly Dictionary<(string, int, int), ImageTexture> _pixelated = new();
    public bool LinearComposition { get; init; }
    public ImageResource Load(Element element)
    {
        if (element is not HTMLImageElement img) throw new ArgumentException("Image source requires img.", nameof(element));
        if (img.HasAttribute("srcset") || img.ParentElement?.LocalName == "picture") throw new NotSupportedException("srcset/picture selection remains open.");
        string src = img.GetAttribute("src") ?? "";
        if (src.Length == 0) throw new NotSupportedException("Missing img src; broken-image/alt presentation remains open.");
        string current = img.CurrentSrc;
        if (current.Length > 0 && _images.TryGetValue(current, out var cached))
            return new ImageResource(current, cached.Image.GetWidth(), cached.Image.GetHeight());
        var resource = LoadResource(img, src);
        img.SetDecodedSource(resource.Key, (int)resource.Width, (int)resource.Height);
        return resource;
    }
    public ImageResource LoadBackground(Element owner, string sourceUrl) => LoadResource(owner, sourceUrl);
    private ImageResource LoadResource(Element owner, string sourceUrl)
    {
        var resolution = ResourceUrlPolicy.Default.Resolve(sourceUrl, owner.OwnerDocument?.BaseUrl ?? throw new InvalidOperationException("Missing document URL."), ResourceKind.Image);
        if (!resolution.Allowed || resolution.Url == null) throw new NotSupportedException("Runtime image URL: " + resolution);
        string key = resolution.Url.Serialize(excludeFragment: true);
        if (!_images.TryGetValue(key, out var loaded))
        {
            byte[] bytes;
            if (resolution.ResourcePath != null)
            {
                using var file = Godot.FileAccess.Open(resolution.ResourcePath, Godot.FileAccess.ModeFlags.Read);
                if (file == null) throw new InvalidOperationException("Cannot load image " + resolution.ResourcePath + ": " + Godot.FileAccess.GetOpenError());
                bytes = file.GetBuffer((long)file.GetLength());
            }
            else bytes = ResourceDataBody.Decode(resolution);
            var image = new Image();
            Error error;
            if (bytes.Length >= 8 && bytes[0] == 137 && bytes[1] == 80 && bytes[2] == 78 && bytes[3] == 71) error = image.LoadPngFromBuffer(bytes);
            else if (bytes.Length >= 2 && bytes[0] == 255 && bytes[1] == 216) error = image.LoadJpgFromBuffer(bytes);
            else if (bytes.Length >= 12 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8)) error = image.LoadWebpFromBuffer(bytes);
            else { image.Dispose(); throw new NotSupportedException("Runtime image decoder supports PNG/JPEG/WebP; other media remain open: " + key); }
            if (error != Error.Ok) { image.Dispose(); throw new InvalidOperationException("Cannot decode image: " + key + ": " + error); }
            if (LinearComposition) LinearPremultiply(image);
            else image.PremultiplyAlpha();
            loaded = new Loaded(image, ImageTexture.CreateFromImage(image)); _images.Add(key, loaded);
        }
        return new ImageResource(key, loaded.Image.GetWidth(), loaded.Image.GetHeight());
    }
    public (Texture2D Texture, Rect Source) Texture(ImageRun run)
    {
        var loaded = _images[run.ResourceKey];
        if (run.Sampling != ImageSampling.Pixelated) return (loaded.Texture, run.Source);
        int sx = Math.Max(1, (int)Math.Round(run.Destination.Width / run.Source.Width, MidpointRounding.AwayFromZero));
        int sy = Math.Max(1, (int)Math.Round(run.Destination.Height / run.Source.Height, MidpointRounding.AwayFromZero));
        if (sx == 1 && sy == 1) return (loaded.Texture, run.Source);
        var key = (run.ResourceKey, sx, sy);
        if (!_pixelated.TryGetValue(key, out var texture))
        {
            using var resized = (Image)loaded.Image.Duplicate();
            resized.Resize(checked(loaded.Image.GetWidth() * sx), checked(loaded.Image.GetHeight() * sy), Image.Interpolation.Nearest);
            texture = ImageTexture.CreateFromImage(resized); _pixelated.Add(key, texture);
        }
        return (texture, new Rect(run.Source.X * sx, run.Source.Y * sy, run.Source.Width * sx, run.Source.Height * sy));
    }
    private static void LinearPremultiply(Image image)
    {
        // Decode before premultiplication/filtering, without the 8-bit linear quantization
        // of Image.SrgbToLinear(). Float textures have no automatic sRGB sampler decode.
        image.Convert(Image.Format.Rgbaf);
        byte[] data = image.GetData();
        Span<float> pixels = MemoryMarshal.Cast<byte, float>(data.AsSpan());
        for (int i = 0; i < pixels.Length; i += 4)
        {
            var c = new Color(pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]).SrgbToLinear();
            pixels[i] = c.R * c.A; pixels[i + 1] = c.G * c.A; pixels[i + 2] = c.B * c.A;
        }
        image.SetData(image.GetWidth(), image.GetHeight(), false, Image.Format.Rgbaf, data);
    }
    internal NativeImageOwnership[] CaptureOwnership()
    {
        var result = new NativeImageOwnership[_images.Count];
        int i = 0;
        foreach (var pair in _images)
            result[i++] = new(pair.Key, pair.Value.Texture.GetInstanceId(), pair.Value.Texture.GetRid().Id,
                pair.Value.Texture.GetReferenceCount(), pair.Value.Image.GetInstanceId(), pair.Value.Image.GetReferenceCount());
        return result;
    }
    public void Clear()
    {
        foreach (var texture in _pixelated.Values) texture.Dispose(); _pixelated.Clear();
        foreach (var loaded in _images.Values) { loaded.Texture.Dispose(); loaded.Image.Dispose(); } _images.Clear();
    }
    public void Dispose() => Clear();
}

internal readonly record struct NativeImageOwnership(string Key, ulong TextureId, ulong TextureRid,
    long TextureReferences, ulong ImageId, long ImageReferences);
