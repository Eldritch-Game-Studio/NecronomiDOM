using System.Collections.Generic;
using System.Text;

namespace NecronomiDom.Url
{
    /// <summary>
    /// A parsed URL record (URL Standard § 4.1 "URL representation") with the URL serializer (§ 4.5).
    /// Produced by <see cref="Parse"/> (the basic URL parser, § 4.4). Immutable.
    /// </summary>
    public sealed class WebUrl
    {
        internal WebUrl(string scheme, string username, string password, string? host, int? port,
                        IReadOnlyList<string>? segments, string? opaquePath, string? query, string? fragment)
        {
            Scheme = scheme;
            Username = username;
            Password = password;
            Host = host;
            Port = port;
            Segments = segments ?? System.Array.Empty<string>();
            OpaquePath = opaquePath;
            Query = query;
            Fragment = fragment;
            Href = Serialize(excludeFragment: false);
        }

        /// <summary>The scheme, ASCII-lowercased, without the trailing ":".</summary>
        public string Scheme { get; }

        public string Username { get; }

        public string Password { get; }

        /// <summary>Serialized host; null when the URL has no host, "" for an empty host (e.g. <c>res:///x</c>).</summary>
        public string? Host { get; }

        public int? Port { get; }

        /// <summary>Path segments (percent-encoded) when the path is not opaque.</summary>
        public IReadOnlyList<string> Segments { get; }

        /// <summary>The opaque path (e.g. the body of a <c>data:</c> URL), or null for a segmented path.</summary>
        public string? OpaquePath { get; }

        public bool HasOpaquePath => OpaquePath != null;

        /// <summary>The query without "?" (null when absent).</summary>
        public string? Query { get; }

        /// <summary>The fragment without "#" (null when absent).</summary>
        public string? Fragment { get; }

        /// <summary>The URL serializer output (§ 4.5).</summary>
        public string Href { get; }

        /// <summary>URL path serializer output (§ 4.5) — the <c>pathname</c>.</summary>
        public string Path => OpaquePath ?? SerializePath(Segments);

        /// <summary>True for the special schemes of URL Standard § 4.2.</summary>
        public bool IsSpecial => IsSpecialScheme(Scheme);

        /// <summary>Runs the basic URL parser (URL Standard § 4.4) on <paramref name="input"/> against an optional base.</summary>
        public static WebUrl? Parse(string input, WebUrl? baseUrl = null) => WebUrlParser.Parse(input, baseUrl, out _);

        /// <summary>The URL serializer with "exclude fragment" (§ 4.5).</summary>
        public string Serialize(bool excludeFragment)
        {
            var sb = new StringBuilder(Scheme.Length + 16);
            sb.Append(Scheme).Append(':');
            if (Host != null)
            {
                sb.Append("//");
                if (Username.Length > 0 || Password.Length > 0)
                {
                    sb.Append(Username);
                    if (Password.Length > 0) sb.Append(':').Append(Password);
                    sb.Append('@');
                }
                sb.Append(Host);
                if (Port != null) sb.Append(':').Append(Port.Value);
            }
            else if (OpaquePath == null && Segments.Count > 1 && Segments[0].Length == 0)
            {
                sb.Append("/.");
            }
            sb.Append(Path);
            if (Query != null) sb.Append('?').Append(Query);
            if (!excludeFragment && Fragment != null) sb.Append('#').Append(Fragment);
            return sb.ToString();
        }

        public override string ToString() => Href;

        public override bool Equals(object? obj) => obj is WebUrl other && other.Href == Href;

        public override int GetHashCode() => Href.GetHashCode(System.StringComparison.Ordinal);

        internal static bool IsSpecialScheme(string scheme) => scheme is "ftp" or "file" or "http" or "https" or "ws" or "wss";

        internal static int? DefaultPort(string scheme) => scheme switch
        {
            "ftp" => 21,
            "http" or "ws" => 80,
            "https" or "wss" => 443,
            _ => null,
        };

        private static string SerializePath(IReadOnlyList<string> segments)
        {
            if (segments.Count == 0) return string.Empty;
            var sb = new StringBuilder();
            foreach (var s in segments) sb.Append('/').Append(s);
            return sb.ToString();
        }
    }
}
