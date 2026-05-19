using System.IO;

namespace Aslan.Naps.Polyfills;

/// <summary>
/// Polyfills for APIs missing on .NET Framework 4.8.
/// </summary>
internal static class NetFrameworkCompat
{
#if !NET5_0_OR_GREATER
    /// <summary>
    /// Polyfill for Dictionary.GetValueOrDefault which is unavailable on net48.
    /// </summary>
    public static TValue? GetValueOrDefault<TKey, TValue>(
        this Dictionary<TKey, TValue> dict, TKey key, TValue? defaultValue = default)
        where TKey : notnull
    {
        return dict.TryGetValue(key, out var value) ? value : defaultValue;
    }

    /// <summary>
    /// Polyfill for string.Contains(string, StringComparison) which is unavailable on net48.
    /// </summary>
    public static bool Contains(this string source, string value, StringComparison comparison)
    {
        return source.IndexOf(value, comparison) >= 0;
    }
#endif

#if !NET7_0_OR_GREATER
    /// <summary>
    /// Polyfill for Stream.ReadExactlyAsync which is only available in .NET 7+.
    /// Reads exactly the requested number of bytes or throws.
    /// </summary>
    public static async Task ReadExactlyAsync(this Stream stream, byte[] buffer, CancellationToken ct = default)
    {
        var offset = 0;
        var count = buffer.Length;
        while (offset < count)
        {
            var read = await stream.ReadAsync(buffer, offset, count - offset, ct).ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException("Unexpected end of stream");
            offset += read;
        }
    }
#endif
}
