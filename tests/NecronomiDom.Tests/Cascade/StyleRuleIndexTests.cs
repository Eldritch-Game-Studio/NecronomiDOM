using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NecronomiDom.Css.Cascade;
using NecronomiDom.Dom;
using NecronomiDom.Html;
using NecronomiDom.Url;
using NUnit.Framework;

namespace NecronomiDom.Tests.Cascade;

/// <summary>Rightmost-key rule index: identical cascade results to the linear walk, with counted selector tests.</summary>
[TestFixture]
public sealed class StyleRuleIndexTests
{
    private static readonly string[] Pseudos = { "", "::before", "::after", "::marker" };

    private static Document Load(string html)
        => HtmlImporter.Import(html, "rule-index", WebUrl.Parse("res:///rule-index.html")!).Document;
    private static List<Element> Elements(Document doc)
        => doc.ShadowIncludingInclusiveDescendants().OfType<Element>().ToList();
    /// <summary>Every computed longhand of every element and pseudo-element, in document order.</summary>
    private static List<string> Snapshot(Document doc, StyleEngine styles)
    {
        var lines = new List<string>();
        foreach (var element in Elements(doc))
            foreach (string pseudo in Pseudos)
            {
                var style = styles.GetComputedStyle(element, pseudo);
                var line = new StringBuilder(element.LocalName).Append('#').Append(element.Id).Append(pseudo).Append(" {");
                for (int i = 0; i < style.Length; i++)
                    line.Append(style.Item(i)).Append(':').Append(style.GetPropertyValue(style.Item(i))).Append(';');
                lines.Add(line.Append('}').ToString());
            }
        return lines;
    }
    private static void AssertSameCascade(Document doc, string because)
    {
        var indexed = new StyleEngine(doc);
        var linear = new StyleEngine(doc) { IndexedRuleMatching = false };
        AssertSameCascade(doc, indexed, linear, because);
    }
    private static void AssertSameCascade(Document doc, StyleEngine indexed, StyleEngine linear, string because)
    {
        long indexedTests = indexed.RuleTestCount, linearTests = linear.RuleTestCount;
        var expected = Snapshot(doc, linear);
        var actual = Snapshot(doc, indexed);
        for (int i = 0; i < expected.Count; i++) Assert.That(actual[i], Is.EqualTo(expected[i]), because);
        Assert.That(actual, Has.Count.EqualTo(expected.Count), because);
        Assert.That(indexed.RuleTestCount - indexedTests, Is.LessThanOrEqualTo(linear.RuleTestCount - linearTests), because);
    }

    public static IEnumerable<TestCaseData> Cases()
    {
        const string body = "<div id=x class='a b' data-k=1 title=t><p id=p1 class=a>One</p><p id=p2 class='b c'>Two</p>" +
            "<ul id=list><li id=l1 class=a>1</li><li id=l2>2</li><li id=l3 class=b>3</li></ul><h1 id=h>H</h1><p id=p3>Three</p>" +
            "<span id=s class=A data-v=yes>S</span><section id=card class=card><div id=content class=content><p id=deep class=x>D</p></div>" +
            "<p id=shallow class=a>Sh</p></section></div><div id=y class='x a'><span id=empty></span></div>";
        yield return Case("specificity across buckets", "div{color:red}.a{color:blue}#x{color:green}[data-k]{color:yellow}*{color:black}" +
            "p.a{color:purple}.a.b{background-color:red}div.b{background-color:blue}#p2.b{color:teal}span[data-v]{color:navy}" +
            "p{width:1px}p{width:2px}.c{width:3px}.b{width:4px}", body);
        yield return Case("source order ties between buckets", ".a{color:red}p{color:blue}#p1{width:1px}.a{width:2px}" +
            "[class]{color:green}p.a{margin-left:1px}.a p{margin-left:2px}*.a{margin-left:3px}", body);
        yield return Case("importance and layers", "@layer base, top;@layer top{.a{color:red}p{width:1px !important}}" +
            "@layer base{.a{color:blue !important}p{width:2px}}div{color:green}#p1{color:yellow !important}" +
            "@layer{li{color:orange}}@layer top.inner{.b{color:silver}}", body);
        yield return Case("conditional groups", "@media (min-width:1px){.a{color:red}}@media (max-width:1px){.a{color:blue}}" +
            "@supports (display:grid){#x{color:blue}p{width:3px}}@supports not (display:grid){p{width:4px}}" +
            "@media screen{@layer m{.b{color:green}}}", body);
        yield return Case("scoping", "@scope (.card) to (.content){p{color:red}:scope{color:blue}.x{width:1px}}" +
            ".card p{color:green}@scope (.card){.a{background-color:red}}@scope{p{margin-left:2px}}", body);
        yield return Case("nesting", ".card{color:red;&.x{color:blue}.x &{color:green}>p{color:purple}p{width:1px}" +
            "@media (min-width:1px){.content{color:teal}}}#x{&>p{margin-left:1px}}", body);
        yield return Case("logical pseudo-classes", ":is(.a,#x) span{color:red}:where(div) .b{color:blue}p:not(.a){color:green}" +
            "li:nth-child(2n){color:red}li:first-child{color:blue}:is(p){width:1px}:where(.c){width:2px}:not(div):empty{width:3px}" +
            ":root{color:gray}li:nth-child(odd of .a, .b){margin-left:1px}div:has(> h1){background-color:red}", body);
        yield return Case("pseudo-elements", ".a::before{content:'x';color:red}p::after{content:'y'}li::marker{color:red}" +
            "::before{color:green}#p1::before{width:1px}[data-k]::after{content:'z'}*::marker{width:2px}", body);
        yield return Case("combinators", "div>p{color:red}div p{width:1px}h1+p{color:blue}h1~p{width:2px}ul li{color:green}" +
            "#x .content>.x{color:navy}.a+.b{margin-left:1px}", body);
        yield return Case("case-insensitive HTML names", "DIV{color:red}P.a{width:1px}[DATA-K]{width:2px}SPAN.A{color:blue}" +
            "[data-v=YES i]{margin-left:1px}.a{width:3px}", body);
        yield return Case("custom property registration", "@property --len{syntax:'<length>';inherits:false;initial-value:3px}" +
            ".a{--len:5px;width:var(--len)}p{width:var(--len)}#x{--len:9px}", body);
        yield return Case("quirks-mode class and id", ".A{color:red}#X{width:1px}.a{width:2px}span.a{color:blue}", body, quirks: true);
        yield return Case("foreign elements keep case", "svg lineargradient{color:red}linearGradient{width:1px}rect{color:blue}" +
            "svg|rect{width:2px}*|rect{margin-left:1px}foreignObject p{color:green}",
            "<svg id=svg><linearGradient id=g></linearGradient><rect id=r class=a></rect><foreignObject id=fo><p id=fp>F</p></foreignObject></svg>");
    }
    private static TestCaseData Case(string name, string css, string body, bool quirks = false)
        => new TestCaseData((quirks ? "" : "<!doctype html>") + "<style>" + css + "</style><body>" + body + "</body>").SetName("Indexed cascade matches linear: " + name);

    [TestCaseSource(nameof(Cases))]
    public void IndexedCascadeMatchesLinearWalk(string html) => AssertSameCascade(Load(html), html);

    [Test]
    public void ShadowTreesPartsSlotsAndHostsCascadeIdentically()
    {
        var doc = Load("<!doctype html><style>x-card::part(label){color:red}x-card{color:blue}.slotted{width:1px}" +
            "#host::part(label){width:2px}</style><body><x-card id=host><span id=assigned class=slotted slot=s>A</span></x-card></body>");
        var host = doc.GetElementById("host")!;
        var shadow = host.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
        var style = doc.CreateElement("style");
        style.TextContent = ":host{margin-left:1px}:host(.on){margin-left:2px}::slotted(span){color:green}::slotted(.slotted){width:3px}" +
            "slot::slotted(*){background-color:red}.label{color:purple}p{width:4px}";
        shadow.AppendChild(style);
        var label = doc.CreateElement("p"); label.Id = "label"; label.ClassName = "label"; label.SetAttribute("part", "label");
        shadow.AppendChild(label);
        var slot = doc.CreateElement("slot"); slot.SetAttribute("name", "s"); shadow.AppendChild(slot);
        host.ClassName = "on";
        AssertSameCascade(doc, "shadow scopes");
    }

    [Test]
    public void StylesheetMutationsRebuildTheIndex()
    {
        var doc = Load("<!doctype html><style>.a{color:red}</style><body><p id=p class='a b'>P</p><p id=q>Q</p></body>");
        var indexed = new StyleEngine(doc);
        var linear = new StyleEngine(doc) { IndexedRuleMatching = false };
        AssertSameCascade(doc, indexed, linear, "initial");
        var sheet = doc.StyleSheets[0]!;
        sheet.InsertRule(".b{color:blue}", 1);
        AssertSameCascade(doc, indexed, linear, "inserted rule");
        ((Css.Om.CSSStyleRule)sheet.CssRules[0]).SelectorText = "#q";
        AssertSameCascade(doc, indexed, linear, "changed selector");
        ((Css.Om.CSSStyleRule)sheet.CssRules[1]).Style.SetProperty("width", "5px");
        AssertSameCascade(doc, indexed, linear, "declaration edit");
        sheet.DeleteRule(1);
        AssertSameCascade(doc, indexed, linear, "deleted rule");
        doc.GetElementById("p")!.ClassName = "c";
        sheet.InsertRule(".c{color:green}", 0);
        AssertSameCascade(doc, indexed, linear, "class and rule change");
        // A leaf rule filed only under .c gains a nested rule that matches #q: it must become unkeyed.
        ((Css.Om.CSSStyleRule)sheet.CssRules[0]).InsertRule("& ~ p{width:7px}");
        AssertSameCascade(doc, indexed, linear, "leaf rule gained nested rules");
        ((Css.Om.CSSStyleRule)sheet.CssRules[0]).DeleteRule(0);
        AssertSameCascade(doc, indexed, linear, "nested rules removed again");
        sheet.InsertRule("@media (min-width:1px){.b{color:purple}}", 0);
        var media = (Css.Om.CSSMediaRule)sheet.CssRules[0];
        AssertSameCascade(doc, indexed, linear, "conditional group");
        media.InsertRule("#p{width:9px}", 1);
        AssertSameCascade(doc, indexed, linear, "rule inserted into a conditional group");
        ((Css.Om.CSSStyleRule)media.CssRules[1]).SelectorText = "p";
        AssertSameCascade(doc, indexed, linear, "selector changed inside a conditional group");
        media.DeleteRule(0);
        AssertSameCascade(doc, indexed, linear, "rule deleted from a conditional group");
    }

    // Randomized selectors and documents: a small vocabulary makes rules collide with elements often.
    private static readonly string[] Tags = { "div", "p", "span", "li", "ul", "section", "button", "a" };
    private static readonly string[] Classes = { "a", "b", "c", "d", "e", "A" };
    private static readonly string[] Ids = { "x", "y", "z" };
    private static readonly string[] Attributes = { "data-k", "title", "data-v" };
    private static readonly string[] PseudoClasses = { ":first-child", ":last-child", ":not(.a)", ":is(.b, #x)", ":where(p)",
        ":nth-child(2n+1)", ":empty", ":root", ":hover", ":not(:first-child)", ":is(div, span)" };
    private static readonly string[] Properties = { "color", "background-color", "width", "margin-left" };
    private static readonly string[] Values = { "red", "blue", "green", "1px", "2px", "3px", "inherit", "initial" };

    private static string Compound(Random random, bool subject)
    {
        var text = new StringBuilder();
        int shape = random.Next(10);
        if (shape < 4) text.Append(Tags[random.Next(Tags.Length)]);
        else if (shape == 4) text.Append('*');
        for (int i = random.Next(3); i > 0; i--) text.Append('.').Append(Classes[random.Next(Classes.Length)]);
        if (random.Next(6) == 0) text.Append('#').Append(Ids[random.Next(Ids.Length)]);
        if (random.Next(4) == 0)
        {
            string name = Attributes[random.Next(Attributes.Length)];
            text.Append('[').Append(random.Next(2) == 0 ? name.ToUpperInvariant() : name)
                .Append(random.Next(3) switch { 0 => "", 1 => "=v", _ => "^=v" }).Append(']');
        }
        if (random.Next(4) == 0) text.Append(PseudoClasses[random.Next(PseudoClasses.Length)]);
        if (text.Length == 0) text.Append('*');
        if (subject && random.Next(6) == 0) text.Append(random.Next(2) == 0 ? "::before" : "::after");
        return text.ToString();
    }
    private static string Selector(Random random)
    {
        var text = new StringBuilder();
        int compounds = 1 + random.Next(3);
        for (int i = 0; i < compounds; i++)
        {
            if (i > 0) text.Append(random.Next(4) switch { 0 => " ", 1 => " > ", 2 => " + ", _ => " ~ " });
            text.Append(Compound(random, i == compounds - 1));
        }
        return text.ToString();
    }
    private static string Rule(Random random, int depth = 0)
    {
        var selectors = string.Join(", ", Enumerable.Range(0, 1 + random.Next(3)).Select(_ => Selector(random)));
        var declarations = new StringBuilder();
        for (int i = 1 + random.Next(3); i > 0; i--)
            declarations.Append(Properties[random.Next(Properties.Length)]).Append(':').Append(Values[random.Next(Values.Length)])
                .Append(random.Next(8) == 0 ? " !important;" : ";");
        string rule = selectors + "{" + declarations + (depth == 0 && random.Next(8) == 0 ? "& " + Compound(random, false) + "{color:teal}" : "") + "}";
        return depth > 1 ? rule : random.Next(12) switch
        {
            0 => "@media (min-width:1px){" + rule + Rule(random, depth + 1) + "}",
            1 => "@media (max-width:1px){" + rule + "}",
            2 => "@layer l" + random.Next(3) + "{" + rule + Rule(random, depth + 1) + "}",
            3 => "@supports (display:grid){" + rule + "}",
            4 => "@scope (." + Classes[random.Next(Classes.Length)] + "){" + rule + "}",
            _ => rule,
        };
    }
    private static void Tree(Random random, Document doc, Element parent, int depth, ref int budget)
    {
        int children = depth > 5 ? 0 : random.Next(5);
        for (int i = 0; i < children && budget > 0; i++, budget--)
        {
            var element = doc.CreateElement(Tags[random.Next(Tags.Length)]);
            Decorate(random, element);
            parent.AppendChild(element);
            Tree(random, doc, element, depth + 1, ref budget);
        }
    }
    private static void Decorate(Random random, Element element)
    {
        element.ClassName = string.Join(random.Next(2) == 0 ? " " : "\t", Enumerable.Range(0, random.Next(4)).Select(_ => Classes[random.Next(Classes.Length)]));
        if (random.Next(5) == 0) element.Id = Ids[random.Next(Ids.Length)];
        foreach (string attribute in Attributes) if (random.Next(4) == 0) element.SetAttribute(attribute, random.Next(2) == 0 ? "v" : "vx");
    }
    private static Document RandomDocument(Random random)
    {
        string css = string.Concat(Enumerable.Range(0, 30 + random.Next(30)).Select(_ => Rule(random)));
        var doc = Load("<!doctype html><style>" + css + "</style><body></body>");
        int budget = 60;
        Tree(random, doc, doc.Body!, 0, ref budget);
        return doc;
    }

    [Test]
    public void RandomSelectorsAndDocumentsCascadeIdentically([Range(0, 79)] int seed)
    {
        var random = new Random(seed);
        var doc = RandomDocument(random);
        var indexed = new StyleEngine(doc);
        var linear = new StyleEngine(doc) { IndexedRuleMatching = false };
        AssertSameCascade(doc, indexed, linear, "seed " + seed);
        var elements = Elements(doc).Where(e => e.ParentElement != null && e.LocalName != "style" && e != doc.Body).ToList();
        for (int i = 0; i < 12 && elements.Count > 0; i++)
        {
            var element = elements[random.Next(elements.Count)];
            switch (random.Next(4))
            {
                case 0: Decorate(random, element); break;
                case 1: element.PseudoState ^= ElementPseudoState.Hover; break;
                case 2: if (element is HTMLElement html) html.Style.SetProperty("color", Values[random.Next(3)]); break;
                default:
                    var added = doc.CreateElement(Tags[random.Next(Tags.Length)]);
                    Decorate(random, added);
                    element.AppendChild(added);
                    break;
            }
        }
        AssertSameCascade(doc, indexed, linear, "seed " + seed + " after mutations");
    }

    /// <summary>A CraftingStation-like document: theme and station rules over ~4,300 elements, styled once.</summary>
    private static Document Station(out int ruleCount)
    {
        var css = new StringBuilder(":root{--kk-gold:#c9a227}*{box-sizing:border-box}body{margin:0}button{font:inherit}.hidden{display:none}");
        var random = new Random(7);
        string[] blocks = { "station", "ingredient", "kk-chip", "kk-glyph", "kk-pip", "recipe", "panel", "combo", "unit", "detail" };
        string[] parts = { "", "-row", "-label", "-name", "-count", "-header", "-title", "-grid", "-scroll", "-value", "-on", "-s" };
        foreach (string block in blocks)
            foreach (string part in parts)
            {
                css.Append('.').Append(block).Append(part).Append("{color:#").Append(random.Next(0x1000000).ToString("x6")).Append('}');
                css.Append(".station-compact .").Append(block).Append(part).Append("{width:").Append(random.Next(1, 90)).Append("px}");
            }
        css.Append("@media (max-height:680px){.station-root .ingredient-name{display:none}.kk-chip{margin-left:1px}}");
        css.Append("#station-close,#station-journal{color:red}button:hover{color:blue}p{margin:0}[aria-hidden]{width:0}");
        ruleCount = blocks.Length * parts.Length * 2 + 2 + 7;
        var doc = Load("<!doctype html><style>" + css + "</style><body><div id=root class='station-root station-compact'>" +
            "<div id=player-grid class=station-grid></div><div id=chest-grid class=station-grid></div>" +
            "<div id=detail class=detail-scroll><div id=effect class=combo-value></div></div>" +
            "<button id=station-close>x</button><button id=station-journal>j</button></div></body>");
        foreach (string grid in new[] { "player-grid", "chest-grid" })
            for (int i = 0; i < 28; i++)
            {
                var row = doc.CreateElement("button"); row.ClassName = "station-slot ingredient-row";
                var label = doc.CreateElement("div"); label.ClassName = "ingredient-label"; row.AppendChild(label);
                foreach (string name in new[] { "ingredient-name", "ingredient-count" })
                { var text = doc.CreateElement("p"); text.ClassName = name; text.TextContent = name; label.AppendChild(text); }
                var traits = doc.CreateElement("div"); traits.ClassName = "kk-pips"; label.AppendChild(traits);
                for (int p = 0; p < 7; p++) { var pip = doc.CreateElement("div"); pip.ClassName = p == 0 ? "kk-pip kk-pip-on" : "kk-pip"; traits.AppendChild(pip); }
                doc.GetElementById(grid)!.AppendChild(row);
            }
        var effect = doc.GetElementById("effect")!;
        for (int i = 0; i < 1200; i++)
        {
            var chip = doc.CreateElement("div"); chip.ClassName = "kk-chip";
            var glyph = doc.CreateElement("div"); glyph.ClassName = "kk-glyph kk-glyph-s"; chip.AppendChild(glyph);
            var value = doc.CreateElement("p"); value.ClassName = "kk-chip-value"; value.TextContent = "+33%"; chip.AppendChild(value);
            effect.AppendChild(chip);
        }
        return doc;
    }

    [Test]
    public void StationStyleTestsOnlyCandidateRulesPerElement()
    {
        var doc = Station(out int authorRules);
        var elements = Elements(doc);
        Assert.That(elements, Has.Count.InRange(4250, 4350));
        var indexed = new StyleEngine(doc);
        foreach (var element in elements) indexed.GetComputedStyle(element).GetPropertyValue("color");
        // The linear reference styles a sample (and, through inheritance, its ancestors) to stay cheap.
        var linear = new StyleEngine(doc) { IndexedRuleMatching = false };
        var sample = elements.Where((_, i) => i % 25 == 0).ToList();
        foreach (var element in sample) linear.GetComputedStyle(element).GetPropertyValue("color");
        double indexedPerEntry = indexed.RuleTestCount / (double)indexed.ComputedEntryCount;
        double linearPerEntry = linear.RuleTestCount / (double)linear.ComputedEntryCount;
        TestContext.Out.WriteLine($"{elements.Count} elements: indexed {indexed.RuleTestCount} rule tests over {indexed.ComputedEntryCount} " +
            $"styles ({indexedPerEntry:F1} each); linear {linear.RuleTestCount} over {linear.ComputedEntryCount} ({linearPerEntry:F1} each)");
        Assert.That(linearPerEntry, Is.GreaterThan(authorRules), "the linear walk tests every author and user-agent rule");
        // Each element visits its own class/id/attribute/tag buckets plus the few unkeyed rules, not the whole sheet.
        Assert.That(indexedPerEntry, Is.LessThan(15));
        foreach (var element in sample)
        {
            var left = indexed.GetComputedStyle(element);
            var right = linear.GetComputedStyle(element);
            Assert.That(left.Length, Is.EqualTo(right.Length));
            for (int i = 0; i < right.Length; i++)
                Assert.That(left.GetPropertyValue(right.Item(i)), Is.EqualTo(right.GetPropertyValue(right.Item(i))), right.Item(i));
        }
    }
}
