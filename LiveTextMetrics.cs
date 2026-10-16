using System.Text;

namespace YalinMarkdown;

/// <summary>
/// Keeps live counters exact without rescanning every character after ordinary edits.
/// TextBox supplies the new snapshot and the offset/length of a single edit.
/// Complex multi-change notifications can safely fall back to Reset.
/// </summary>
internal sealed class LiveTextMetrics
{
    private string text = string.Empty;

    internal int Characters => text.Length;
    internal int Words { get; private set; }
    internal long Utf8Bytes { get; private set; }
    internal long ApproximateTokens => Math.Max(1, (Utf8Bytes + 3) / 4);

    internal void Reset(string value)
    {
        text = value;
        Words = CountWordStarts(value, 0, value.Length);
        Utf8Bytes = Encoding.UTF8.GetByteCount(value);
    }

    internal bool TryApplySingleChange(string next, int offset, int removedLength, int addedLength)
    {
        if (offset < 0 || removedLength < 0 || addedLength < 0 ||
            offset > text.Length - removedLength || offset > next.Length - addedLength ||
            text.Length - removedLength + addedLength != next.Length)
            return false;

        // The characters immediately following a change can gain/lose a word boundary.
        var oldEnd = Math.Min(text.Length, offset + removedLength + 1);
        var newEnd = Math.Min(next.Length, offset + addedLength + 1);
        Words += CountWordStarts(next, offset, newEnd) - CountWordStarts(text, offset, oldEnd);

        // Include one UTF-16 code unit on each side for surrogate-pair edits.
        var byteStart = Math.Max(0, offset - 1);
        Utf8Bytes += Encoding.UTF8.GetByteCount(next.AsSpan(byteStart, newEnd - byteStart))
            - Encoding.UTF8.GetByteCount(text.AsSpan(byteStart, oldEnd - byteStart));
        text = next;
        return true;
    }

    private static int CountWordStarts(string value, int start, int end)
    {
        var count = 0;
        for (var i = start; i < end; i++)
        {
            if (!char.IsWhiteSpace(value[i]) && (i == 0 || char.IsWhiteSpace(value[i - 1])))
                count++;
        }
        return count;
    }
}
