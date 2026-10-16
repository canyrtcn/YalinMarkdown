using System.IO;
using System.Text;

namespace YalinMarkdown;

/// <summary>
/// Defensive limits and filesystem operations that do not depend on WPF.
/// The source document is never shortened; only the preview is bounded.
/// </summary>
internal static class DocumentSafety
{
    internal const long MaxOpenFileBytes = 8L * 1024 * 1024;
    internal const int MaxPreviewCharacters = 80_000;
    internal const int MaxPreviewLines = 1_200;
    internal const int MaxPreviewLineCharacters = 4_096;

    internal static string GetPreviewExcerpt(string content, out bool truncated)
    {
        var max = Math.Min(content.Length, MaxPreviewCharacters);
        var end = max;
        var lines = 0;
        for (var index = 0; index < max; index++)
        {
            if (content[index] != '\n')
                continue;
            lines++;
            if (lines >= MaxPreviewLines)
            {
                end = index + 1;
                break;
            }
        }
        // Avoid rendering dangling high surrogates at the excerpt boundary.
        if (end > 0 && end < content.Length && char.IsHighSurrogate(content[end - 1]))
            end--;
        truncated = end < content.Length;
        return truncated ? content[..end] : content;
    }

    internal static string ClipPreviewLine(string line)
    {
        if (line.Length <= MaxPreviewLineCharacters)
            return line;
        var end = MaxPreviewLineCharacters;
        if (char.IsHighSurrogate(line[end - 1]))
            end--;
        return line[..end] + " … [line shortened in preview]";
    }

    internal static Encoding RestoreEncoding(int codePage, bool emitBom) => codePage switch
    {
        1200 => new UnicodeEncoding(bigEndian: false, byteOrderMark: emitBom, throwOnInvalidBytes: true),
        1201 => new UnicodeEncoding(bigEndian: true, byteOrderMark: emitBom, throwOnInvalidBytes: true),
        12000 => new UTF32Encoding(bigEndian: false, byteOrderMark: emitBom, throwOnInvalidCharacters: true),
        12001 => new UTF32Encoding(bigEndian: true, byteOrderMark: emitBom, throwOnInvalidCharacters: true),
        _ => new UTF8Encoding(emitBom, throwOnInvalidBytes: true)
    };

    /// <summary>
    /// Reads UTF-8, UTF-16 or UTF-32, preserving a byte-order mark when present.
    /// Invalid non-UTF-8 legacy encodings are rejected rather than silently corrupted.
    /// </summary>
    internal static async Task<(string Content, Encoding Encoding)> ReadDocumentAsync(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite, bufferSize: 4096, useAsync: true);
        // Enforce the same defensive limit for direct calls as for the file picker.
        if (stream.Length > MaxOpenFileBytes)
            throw new IOException("File exceeds the safe editing limit.");
        var marker = new byte[4];
        var read = 0;
        while (read < marker.Length)
        {
            var next = await stream.ReadAsync(marker.AsMemory(read));
            if (next == 0)
                break;
            read += next;
        }
        stream.Position = 0;
        var encoding = read >= 4 && marker[0] == 0x00 && marker[1] == 0x00 && marker[2] == 0xFE && marker[3] == 0xFF
            ? RestoreEncoding(12001, true)
            : read >= 4 && marker[0] == 0xFF && marker[1] == 0xFE && marker[2] == 0x00 && marker[3] == 0x00
                ? RestoreEncoding(12000, true)
                : read >= 3 && marker[0] == 0xEF && marker[1] == 0xBB && marker[2] == 0xBF
                    ? RestoreEncoding(65001, true)
                    : read >= 2 && marker[0] == 0xFF && marker[1] == 0xFE
                        ? RestoreEncoding(1200, true)
                        : read >= 2 && marker[0] == 0xFE && marker[1] == 0xFF
                            ? RestoreEncoding(1201, true)
                            : RestoreEncoding(65001, false);
        using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true);
        var content = new StringBuilder();
        var buffer = new char[8192];
        int charsRead;
        while ((charsRead = await reader.ReadAsync(buffer.AsMemory())) != 0)
        {
            // Also bound allocations if the file grows after the size check.
            if (content.Length > MaxOpenFileBytes - charsRead)
                throw new IOException("File grew beyond the safe editing limit while being read.");
            content.Append(buffer, 0, charsRead);
        }
        return (content.ToString(), encoding);
    }

    /// <summary>
    /// Write in the same directory then atomically replace on filesystems supporting File.Replace.
    /// Never truncate the destination before the replacement is ready.
    /// </summary>
    internal static void AtomicWriteAllText(string filePath, string content, Encoding? encoding = null)
    {
        var fullPath = Path.GetFullPath(filePath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new IOException("The destination directory could not be determined.");
        var temporaryPath = Path.Combine(directory,
            "." + Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temporaryPath, content, encoding ?? RestoreEncoding(65001, false));
            if (File.Exists(fullPath))
                File.Replace(temporaryPath, fullPath, null, ignoreMetadataErrors: true);
            else
                File.Move(temporaryPath, fullPath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
