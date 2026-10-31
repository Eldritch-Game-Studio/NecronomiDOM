using System;
using System.Collections.Generic;
using NecronomiDom.Dom;

namespace NecronomiDom.Html
{
    internal sealed class HtmlSourceMap
    {
        private readonly List<int> _lines = new() { 0 };
        private readonly int _length;
        internal HtmlSourceMap(string source)
        {
            _length = source.Length;
            for (var i = 0; i < source.Length; i++)
            {
                if (source[i] == '\r') { if (i + 1 < source.Length && source[i + 1] == '\n') i++; _lines.Add(i + 1); }
                else if (source[i] == '\n') _lines.Add(i + 1);
            }
        }
        internal DomSourceLocation At(int offset)
        {
            offset = Math.Clamp(offset, 0, _length);
            var line = _lines.BinarySearch(offset);
            if (line < 0) line = ~line - 1;
            return new DomSourceLocation(line + 1, offset - _lines[line] + 1, offset);
        }
    }
}
