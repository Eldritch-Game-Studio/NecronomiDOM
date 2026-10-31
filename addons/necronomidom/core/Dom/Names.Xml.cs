using System;

namespace NecronomiDom.Dom
{
    internal static partial class Names
    {
        // XML 1.0 fifth edition § 2.3 Names, including supplementary scalar values.
        // XmlConvert uses an older name table and rejects otherwise valid astral names.
        internal static bool IsXmlName(ReadOnlySpan<char> name, bool allowColon = true)
        {
            if (name.IsEmpty) return false;
            for (var i = 0; i < name.Length; i++)
            {
                var first = i == 0;
                int scalar = name[i];
                if (char.IsHighSurrogate(name[i]))
                {
                    if (i + 1 == name.Length || !char.IsLowSurrogate(name[i + 1])) return false;
                    scalar = char.ConvertToUtf32(name[i], name[i + 1]);
                    i++;
                }
                else if (char.IsLowSurrogate(name[i])) return false;
                if (!XmlNameStart(scalar, allowColon) && (first || !XmlNameContinuation(scalar))) return false;
            }
            return true;
        }
        private static bool XmlNameStart(int c, bool allowColon) =>
            allowColon && c == ':' || c == '_' || c is >= 'A' and <= 'Z' or >= 'a' and <= 'z'
                or >= 0xC0 and <= 0xD6 or >= 0xD8 and <= 0xF6 or >= 0xF8 and <= 0x2FF
                or >= 0x370 and <= 0x37D or >= 0x37F and <= 0x1FFF or >= 0x200C and <= 0x200D
                or >= 0x2070 and <= 0x218F or >= 0x2C00 and <= 0x2FEF or >= 0x3001 and <= 0xD7FF
                or >= 0xF900 and <= 0xFDCF or >= 0xFDF0 and <= 0xFFFD or >= 0x10000 and <= 0xEFFFF;
        private static bool XmlNameContinuation(int c) =>
            c is '-' or '.' or >= '0' and <= '9' or 0xB7 or >= 0x300 and <= 0x36F or >= 0x203F and <= 0x2040;
    }
}
