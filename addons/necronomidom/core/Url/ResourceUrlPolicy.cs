using System;
using System.Text;

namespace NecronomiDom.Url
{
    /// <summary>
    /// Markup-profile URL policy: references resolve against the document's empty-host base URL
    /// (<c>res:///ui/asset</c>) by the URL Standard parser; allowed results are <c>res:///asset</c> (mapped to Godot
    /// <c>res://asset</c>, or to <see cref="WithRoot"/>'s root), <c>user:///ui/asset</c> (mapped to <c>user://ui/asset</c>) and
    /// <c>data:</c> ≤ 1 MiB for stylesheets, images and fonts. Everything else — other schemes, non-empty hosts,
    /// <c>..</c> segments that would climb above the root, backslashes (which Chrome's <c>file:</c> resolution
    /// would treat as separators) — is rejected.
    /// </summary>
    public sealed class ResourceUrlPolicy
    {
        /// <summary>Maximum decoded size of a <c>data:</c> URL body.</summary>
        public const int MaxDataUrlBytes = 1024 * 1024;

        private readonly string _resRoot;

        private ResourceUrlPolicy(string resRoot) => _resRoot = resRoot;

        /// <summary>The product policy: <c>res:///p</c> → <c>res://p</c>.</summary>
        public static ResourceUrlPolicy Default { get; } = new("res://");

        /// <summary>Policy mapping <c>res:///p</c> to <paramref name="resRoot"/> + p (WPT harness: <c>res://tests/wpt/</c>).</summary>
        public static ResourceUrlPolicy WithRoot(string resRoot)
        {
            if (!resRoot.StartsWith("res://", StringComparison.Ordinal) || !resRoot.EndsWith('/'))
                throw new ArgumentException("root must be a res:// directory ending in '/'", nameof(resRoot));
            return new ResourceUrlPolicy(resRoot);
        }

        /// <summary>Resolves <paramref name="reference"/> against <paramref name="documentUrl"/> and applies the policy.</summary>
        public UrlResolution Resolve(string reference, WebUrl documentUrl, ResourceKind kind = ResourceKind.Other)
        {
            if (reference.Contains('\\', StringComparison.Ordinal))
                return UrlResolution.Reject(null, "backslash in URL (not portable between res: and Chrome's file: resolution)");
            var url = WebUrlParser.Parse(reference, documentUrl, out var climbed);
            if (url == null) return UrlResolution.Reject(null, "not a valid URL");
            if (climbed) return UrlResolution.Reject(url, "'..' climbs above the root");
            switch (url.Scheme)
            {
                case "data":
                    return ResolveData(url, kind);
                case "res":
                    if (url.Host != string.Empty) return UrlResolution.Reject(url, "res: URLs must have an empty host (res:///path)");
                    return MapPath(url, _resRoot, 0);
                case "user":
                    if (url.Host != string.Empty || url.Segments.Count < 2 || url.Segments[0] != "ui")
                        return UrlResolution.Reject(url, "user: URLs are limited to user:///ui/asset");
                    return MapPath(url, "user://ui/", 1);
                default:
                    return UrlResolution.Reject(url, $"scheme '{url.Scheme}:' is not allowed");
            }
        }

        private static UrlResolution MapPath(WebUrl url, string root, int skip)
        {
            var sb = new StringBuilder(root);
            for (var i = skip; i < url.Segments.Count; i++)
            {
                var segment = PercentEncoding.DecodeToString(url.Segments[i]);
                foreach (var c in segment)
                    if (c is '/' or '\\' || c < 0x20 || c == 0x7F)
                        return UrlResolution.Reject(url, "path segment contains an encoded separator or control character");
                if (i > skip) sb.Append('/');
                sb.Append(segment);
            }
            return UrlResolution.Allow(url, sb.ToString());
        }

        /// <summary>Size check per the Fetch "data: URL processor" (body percent-decoded, then forgiving-base64 if ";base64").</summary>
        private static UrlResolution ResolveData(WebUrl url, ResourceKind kind)
        {
            if (kind is not (ResourceKind.Stylesheet or ResourceKind.Image or ResourceKind.Font))
                return UrlResolution.Reject(url, $"data: URLs are not allowed for {kind}");
            var input = url.Serialize(excludeFragment: true).Substring("data:".Length);
            var comma = input.IndexOf(',', StringComparison.Ordinal);
            if (comma < 0) return UrlResolution.Reject(url, "data: URL has no ','");
            var mime = input.Substring(0, comma).TrimEnd(' ', '\t', '\n', '\f', '\r');
            var body = PercentEncoding.DecodeToBytes(input.Substring(comma + 1));
            long size = body.Length;
            if (IsBase64(mime))
            {
                var decoded = ForgivingBase64Length(body);
                if (decoded < 0) return UrlResolution.Reject(url, "data: URL has invalid base64");
                size = decoded;
            }
            return size > MaxDataUrlBytes
                ? UrlResolution.Reject(url, $"data: URL body is {size} bytes (limit {MaxDataUrlBytes})")
                : UrlResolution.Allow(url, null);
        }

        private static bool IsBase64(string mime)
        {
            var semicolon = mime.LastIndexOf(';');
            if (semicolon < 0) return false;
            var tail = mime.Substring(semicolon + 1).Trim(' ', '\t', '\n', '\f', '\r');
            return tail.Equals("base64", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Decoded byte count of Infra's "forgiving-base64 decode", or −1 on failure.</summary>
        private static long ForgivingBase64Length(byte[] data)
        {
            long length = 0;
            var padding = 0;
            foreach (var b in data)
            {
                if (b is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\f' or (byte)'\r') continue;
                if (b == '=')
                {
                    padding++;
                    continue;
                }
                if (padding > 0) return -1;
                var valid = b is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z' or >= (byte)'0' and <= (byte)'9'
                    or (byte)'+' or (byte)'/';
                if (!valid) return -1;
                length++;
            }
            if (padding > 0 && ((length + padding) % 4 != 0 || padding > 2)) return -1;
            if (length % 4 == 1) return -1;
            return length * 3 / 4;
        }
    }
}
