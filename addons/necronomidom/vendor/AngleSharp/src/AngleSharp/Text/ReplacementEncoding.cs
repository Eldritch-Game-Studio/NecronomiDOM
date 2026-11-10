namespace AngleSharp.Text
{
    using System;
    using System.Text;

    /// <summary>
    /// The WHATWG replacement encoding: empty input stays empty; any nonempty
    /// byte stream becomes one U+FFFD, regardless of its length or chunking.
    /// This encoding deliberately has no encoder.
    /// </summary>
    internal sealed class ReplacementEncoding : Encoding
    {
        public override String WebName => "replacement";

        public override String EncodingName => "Replacement";

        public override Int32 GetCharCount(Byte[] bytes, Int32 index, Int32 count)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            return GetCharCount(bytes.AsSpan(index, count));
        }

        public override Int32 GetCharCount(ReadOnlySpan<Byte> bytes) => bytes.IsEmpty ? 0 : 1;

        public override Int32 GetChars(Byte[] bytes, Int32 byteIndex, Int32 byteCount, Char[] chars, Int32 charIndex)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            ArgumentNullException.ThrowIfNull(chars);
            return GetChars(bytes.AsSpan(byteIndex, byteCount), chars.AsSpan(charIndex));
        }

        public override Int32 GetChars(ReadOnlySpan<Byte> bytes, Span<Char> chars)
        {
            if (bytes.IsEmpty)
            {
                return 0;
            }

            if (chars.IsEmpty)
            {
                throw new ArgumentException("The output buffer is too small.", nameof(chars));
            }

            chars[0] = '\uFFFD';
            return 1;
        }

        public override Int32 GetMaxCharCount(Int32 byteCount)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(byteCount);
            return byteCount == 0 ? 0 : 1;
        }

        public override Decoder GetDecoder() => new ReplacementDecoder();

        public override Encoder GetEncoder() => throw new NotSupportedException("Replacement has no encoder.");

        public override Int32 GetByteCount(Char[] chars, Int32 index, Int32 count) =>
            throw new NotSupportedException("Replacement has no encoder.");

        public override Int32 GetBytes(Char[] chars, Int32 charIndex, Int32 charCount, Byte[] bytes, Int32 byteIndex) =>
            throw new NotSupportedException("Replacement has no encoder.");

        public override Int32 GetMaxByteCount(Int32 charCount) =>
            throw new NotSupportedException("Replacement has no encoder.");

        private sealed class ReplacementDecoder : Decoder
        {
            private Boolean _returnedReplacement;

            public override Int32 GetCharCount(Byte[] bytes, Int32 index, Int32 count)
            {
                ArgumentNullException.ThrowIfNull(bytes);
                return GetCharCount(bytes.AsSpan(index, count), false);
            }

            public override Int32 GetCharCount(ReadOnlySpan<Byte> bytes, Boolean flush) =>
                !_returnedReplacement && !bytes.IsEmpty ? 1 : 0;

            public override Int32 GetChars(Byte[] bytes, Int32 byteIndex, Int32 byteCount, Char[] chars, Int32 charIndex) =>
                GetChars(bytes, byteIndex, byteCount, chars, charIndex, false);

            public override Int32 GetChars(Byte[] bytes, Int32 byteIndex, Int32 byteCount, Char[] chars, Int32 charIndex, Boolean flush)
            {
                ArgumentNullException.ThrowIfNull(bytes);
                ArgumentNullException.ThrowIfNull(chars);
                return GetChars(bytes.AsSpan(byteIndex, byteCount), chars.AsSpan(charIndex), flush);
            }

            public override Int32 GetChars(ReadOnlySpan<Byte> bytes, Span<Char> chars, Boolean flush)
            {
                var count = GetCharCount(bytes, flush);

                if (count != 0)
                {
                    if (chars.IsEmpty)
                    {
                        throw new ArgumentException("The output buffer is too small.", nameof(chars));
                    }

                    chars[0] = '\uFFFD';
                    _returnedReplacement = true;
                }

                if (flush)
                {
                    Reset();
                }

                return count;
            }

            public override void Convert(Byte[] bytes, Int32 byteIndex, Int32 byteCount, Char[] chars, Int32 charIndex,
                Int32 charCount, Boolean flush, out Int32 bytesUsed, out Int32 charsUsed, out Boolean completed)
            {
                ArgumentNullException.ThrowIfNull(bytes);
                ArgumentNullException.ThrowIfNull(chars);
                Convert(bytes.AsSpan(byteIndex, byteCount), chars.AsSpan(charIndex, charCount), flush,
                    out bytesUsed, out charsUsed, out completed);
            }

            public override void Convert(ReadOnlySpan<Byte> bytes, Span<Char> chars, Boolean flush,
                out Int32 bytesUsed, out Int32 charsUsed, out Boolean completed)
            {
                charsUsed = GetChars(bytes, chars, flush);
                bytesUsed = bytes.Length;
                completed = true;
            }

            public override void Reset() => _returnedReplacement = false;
        }
    }
}
