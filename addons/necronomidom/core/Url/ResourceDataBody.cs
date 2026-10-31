using System;
using System.Text;

namespace NecronomiDom.Url;

/// <summary>Decoded bytes of a policy-approved data resource; uses the same percent/base64 rules as its size check.</summary>
public static class ResourceDataBody
{
    public static byte[] Decode(UrlResolution resolution)
    {
        if (!resolution.Allowed || resolution.Url?.Scheme != "data") throw new ArgumentException("Expected an allowed data URL.", nameof(resolution));
        string url = resolution.Url.Serialize(excludeFragment: true);
        int comma = url.IndexOf(',');
        byte[] body = PercentEncoding.DecodeToBytes(url[(comma + 1)..]);
        string mime = url[5..comma].TrimEnd(' ', '\t', '\n', '\f', '\r');
        int semicolon = mime.LastIndexOf(';');
        if (semicolon < 0 || !mime[(semicolon + 1)..].Trim(' ', '\t', '\n', '\f', '\r').Equals("base64", StringComparison.OrdinalIgnoreCase)) return body;
        var cleaned = new StringBuilder(body.Length);
        foreach (byte value in body) if (value is not (32 or 9 or 10 or 12 or 13)) cleaned.Append((char)value);
        while (cleaned.Length % 4 != 0) cleaned.Append('=');
        return Convert.FromBase64String(cleaned.ToString());
    }
}
