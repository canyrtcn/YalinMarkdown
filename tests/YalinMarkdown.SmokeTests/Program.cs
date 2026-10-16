using System.Text;
using YalinMarkdown;

static void Check(bool result, string message)
{
    if (!result)
        throw new InvalidOperationException("FAIL: " + message);
    Console.WriteLine("PASS: " + message);
}

// Live counters must reflect every edit, not wait for a debounce timer.
static int ExpectedWords(string value)
{
    var words = 0;
    var inWord = false;
    foreach (var character in value)
    {
        if (char.IsWhiteSpace(character)) inWord = false;
        else if (!inWord) { words++; inWord = true; }
    }
    return words;
}

var metrics = new LiveTextMetrics();
var currentText = "Bir iki 😀\nthree";
metrics.Reset(currentText);
void VerifyMetrics(string label)
{
    Check(metrics.Characters == currentText.Length &&
          metrics.Words == ExpectedWords(currentText) &&
          metrics.Utf8Bytes == Encoding.UTF8.GetByteCount(currentText) &&
          metrics.ApproximateTokens == Math.Max(1L, (Encoding.UTF8.GetByteCount(currentText) + 3L) / 4), label);
}
void ReplaceMetrics(int offset, int removedLength, string inserted, string label)
{
    var updated = currentText[..offset] + inserted + currentText[(offset + removedLength)..];
    Check(metrics.TryApplySingleChange(updated, offset, removedLength, inserted.Length),
          label + " applied");
    currentText = updated;
    VerifyMetrics(label);
}
VerifyMetrics("Initial live counters are exact");
ReplaceMetrics(0, 0, "Yeni ", "Insert word at the beginning");
ReplaceMetrics(4, 1, "_", "Replace whitespace with punctuation");
ReplaceMetrics(currentText.Length, 0, " 😀", "Append multibyte Unicode");
ReplaceMetrics(1, 3, "\n", "Delete within a word and add line break");
metrics.Reset(string.Empty);
currentText = string.Empty;
VerifyMetrics("Empty text counters are exact");

var random = new Random(2026);
const string alphabet = "abc  \t\n";
for (var i = 0; i < 2000; i++)
{
    var at = random.Next(currentText.Length + 1);
    var length = random.Next(Math.Min(5, currentText.Length - at) + 1);
    var inserted = new string(Enumerable.Range(0, random.Next(6))
        .Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray());
    var updated = currentText[..at] + inserted + currentText[(at + length)..];
    if (!metrics.TryApplySingleChange(updated, at, length, inserted.Length))
        throw new InvalidOperationException("Random edit was rejected at " + i);
    currentText = updated;
    if (metrics.Words != ExpectedWords(currentText) ||
        metrics.Characters != currentText.Length ||
        metrics.Utf8Bytes != Encoding.UTF8.GetByteCount(currentText))
        throw new InvalidOperationException("Counter drift at random edit " + i);
}
Console.WriteLine("PASS: 2,000 incremental live edits match full recounts");

var small = "# Hello\nBody";
Check(DocumentSafety.GetPreviewExcerpt(small, out var shortTruncated) == small && !shortTruncated,
    "Short preview is unchanged");

var large = string.Concat(Enumerable.Repeat("# Heading\nA paragraph\n", 15_000));
var excerpt = DocumentSafety.GetPreviewExcerpt(large, out var largeTruncated);
Check(largeTruncated && excerpt.Length <= DocumentSafety.MaxPreviewCharacters &&
      excerpt.Length < large.Length, "Large preview is bounded and flagged");
Check(DocumentSafety.ClipPreviewLine(new string('x', 10_000)).Length < 5_000,
    "Long individual preview lines are clipped");
var emojiLine = new string('x', DocumentSafety.MaxPreviewLineCharacters - 1) + "😀" + "tail";
Check(!DocumentSafety.ClipPreviewLine(emojiLine).Contains('\ud83d') &&
      !DocumentSafety.ClipPreviewLine(emojiLine).Contains('\ude00'),
    "Preview clipping avoids cutting surrogate pairs in half");

var tempDir = Path.Combine(Path.GetTempPath(), "YalinMarkdownSmoke-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempDir);
try
{
    var file = Path.Combine(tempDir, "example.md");
    DocumentSafety.AtomicWriteAllText(file, "first");
    Check(File.ReadAllText(file) == "first", "New file is saved");
    DocumentSafety.AtomicWriteAllText(file, "İkinci belge 😀");
    Check(File.ReadAllText(file, Encoding.UTF8) == "İkinci belge 😀", "Existing file is replaced with UTF-8 content");
    Check(Directory.GetFiles(tempDir).Length == 1, "Temporary save files are cleaned");
    DocumentSafety.AtomicWriteAllText(file, "BOM preserved", DocumentSafety.RestoreEncoding(65001, true));
    var (bomText, bomEncoding) = await DocumentSafety.ReadDocumentAsync(file);
    Check(bomText == "BOM preserved" && bomEncoding.GetPreamble().Length == 3,
        "UTF-8 BOM is recognized and preserved");
    DocumentSafety.AtomicWriteAllText(file, "Türkçe içerik", DocumentSafety.RestoreEncoding(1200, true));
    var (unicodeText, unicodeEncoding) = await DocumentSafety.ReadDocumentAsync(file);
    Check(unicodeText == "Türkçe içerik" && unicodeEncoding.CodePage == 1200,
        "UTF-16 LE documents keep their encoding");
    await File.WriteAllBytesAsync(file, [0xff, 0x80, 0xfe]);
    var invalidUtf8Rejected = false;
    try { await DocumentSafety.ReadDocumentAsync(file); }
    catch (DecoderFallbackException) { invalidUtf8Rejected = true; }
    Check(invalidUtf8Rejected, "Unrecognized legacy text is rejected instead of silently damaged");

    foreach (var codePage in new[] { 1201, 12000, 12001 })
    {
        var expected = "Yalın Markdown\r\nİçerik 😀";
        DocumentSafety.AtomicWriteAllText(file, expected, DocumentSafety.RestoreEncoding(codePage, true));
        var (actual, detected) = await DocumentSafety.ReadDocumentAsync(file);
        Check(actual == expected && detected.CodePage == codePage && detected.GetPreamble().Length > 0,
            $"UTF encoding with code page {codePage} and CRLF survives a round trip");
    }

    await using (var oversized = new FileStream(file, FileMode.Create, FileAccess.Write))
        oversized.SetLength(DocumentSafety.MaxOpenFileBytes + 1);
    var oversizeRejected = false;
    try { await DocumentSafety.ReadDocumentAsync(file); }
    catch (IOException) { oversizeRejected = true; }
    Check(oversizeRejected, "Oversized files are rejected by the reader itself");
}
finally
{
    Directory.Delete(tempDir, recursive: true);
}

Console.WriteLine("All YalinMarkdown smoke tests passed.");
