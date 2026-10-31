using System;

namespace NecronomiDom.Dom
{
    internal static partial class Names
    {
        internal static string AsciiLower(string text) => AsciiCase(text, false);
        internal static string AsciiUpper(string text) => AsciiCase(text, true);
        internal static bool AsciiEqual(string? left, string right)
            => left != null && AsciiEqual(left.AsSpan(), right.AsSpan());
        internal static bool AsciiEqual(ReadOnlySpan<char> left, ReadOnlySpan<char> right)
        {
            if (left.Length != right.Length) return false;
            for (var i = 0; i < left.Length; i++)
            {
                var a = left[i]; var b = right[i];
                if (a is >= 'A' and <= 'Z') a = (char)(a + 32);
                if (b is >= 'A' and <= 'Z') b = (char)(b + 32);
                if (a != b) return false;
            }
            return true;
        }
        private static string AsciiCase(string text, bool upper)
        {
            var changes = false;
            foreach (var c in text) if (upper ? c is >= 'a' and <= 'z' : c is >= 'A' and <= 'Z') { changes = true; break; }
            if (!changes) return text;
            return string.Create(text.Length, (text, upper), static (output, state) =>
            {
                for (var i = 0; i < output.Length; i++)
                {
                    var c = state.text[i];
                    output[i] = state.upper && c is >= 'a' and <= 'z' ? (char)(c - 32) : !state.upper && c is >= 'A' and <= 'Z' ? (char)(c + 32) : c;
                }
            });
        }
    }
}
