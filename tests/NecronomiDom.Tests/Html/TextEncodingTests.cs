using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text;
using AngleSharp.Browser;
using AngleSharp.Html;
using AngleSharp.Text;
using NUnit.Framework;

namespace NecronomiDom.Tests.Html;

[TestFixture]
public sealed class TextEncodingTests
{
    [TestCase("windows-1252", "80E9", "€é")]
    [TestCase("iso-8859-1", "80E9", "€é")]
    [TestCase("windows-1251", "C0", "А")]
    [TestCase("shift_jis", "82A0", "あ")]
    [TestCase("gb18030", "D6D0", "中")]
    [TestCase("big5", "A4A4", "中")]
    [TestCase("iso-2022-jp", "1B244224221B2842", "あ")]
    public void LegacyLabelsDecodeWithoutGlobalProviderRegistration(string label, string hex, string expected)
    {
        Assert.That(TextEncoding.IsSupported(label), Is.True);
        Assert.That(TextEncoding.Resolve(label).GetString(Convert.FromHexString(hex)), Is.EqualTo(expected));
    }

    [TestCase("csiso2022kr")]
    [TestCase("hz-gb-2312")]
    [TestCase("iso-2022-cn")]
    [TestCase("iso-2022-cn-ext")]
    [TestCase("iso-2022-kr")]
    [TestCase("replacement")]
    [TestCase("ISO-2022-CN")]
    public void ReplacementLabelsDoNotDecodeMarkupOrMasqueradeAsUtf8(string label)
    {
        var encoding = TextEncoding.Resolve(label);
        var bytes = Encoding.UTF8.GetBytes("<script>not decoded</script>");
        var chars = new char[1];

        Assert.That(TextEncoding.IsSupported(label), Is.True);
        Assert.That(encoding.WebName, Is.EqualTo("replacement"));
        Assert.That(TextEncoding.Parse($"text/html; charset={label}"), Is.SameAs(encoding));
        Assert.That(encoding.GetString(Array.Empty<byte>()), Is.Empty);
        Assert.That(encoding.GetString(bytes), Is.EqualTo("\uFFFD"));
        Assert.That(encoding.GetCharCount(bytes), Is.EqualTo(1));
        Assert.That(encoding.GetChars(bytes.AsSpan(), chars.AsSpan()), Is.EqualTo(1));
        Assert.That(chars[0], Is.EqualTo('\uFFFD'));
        using var source = new ReadOnlyByteTextSource(bytes, encoding);
        Assert.That(source.Text, Is.EqualTo("\uFFFD"));
        using var stream = new MemoryStream(bytes);
        using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: false);
        Assert.That(reader.ReadToEnd(), Is.EqualTo("\uFFFD"));
        Assert.Throws<NotSupportedException>(() => encoding.GetEncoder());
    }

    [Test]
    public void ReplacementDecoderEmitsOnceAcrossChunksAndResetsAfterFlush()
    {
        var decoder = TextEncoding.Resolve("iso-2022-cn").GetDecoder();
        var bytes = new byte[] { 0x41, 0x42, 0x43 };
        var chars = new char[1];

        Assert.That(decoder.GetCharCount(bytes, 0, 1), Is.EqualTo(1));
        Assert.That(decoder.GetCharCount(bytes, 0, 1), Is.EqualTo(1), "Counting must not consume state.");
        Assert.Throws<ArgumentException>(() => decoder.GetChars(bytes, 0, 1, Array.Empty<char>(), 0));
        decoder.Convert(bytes, 0, 1, chars, 0, 1, false, out var bytesUsed, out var charsUsed, out var completed);
        Assert.That((bytesUsed, charsUsed, completed), Is.EqualTo((1, 1, true)));
        Assert.That(chars[0], Is.EqualTo('\uFFFD'));
        Assert.That(decoder.GetCharCount(bytes, 1, 2), Is.Zero);
        decoder.Convert(bytes, 1, 2, Array.Empty<char>(), 0, 0, false, out bytesUsed, out charsUsed, out completed);
        Assert.That((bytesUsed, charsUsed, completed), Is.EqualTo((2, 0, true)));
        decoder.Convert(Array.Empty<byte>(), 0, 0, chars, 0, 1, true, out bytesUsed, out charsUsed, out completed);
        Assert.That((bytesUsed, charsUsed, completed), Is.EqualTo((0, 0, true)));
        Assert.That(decoder.GetChars(bytes, 0, 1, chars, 0), Is.EqualTo(1));
        decoder.Reset();
        Assert.That(decoder.GetChars(bytes, 0, 1, chars, 0), Is.EqualTo(1));
    }

    [Test]
    public void ReplacementFormOutputUsesTheStandardsUtf8OutputEncoding()
    {
        var form = new FormDataSet();
        form.Append("name", "é", "text");
        form.Append("_charset_", "", "hidden");
        using var body = form.AsUrlEncoded(TextEncoding.Resolve("replacement"));
        using var reader = new StreamReader(body);
        Assert.That(reader.ReadToEnd(), Is.EqualTo("name=%C3%A9&_charset_=utf-8"));
    }

    [Test]
    [NonParallelizable]
    public void LocalCodePagesProviderDoesNotRootCollectibleContext()
    {
        var context = InitializeCollectibleEncodings();

        for (var attempt = 0; attempt < 10 && context.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.That(context.IsAlive, Is.False, "Encoding initialization must not register a collectible provider process-wide.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference InitializeCollectibleEncodings()
    {
        var context = new EncodingLoadContext();
        var weak = new WeakReference(context);

        try
        {
            var assembly = context.LoadFromAssemblyPath(typeof(TextEncoding).Assembly.Location);
            var handler = assembly.GetType(typeof(EncodingMetaHandler).FullName!, throwOnError: true)!;
            _ = Activator.CreateInstance(handler);
            var type = assembly.GetType(typeof(TextEncoding).FullName!, throwOnError: true)!;
            var resolve = type.GetMethod(nameof(TextEncoding.Resolve), BindingFlags.Public | BindingFlags.Static)!;
            var legacy = (Encoding)resolve.Invoke(null, new object[] { "windows-1252" })!;
            var replacement = (Encoding)resolve.Invoke(null, new object[] { "iso-2022-cn" })!;

            // Force CodePages into the collectible context, even on a host which
            // normally shares it from the default context (such as dotnet test).
            if (AssemblyLoadContext.GetLoadContext(legacy.GetType().Assembly) != context)
                throw new InvalidOperationException("The smoke must exercise a collectible CodePages provider.");
            if (legacy.GetString(new byte[] { 0x80 }) != "€" || replacement.GetString(new byte[] { 0x41 }) != "\uFFFD")
                throw new InvalidOperationException("Collectible encoding initialization decoded incorrectly.");
        }
        finally
        {
            context.Unload();
        }

        return weak;
    }

    private sealed class EncodingLoadContext() : AssemblyLoadContext(isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName assemblyName) =>
            assemblyName.Name == typeof(CodePagesEncodingProvider).Assembly.GetName().Name
                ? LoadFromAssemblyPath(typeof(CodePagesEncodingProvider).Assembly.Location)
                : null;
    }
}
