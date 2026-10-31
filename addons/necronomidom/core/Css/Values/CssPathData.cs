using System;
using System.Globalization;
using System.Text;

namespace NecronomiDom.Css.Values
{
    internal static class CssPathData
    {
        internal static bool IsValid(string path) => Parse(path, null);
        internal static string Serialize(string path)
        {
            var result = new StringBuilder(path.Length);
            if (!Parse(path, result)) throw new ArgumentException("Invalid SVG path data.", nameof(path));
            return result.ToString();
        }
        private static bool Parse(string path, StringBuilder? result)
        {
            int pos = 0;
            char command = '\0';
            bool first = true;
            Span<double> values = stackalloc double[7];
            while (true)
            {
                SkipSpace(path, ref pos);
                if (pos == path.Length) return !first;
                if (char.IsAsciiLetter(path[pos]))
                {
                    command = path[pos++];
                    if (first && command is not ('M' or 'm')) return false;
                    if (Upper(command) == 'Z')
                    {
                        AppendCommand(result, 'Z');
                        first = false; command = '\0'; continue;
                    }
                }
                int count = Upper(command) switch
                {
                    'M' or 'L' or 'T' => 2, 'H' or 'V' => 1, 'C' => 6, 'S' or 'Q' => 4, 'A' => 7, _ => 0,
                };
                if (count == 0) return false;
                for (int i = 0; i < count; i++)
                {
                    SkipSpace(path, ref pos);
                    if (i > 0 && pos < path.Length && path[pos] == ',') { pos++; SkipSpace(path, ref pos); }
                    if (Upper(command) == 'A' && i is 3 or 4)
                    {
                        if (pos >= path.Length || path[pos] is not ('0' or '1')) return false;
                        values[i] = path[pos++] - '0';
                    }
                    else
                    {
                        if (!Number(path, ref pos, out values[i])) return false;
                        if (Upper(command) == 'A' && i < 2 && values[i] < 0) return false;
                    }
                }
                AppendCommand(result, command);
                if (result != null)
                    for (int i = 0; i < count; i++) result.Append(' ').Append(NumberFormat.Format(values[i]));
                first = false;
                if (command == 'M') command = 'L'; else if (command == 'm') command = 'l';
                SkipSpace(path, ref pos);
                if (pos < path.Length && path[pos] == ',')
                {
                    pos++;
                    int next = pos;
                    SkipSpace(path, ref next);
                    if (next >= path.Length || char.IsAsciiLetter(path[next])) return false;
                }
            }
        }
        private static char Upper(char command) => command is >= 'a' and <= 'z' ? (char)(command - 32) : command;
        private static void AppendCommand(StringBuilder? result, char command)
        {
            if (result == null) return;
            if (result.Length != 0) result.Append(' ');
            result.Append(command);
        }
        private static void SkipSpace(string source, ref int pos)
        {
            while (pos < source.Length && source[pos] is ' ' or '\t' or '\r' or '\n') pos++;
        }
        private static bool Number(string source, ref int pos, out double value)
        {
            int start = pos;
            if (pos < source.Length && source[pos] is '+' or '-') pos++;
            bool digit = false;
            while (pos < source.Length && char.IsAsciiDigit(source[pos])) { pos++; digit = true; }
            if (pos < source.Length && source[pos] == '.')
            {
                pos++;
                while (pos < source.Length && char.IsAsciiDigit(source[pos])) { pos++; digit = true; }
            }
            if (!digit) { value = 0; return false; }
            if (pos < source.Length && source[pos] is 'e' or 'E')
            {
                pos++;
                if (pos < source.Length && source[pos] is '+' or '-') pos++;
                int exponent = pos;
                while (pos < source.Length && char.IsAsciiDigit(source[pos])) pos++;
                if (pos == exponent) { value = 0; return false; }
            }
            return double.TryParse(source.AsSpan(start, pos - start), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
        }
    }
}
