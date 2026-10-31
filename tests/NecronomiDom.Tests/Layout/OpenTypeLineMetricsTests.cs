using System;
using System.Buffers.Binary;
using System.IO;
using NecronomiDom.Layout;
using NUnit.Framework;

namespace NecronomiDom.Tests.Layout;

[TestFixture]
public sealed class OpenTypeLineMetricsTests
{
    // Linux Chrome 155 CSS struts for a font with these raw metrics (Noto Sans: 1000 units, 1069/293).
    [TestCase(1, 0, 1)]
    [TestCase(2, 2, 1)]
    [TestCase(3, 3, 1)]
    [TestCase(4, 3, 2)]
    [TestCase(16, 17, 5)]
    [TestCase(20, 21, 6)]
    [TestCase(22, 23, 7)]
    [TestCase(24, 25, 8)]
    [TestCase(32, 33, 10)]
    [TestCase(40, 43, 12)]
    public void QuantizesRawTypographicMetricsUsingTheGenericBrowserPolicy(int size, double ascent, double descent)
    {
        var metrics = new OpenTypeLineMetrics(1000, 1069, 293, 0).Resolve(size);
        Assert.That(metrics, Is.EqualTo(new FontLineMetrics(ascent, descent, 0)));
    }
    [Test]
    public void UsesTypographicOS2MetricsOnlyWhenTheFontRequestsThem()
    {
        byte[] data = Font();
        Assert.That(OpenTypeLineMetrics.Parse(data), Is.EqualTo(new OpenTypeLineMetrics(1000, 1069, 293, 0)));
        U16(data, 212, 0);
        var ordinary = OpenTypeLineMetrics.Parse(data);
        Assert.That(ordinary, Is.EqualTo(new OpenTypeLineMetrics(1000, 600, 200, 50)));
        Assert.That(ordinary.Resolve(20), Is.EqualTo(new FontLineMetrics(12, 4, 1)));
    }
    [Test]
    public void ReadsCollectionOffsetsRelativeToTheWholeMappedFont()
    {
        byte[] data = Font(), collection = new byte[data.Length + 20];
        data.CopyTo(collection, 20);
        U32(collection, 0, 0x74746366); U32(collection, 4, 0x00010000); U32(collection, 8, 1); U32(collection, 12, 20);
        for (int i = 0; i < 3; i++)
        {
            int offset = 20 + 12 + i * 16 + 8;
            U32(collection, offset, BinaryPrimitives.ReadUInt32BigEndian(collection.AsSpan(offset, 4)) + 20);
        }
        Assert.That(OpenTypeLineMetrics.Parse(collection), Is.EqualTo(OpenTypeLineMetrics.Parse(data)));
        Assert.Throws<ArgumentOutOfRangeException>(() => OpenTypeLineMetrics.Parse(collection, 1));
    }
    [Test]
    public void NegativeLeadingDoesNotReduceTheFontStrut()
    {
        byte[] data = Font(); I16(data, 222, -50);
        Assert.That(OpenTypeLineMetrics.Parse(data).Resolve(20).LineGap, Is.Zero);
    }
    [Test]
    public void TruncatedTablesAndZeroUnitsAreNotSilentlyReplacedByFallbackMetrics()
    {
        byte[] data = Font(); U32(data, 20, uint.MaxValue);
        Assert.Throws<InvalidDataException>(() => OpenTypeLineMetrics.Parse(data));
        data = Font(); U16(data, 78, 0);
        Assert.Throws<InvalidDataException>(() => OpenTypeLineMetrics.Parse(data));
    }
    [Test]
    public void BundledFontReportsTypographicMetricsAndCapHeight()
    {
        var metrics = OpenTypeLineMetrics.Parse(File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "NotoSans-Regular.otf")));
        Assert.That(metrics, Is.EqualTo(new OpenTypeLineMetrics(1000, 1069, 293, 0, 714)));
    }
    [Test]
    public void CapHeightIsZeroWhenTheOs2TableDoesNotRecordIt()
    {
        Assert.That(OpenTypeLineMetrics.Parse(Font()).CapHeight, Is.Zero);
    }
    [Test]
    public void TextMetricsPreservesExistingThreePositionConstructionAndDeconstruction()
    {
        var metrics = new TextMetrics(12, 7, 3) { LineGap = 2 };
        var (width, ascent, descent) = metrics;
        Assert.That((width, ascent, descent, metrics.LineGap), Is.EqualTo((12d, 7d, 3d, 2d)));
    }
    private static byte[] Font()
    {
        var data = new byte[228]; U32(data, 0, 0x00010000); U16(data, 4, 3);
        Table(data, 12, 0x68656164, 60, 54); Table(data, 28, 0x68686561, 114, 36); Table(data, 44, 0x4F532F32, 150, 78);
        U16(data, 78, 1000); I16(data, 118, 600); I16(data, 120, -200); I16(data, 122, 50);
        U16(data, 150, 4); U16(data, 212, 0x80); I16(data, 218, 1069); I16(data, 220, -293);
        return data;
    }
    private static void Table(byte[] data, int record, uint tag, uint offset, uint length)
    { U32(data, record, tag); U32(data, record + 8, offset); U32(data, record + 12, length); }
    private static void U32(byte[] data, int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset, 4), value);
    private static void U16(byte[] data, int offset, ushort value) => BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(offset, 2), value);
    private static void I16(byte[] data, int offset, short value) => BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(offset, 2), value);
}
