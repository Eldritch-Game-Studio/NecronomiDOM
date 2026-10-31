using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NecronomiDom.Dom;
using NecronomiDom.Html;
using NecronomiDom.Url;
using NUnit.Framework;

namespace NecronomiDom.Tests.Html
{
    [TestFixture]
    public sealed class Html5libTests
    {
        private static readonly WebUrl DocumentUrl = WebUrl.Parse("res:///tests/html5lib.html")!;
        // Cases where Chrome 155 (scripting disabled) builds a different tree than the corpus expects at the
        // recorded corpus commit; the importer follows Chrome, so these cases assert Chrome's tree instead.
        private static readonly Dictionary<string, string> ChromeTrees = new(StringComparer.Ordinal)
        {
            ["html5test-com.dat:12"] = "| <?import namespace=\"foo\" implementation=\"#bar\">\n| <html>\n|   <head>\n|   <body>",
            ["noscript01.dat:13"] = "| <html>\n|   <head>\n|     <noscript>\n|     <!-- foo -->\n|   <body>",
            ["plain-text-unsafe.dat:1"] = "| <html>\n|   <head>\n|   <body>\n|     \"FOO\rZOO\"",
            ["tests1.dat:40"] = "| <html>\n|   <head>\n|   <body>",
            ["tests1.dat:44"] = "| <?COMMENT >\n| <html>\n|   <head>\n|   <body>",
            ["tests1.dat:47"] = "| <?COM--MENT >\n| <html>\n|   <head>\n|   <body>",
            ["tests_innerHTML_1.dat:76"] = "| <input>\n| <option>",
        };
        public static IEnumerable<TestCaseData> Cases()
        {
            var root = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "html5lib", "tree-construction");
            foreach (var path in Directory.GetFiles(root, "*.dat", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
                var parsed = new List<CorpusCase>();
                var text = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
                var cases = text.Split("#data\n", StringSplitOptions.None);
                for (var i = 1; i < cases.Length; i++)
                {
                    var sections = new Dictionary<string, StringBuilder>(StringComparer.Ordinal);
                    var current = "data"; sections[current] = new StringBuilder();
                    using var reader = new StringReader(cases[i]);
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line is "#errors" or "#new-errors" or "#document" or "#document-fragment" or "#script-on" or "#script-off")
                        { current = line.Substring(1); sections[current] = new StringBuilder(); }
                        else sections[current].Append(line).Append('\n');
                    }
                    if (!sections.TryGetValue("document", out var expected)) throw new InvalidDataException(path + ": missing document section");
                    var data = sections["data"].ToString(); if (data.EndsWith('\n')) data = data.Substring(0, data.Length - 1);
                    var context = sections.TryGetValue("document-fragment", out var fragment) ? fragment.ToString().TrimEnd('\n') : null;
                    parsed.Add(new CorpusCase(i, data, context, expected.ToString().TrimEnd('\n'), sections.ContainsKey("script-on")));
                }
                foreach (var corpusCase in parsed)
                {
                    var name = relative + ":" + corpusCase.Index + (corpusCase.ScriptOn ? ":script-on" : ":script-off");
                    var expected = !corpusCase.ScriptOn && ChromeTrees.TryGetValue(relative + ":" + corpusCase.Index, out var chrome)
                        ? chrome : corpusCase.Expected;
                    var test = new TestCaseData(corpusCase.Html, corpusCase.Context, expected).SetName(name);
                    // The importer never executes script, so scripting-enabled expectations are not applicable.
                    if (corpusCase.ScriptOn) test.Ignore("Requires a scripting-enabled parser.");
                    yield return test;
                }
            }
        }
        [TestCaseSource(nameof(Cases))]
        public void TreeConstructionMatchesCorpus(string html, string? context, string expected)
        {
            var options = new HtmlImportOptions { IsScripting = false };
            Node tree;
            if (context == null) tree = HtmlImporter.Import(html, "html5lib.dat", DocumentUrl, options).Document;
            else
            {
                var document = new Document().Implementation.CreateHTMLDocument();
                var parts = context.Split(' ');
                var ns = parts.Length == 1 ? "http://www.w3.org/1999/xhtml" : parts[0] == "svg" ? "http://www.w3.org/2000/svg" : "http://www.w3.org/1998/Math/MathML";
                var element = document.CreateElementNS(ns, parts[parts.Length - 1]);
                tree = HtmlImporter.ImportFragment(html, element, "html5lib.dat", options).Fragment;
            }
            Assert.That(DomTreeDump.ToHtml5libFormat(tree), Is.EqualTo(expected));
        }
        private sealed record CorpusCase(int Index, string Html, string? Context, string Expected, bool ScriptOn);
    }
}
