using System.Text;

namespace NecronomiDom.Dom
{
    internal static class HtmlText
    {
        internal static string CollapseWhitespace(string text)
        {
            var normalize = false;
            for (var i = 0; i < text.Length; i++)
                if (DOMTokenList.IsSpace(text[i]) && (text[i] != ' ' || i == 0 || i == text.Length - 1 || DOMTokenList.IsSpace(text[i - 1]))) { normalize = true; break; }
            if (!normalize) return text;
            var output = new StringBuilder(text.Length); var space = false;
            foreach (var c in text)
            {
                if (DOMTokenList.IsSpace(c)) { if (output.Length > 0) space = true; continue; }
                if (space) { output.Append(' '); space = false; }
                output.Append(c);
            }
            return output.ToString();
        }
    }
}
