using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NecronomiDom.Css.Cascade;
using NecronomiDom.Css.Om;
using NecronomiDom.Dom;
using NecronomiDom.Html;
using NecronomiDom.Url;
using NUnit.Framework;

namespace NecronomiDom.Tests.Cascade;

/// <summary>Class changes whose tokens only key subject compounds keep descendants' rules, and lazily refreshed
/// computed views stay identical to a fresh cascade.</summary>
[TestFixture]
public sealed class StyleClassInvalidationTests
{
    private const string Green = "rgb(0, 128, 0)";

    private static Document Load(string css, string body) => HtmlImporter.Import(
        "<!doctype html><style>" + css + "</style><body>" + body + "</body>", "class-invalidation",
        WebUrl.Parse("res:///class-invalidation.html")!).Document;
    private static StyleEngine Styled(Document doc)
    {
        var styles = new StyleEngine(doc);
        StyleAll(doc, styles);
        return styles;
    }
    private static void StyleAll(Document doc, StyleEngine styles)
    {
        foreach (Element element in doc.QuerySelectorAll("*")) styles.GetComputedStyle(element).GetPropertyValue("color");
    }
    private static (long Restyled, long ReusedRules) Read(Document doc, StyleEngine styles)
    {
        long entries = styles.ComputedEntryCount, reused = styles.ReusedRuleCollectionCount;
        StyleAll(doc, styles);
        return (styles.ComputedEntryCount - entries, styles.ReusedRuleCollectionCount - reused);
    }
    private static string Color(StyleEngine styles, Document doc, string id)
        => styles.GetComputedStyle(doc.GetElementById(id)!).GetPropertyValue("color");

    private const string Tree = "<div id=a><p id=a1><span id=a2>x</span></p></div><div id=b><span id=b1>y</span></div>";

    [Test]
    public void SubjectOnlyClassChangeKeepsDescendantRules()
    {
        var doc = Load(".on { color: green } .x { width: 1px }", Tree);
        var styles = Styled(doc);
        doc.GetElementById("a")!.ClassName = "on";
        var (restyled, reused) = Read(doc, styles);
        Assert.That(restyled, Is.EqualTo(3), "the element and its descendants inherit again");
        Assert.That(reused, Is.EqualTo(2), "only the changed element matches its rules again");
        Assert.That(Color(styles, doc, "a2"), Is.EqualTo(Green));
        Assert.That(Color(styles, doc, "b1"), Is.Not.EqualTo(Green));
    }

    [TestCase(".on .x { color: green }", "a2")]
    [TestCase(".on > p { color: green }", "a1")]
    [TestCase(":is(.on span) { color: green }", "a2")]
    [TestCase(".on { & span { color: green } }", "a2")]
    [TestCase("@scope (.on) { span { color: green } }", "a2")]
    [TestCase("[class~=on] span { color: green }", "a2")]
    public void ClassesTestedOnOtherElementsRematchTheSubtree(string css, string target)
    {
        var doc = Load(css, Tree.Replace("<span id=a2>", "<span id=a2 class=x>"));
        var styles = Styled(doc);
        doc.GetElementById("a")!.ClassName = "on";
        var (_, reused) = Read(doc, styles);
        Assert.That(reused, Is.Zero);
        Assert.That(Color(styles, doc, target), Is.EqualTo(Green));
        doc.GetElementById("a")!.ClassName = "";
        Assert.That(Color(styles, doc, target), Is.Not.EqualTo(Green));
    }

    [Test]
    public void SiblingClassChangeStillRestylesSiblings()
    {
        var doc = Load(".on + div { color: green }", Tree);
        var styles = Styled(doc);
        doc.GetElementById("a")!.ClassName = "on";
        Assert.That(Color(styles, doc, "b"), Is.EqualTo(Green));
        Assert.That(Color(styles, doc, "b1"), Is.EqualTo(Green));
    }

    [Test]
    public void ClassChangeRevertedBeforeTheReadRestylesNothing()
    {
        var doc = Load(".on .x { color: green } .on { color: green }", Tree);
        var styles = Styled(doc);
        var a = doc.GetElementById("a")!;
        a.ClassList.Toggle("on");
        a.ClassList.Toggle("on");
        Assert.That(Read(doc, styles).Restyled, Is.Zero);
        a.ClassName = "on";
        Assert.That(Color(styles, doc, "a"), Is.EqualTo(Green));
    }

    [Test]
    public void HeldViewsFollowValueAndNameSetChanges()
    {
        var doc = Load(".on { color: green; --extra: 1px } .wide { width: 10px }", Tree);
        var styles = Styled(doc);
        var a = doc.GetElementById("a")!;
        var resolved = styles.GetComputedStyle(a);
        var computed = styles.GetComputedValues(a);
        int length = resolved.Length, version = resolved.Version;
        a.ClassName = "wide";
        Assert.That(resolved.GetPropertyValue("width"), Is.EqualTo("10px"));
        Assert.That(computed.GetPropertyValue("width"), Is.EqualTo("10px"));
        Assert.That(resolved.Version, Is.Not.EqualTo(version), "a changed value advances the consumer version");
        a.ClassName = "on";
        Assert.That(resolved.Length, Is.EqualTo(length + 1));
        Assert.That(resolved.GetPropertyValue("--extra"), Is.EqualTo("1px"));
        Assert.That(computed.Declarations.Select(d => d.Name), Does.Contain("--extra"));
        AssertSameAsFresh(doc, styles, "after name set change");
    }

    // Randomized: shared class tokens appear in subject and non-subject positions, nesting contexts, selector arguments,
    // scopes and sibling combinators; s and t key subject compounds only, so their changes keep descendants' rules.
    // Class changes also change custom property sets, display and blockification.
    private static readonly string[] Tags = { "div", "p", "span", "section" };
    private static readonly string[] Classes = { "a", "b", "c", "d" };
    private static readonly string[] SubjectClasses = { "s", "t" };
    private static readonly string[] Tokens = Classes.Concat(SubjectClasses).ToArray();
    private static readonly string[] Declarations = { "color:red", "color:blue", "width:1px", "width:2px", "--v:green",
        "--w:3px", "color:var(--v,teal)", "margin-left:var(--w,4px)", "display:flex", "display:none", "color:inherit" };

    private static string Class(Random random) => "." + Classes[random.Next(Classes.Length)];
    private static string Compound(Random random, bool subject)
    {
        var text = new StringBuilder(random.Next(3) == 0 ? Tags[random.Next(Tags.Length)] : "");
        for (int i = random.Next(2); i >= 0; i--)
            text.Append(subject && random.Next(2) == 0 ? "." + SubjectClasses[random.Next(SubjectClasses.Length)] : Class(random));
        return random.Next(8) switch
        {
            0 => text + ":not(" + Class(random) + ")",
            1 => text + ":is(" + Class(random) + " " + Class(random) + ")",
            2 => text + ":nth-child(odd of " + Class(random) + ")",
            _ => text.ToString(),
        };
    }
    private static string Selector(Random random, bool subjectClasses)
    {
        var text = new StringBuilder();
        for (int i = random.Next(3); i > 0; i--)
            text.Append(Compound(random, false)).Append(random.Next(4) switch { 0 => " ", 1 => " > ", 2 => " + ", _ => " ~ " });
        text.Append(Compound(random, subjectClasses));
        if (random.Next(6) == 0) text.Append("::before");
        return text.ToString();
    }
    private static string Body(Random random)
    {
        var text = new StringBuilder();
        for (int i = 1 + random.Next(2); i > 0; i--) text.Append(Declarations[random.Next(Declarations.Length)]).Append(';');
        if (random.Next(5) == 0) text.Append("content:'x';");
        return text.ToString();
    }
    private static string Rule(Random random, bool classAttributes)
    {
        // A rule with nested rules is their context, so its own selector may not use the subject-only tokens.
        int nesting = random.Next(6);
        string rule = Selector(random, subjectClasses: nesting > 2) + "{" + Body(random) + nesting switch
        {
            0 => "& " + Compound(random, true) + "{" + Body(random) + "}",
            1 => "&." + Tokens[random.Next(Tokens.Length)] + "{" + Body(random) + "}",
            2 => Compound(random, false) + " &{" + Body(random) + "}",
            _ => "",
        } + "}";
        return random.Next(10) switch
        {
            0 => "@scope (" + Class(random) + ") to (" + Class(random) + "){" + rule + "}",
            1 when classAttributes => "[class~=" + Tokens[random.Next(Tokens.Length)] + "] " + rule,
            _ => rule,
        };
    }
    private static void Grow(Random random, Document doc, Element parent, int depth, ref int budget)
    {
        for (int i = depth > 4 ? 0 : random.Next(4); i > 0 && budget > 0; i--, budget--)
        {
            var element = doc.CreateElement(Tags[random.Next(Tags.Length)]);
            element.ClassName = RandomClasses(random);
            parent.AppendChild(element);
            Grow(random, doc, element, depth + 1, ref budget);
        }
    }
    private static string RandomClasses(Random random)
        => string.Join(" ", Enumerable.Range(0, random.Next(4)).Select(_ => Tokens[random.Next(Tokens.Length)]));

    /// <summary>Every longhand of both computed views, for every element and its ::before, in CSSOM order.</summary>
    private static List<string> Snapshot(Document doc, StyleEngine styles)
    {
        var lines = new List<string>();
        foreach (Element element in doc.QuerySelectorAll("*"))
        {
            foreach (string pseudo in new[] { "", "::before" })
                lines.Add(Line(element, pseudo, styles.GetComputedStyle(element, pseudo)));
            lines.Add(Line(element, "(computed)", styles.GetComputedValues(element)));
        }
        return lines;
    }
    private static string Line(Element element, string label, CSSStyleDeclaration style)
    {
        var line = new StringBuilder(element.LocalName).Append('.').Append(element.ClassName).Append(label).Append(" {");
        for (int i = 0; i < style.Length; i++)
            line.Append(style.Item(i)).Append(':').Append(style.GetPropertyValue(style.Item(i))).Append(';');
        return line.Append('}').ToString();
    }
    private static void AssertSameAsFresh(Document doc, StyleEngine styles, string because)
    {
        var expected = Snapshot(doc, new StyleEngine(doc));
        var actual = Snapshot(doc, styles);
        Assert.That(actual, Has.Count.EqualTo(expected.Count), because);
        for (int i = 0; i < expected.Count; i++) Assert.That(actual[i], Is.EqualTo(expected[i]), because);
    }

    [Test]
    public void RandomClassMutationsMatchAFreshCascade([Range(0, 29)] int seed)
    {
        var random = new Random(seed);
        string css = string.Concat(Enumerable.Range(0, 25 + random.Next(20)).Select(_ => Rule(random, seed % 3 == 0)));
        var doc = Load(css, "");
        int budget = 45;
        Grow(random, doc, doc.Body!, 0, ref budget);
        var styles = new StyleEngine(doc);
        AssertSameAsFresh(doc, styles, "seed " + seed);
        var elements = doc.Body!.QuerySelectorAll("*").Cast<Element>().ToList();
        for (int round = 0; round < 5 && elements.Count > 0; round++)
        {
            for (int i = 1 + random.Next(4); i > 0; i--)
            {
                var element = elements[random.Next(elements.Count)];
                string token = Tokens[random.Next(Tokens.Length)];
                switch (random.Next(6))
                {
                    case 0: element.ClassName = RandomClasses(random); break;
                    case 1: element.ClassList.Toggle(token); break;
                    case 2: element.ClassList.Toggle(token); element.ClassList.Toggle(token); break;
                    case 3: element.ClassList.Add(token); break;
                    case 4: element.ClassList.Remove(token); break;
                    // An inline change below or above a class change: either may keep rules, neither may hide the other.
                    default: ((HTMLElement)element).Style.SetProperty("color", random.Next(2) == 0 ? "purple" : ""); break;
                }
            }
            AssertSameAsFresh(doc, styles, "seed " + seed + " round " + round);
        }
    }
}
