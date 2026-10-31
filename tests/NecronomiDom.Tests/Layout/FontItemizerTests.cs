using System.Collections.Generic;
using System.Linq;
using NecronomiDom.Layout;
using NUnit.Framework;

namespace NecronomiDom.Tests.Layout;

[TestFixture]
public sealed class FontItemizerTests
{
    // Synthetic five-face chain: printable ASCII is in every face, the rest is listed per face.
    private static readonly string[] Coverage =
    {
        "Русский", // Latin/Cyrillic primary face
        "Русский", // second Latin/Cyrillic face
        "简体中文日本語", // Simplified Chinese face: no Hangul
        "体中文日本語", // Japanese face: no U+7B80, no Hangul
        "体中文日本語한국어", // Korean face: no U+7B80
    };
    private static bool Covered(int face, int scalar) => scalar is >= 0x20 and <= 0x7E || Coverage[face].EnumerateRunes().Any(r => r.Value == scalar);

    [TestCase("简体中文", 2)]
    [TestCase("日本語", 2)]
    [TestCase("한국어", 4)]
    [TestCase("Русский", 0)]
    public void TextSelectsTheFirstFaceMappingEveryScalar(string text, int face)
    {
        var items = new List<FontFaceRange>();
        Assert.That(FontItemizer.Itemize(text, Coverage.Length, Covered, items), Is.Zero);
        Assert.That(items, Is.EqualTo(new[] { new FontFaceRange(face, 0, text.Length) }));
    }

    [Test]
    public void MixedTextSplitsOnlyWhereTheSelectedFaceChanges()
    {
        var items = new List<FontFaceRange>();
        Assert.That(FontItemizer.Itemize("Language: 日本語 / 한국어", Coverage.Length, Covered, items), Is.Zero);
        Assert.That(items, Is.EqualTo(new[] { new FontFaceRange(0, 0, 10), new FontFaceRange(2, 10, 13), new FontFaceRange(0, 13, 16),
            new FontFaceRange(4, 16, 19) }));
    }

    [Test]
    public void LookupStopsAtTheFirstMappingFaceAndNeverQueriesControls()
    {
        var queried = new List<(int Face, int Scalar)>();
        var items = new List<FontFaceRange>();
        Assert.That(FontItemizer.Itemize("한\t국", Coverage.Length, (face, scalar) => { queried.Add((face, scalar)); return Covered(face, scalar); }, items), Is.Zero);
        Assert.That(items, Is.EqualTo(new[] { new FontFaceRange(4, 0, 3) }), "A C0 control joins the current face run.");
        Assert.That(queried, Is.EqualTo(new[] { (0, 0xD55C), (1, 0xD55C), (2, 0xD55C), (3, 0xD55C), (4, 0xD55C),
            (0, 0xAD6D), (1, 0xAD6D), (2, 0xAD6D), (3, 0xAD6D), (4, 0xAD6D) }));
        queried.Clear(); items.Clear();
        Assert.That(FontItemizer.Itemize("Ab", Coverage.Length, (face, scalar) => { queried.Add((face, scalar)); return Covered(face, scalar); }, items), Is.Zero);
        Assert.That(queried, Is.EqualTo(new[] { (0, (int)'A'), (0, (int)'b') }), "Covered primary text never loads a fallback face.");
    }

    [Test]
    public void SurrogatePairsUseUtf16RangesAndAbsentScalarsAreReported()
    {
        var items = new List<FontFaceRange>();
        Assert.That(FontItemizer.Itemize("A😀B", 2, (face, scalar) => face == 1 ? scalar == 0x1F600 : scalar < 0x7F, items), Is.Zero);
        Assert.That(items, Is.EqualTo(new[] { new FontFaceRange(0, 0, 1), new FontFaceRange(1, 1, 3), new FontFaceRange(0, 3, 4) }));
        Assert.That(FontItemizer.Itemize("A😀B", Coverage.Length, Covered, new List<FontFaceRange>()), Is.EqualTo(0x1F600));
        items.Clear();
        Assert.That(FontItemizer.Itemize("日~", Coverage.Length, (face, scalar) => scalar == '日' && face == 2, items), Is.Zero);
        Assert.That(items, Is.EqualTo(new[] { new FontFaceRange(2, 0, 2) }), "ASCII absent from the chain joins the current range.");
        items.Clear();
        Assert.That(FontItemizer.Itemize("", 1, (_, _) => throw new AssertionException("no lookup"), items), Is.Zero);
        Assert.That(items, Is.Empty);
    }
}
