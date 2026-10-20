using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

// Text-safe, versioned, offline payload. The digest verifies the compressed bytes;
// it is not used as a substitute for the session data.
public static class TestShareCodeCodec
{
    private const string Prefix = "DFT1.";
    private const int MaximumDecodedBytes = 32 * 1024 * 1024;

    public static string Encode(string json)
    {
        if (string.IsNullOrEmpty(json)) throw new ArgumentException("测试数据为空。", nameof(json));
        byte[] source = Encoding.UTF8.GetBytes(json);
        if (source.Length > MaximumDecodedBytes) throw new InvalidOperationException("测试记录超过分享码容量上限。");
        byte[] compressed;
        using (var output = new MemoryStream())
        {
            using (var gzip = new GZipStream(output, CompressionLevel.Optimal, true))
                gzip.Write(source, 0, source.Length);
            compressed = output.ToArray();
        }
        using (var sha = SHA256.Create())
        {
            string digest = Convert.ToBase64String(sha.ComputeHash(compressed)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            string body = Convert.ToBase64String(compressed).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            return Prefix + digest + "." + body;
        }
    }

    public static string Decode(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || !code.StartsWith(Prefix, StringComparison.Ordinal))
            throw new FormatException("不是受支持的测试分享码。");
        if (code.Length > MaximumDecodedBytes * 2)
            throw new FormatException("分享码超过容量上限。");
        int separator = code.IndexOf('.', Prefix.Length);
        if (separator < 0) throw new FormatException("分享码缺少校验段。");
        byte[] expected, compressed;
        try
        {
            expected = FromUrlBase64(code.Substring(Prefix.Length, separator - Prefix.Length));
            compressed = FromUrlBase64(code.Substring(separator + 1));
        }
        catch (Exception e) when (e is FormatException || e is ArgumentException)
        { throw new FormatException("分享码字符无效。", e); }
        if (expected.Length != 32 || compressed.Length > MaximumDecodedBytes)
            throw new FormatException("分享码长度无效。");
        using (var sha = SHA256.Create())
        {
            byte[] actual = sha.ComputeHash(compressed);
            for (int i = 0; i < actual.Length; i++)
                if (actual[i] != expected[i]) throw new FormatException("分享码校验失败，可能复制不完整。");
        }
        try
        {
            using (var input = new MemoryStream(compressed))
            using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                byte[] buffer = new byte[8192];
                int count;
                while ((count = gzip.Read(buffer, 0, buffer.Length)) != 0)
                {
                    if (output.Length + count > MaximumDecodedBytes)
                        throw new FormatException("测试记录超过容量上限。");
                    output.Write(buffer, 0, count);
                }
                return new UTF8Encoding(false, true).GetString(output.ToArray());
            }
        }
        catch (InvalidDataException e) { throw new FormatException("分享码压缩数据无效。", e); }
        catch (DecoderFallbackException e) { throw new FormatException("分享码文本无效。", e); }
    }

    private static byte[] FromUrlBase64(string text)
    {
        text = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(text.PadRight((text.Length + 3) / 4 * 4, '='));
    }
}
