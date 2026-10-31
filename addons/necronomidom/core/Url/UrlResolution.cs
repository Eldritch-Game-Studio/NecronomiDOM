namespace NecronomiDom.Url
{
    /// <summary>
    /// What a URL reference is fetched as; decides whether <c>data:</c> is acceptable
    /// (markup profile: data: URLs are allowed for images, fonts and stylesheets only, never for documents).
    /// </summary>
    public enum ResourceKind { Other, Document, Stylesheet, Image, Font }

    /// <summary>Outcome of <see cref="ResourceUrlPolicy.Resolve"/> (markup-profile URL policy).</summary>
    public sealed class UrlResolution
    {
        private UrlResolution(bool allowed, WebUrl? url, string? resourcePath, string? rejectReason)
        {
            Allowed = allowed;
            Url = url;
            ResourcePath = resourcePath;
            RejectReason = rejectReason;
        }

        /// <summary>True when the reference may be loaded.</summary>
        public bool Allowed { get; }

        /// <summary>The resolved URL (null when parsing failed).</summary>
        public WebUrl? Url { get; }

        /// <summary>Godot path to load (<c>res://p</c> or <c>user://ui/p</c>); null for <c>data:</c> and rejections.</summary>
        public string? ResourcePath { get; }

        /// <summary>Why the reference was rejected (null when allowed).</summary>
        public string? RejectReason { get; }

        internal static UrlResolution Allow(WebUrl url, string? resourcePath) => new(true, url, resourcePath, null);

        internal static UrlResolution Reject(WebUrl? url, string reason) => new(false, url, null, reason);

        public override string ToString() => Allowed ? $"allowed {ResourcePath ?? Url?.Href}" : $"rejected: {RejectReason}";
    }
}
