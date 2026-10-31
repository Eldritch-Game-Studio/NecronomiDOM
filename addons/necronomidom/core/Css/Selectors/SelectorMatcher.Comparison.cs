using System;
using NecronomiDom.Dom;

namespace NecronomiDom.Css.Selectors
{
    public static partial class SelectorMatcher
    {
        private static bool AttributeEqual(ReadOnlySpan<char> value, ReadOnlySpan<char> expected, bool insensitive)
            => insensitive ? Names.AsciiEqual(value, expected) : value.SequenceEqual(expected);
        private static bool AttributePrefix(string value, string expected, bool insensitive)
            => value.Length >= expected.Length && AttributeEqual(value.AsSpan(0, expected.Length), expected.AsSpan(), insensitive);
        private static int AttributeIndex(string value, string expected, bool insensitive)
        {
            if (!insensitive) return value.IndexOf(expected, StringComparison.Ordinal);
            for (int i = 0; i <= value.Length - expected.Length; i++)
                if (Names.AsciiEqual(value.AsSpan(i, expected.Length), expected.AsSpan())) return i;
            return -1;
        }
    }
}
