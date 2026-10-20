using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using YalinMarkdown;
using Localization = YalinMarkdown.Localization;

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

// Translation availability and brand consistency across every selectable language.
Check(Localization.Languages.Length == 10 && Localization.Languages[0].Code == "en",
    "English is the default and all ten languages are selectable");
foreach (var language in Localization.Languages)
{
    var core = Localization.Get(language.Code);
    var panel = AgentPanelLocalization.Get(language.Code);
    Check(core.AppName == "YalinMarkdown" && !string.IsNullOrWhiteSpace(core.Format) &&
          !string.IsNullOrWhiteSpace(core.Untitled) && !string.IsNullOrWhiteSpace(panel.Tools) &&
          !string.IsNullOrWhiteSpace(panel.SkillMatch) && !string.IsNullOrWhiteSpace(panel.ChooseWorkspace),
          $"Localization is complete for {language.Code}");
}

// Agent Tool checks: frontmatter must recognize separate delimiter lines, including CRLF.
var validSkill = "---\r\nname: log-analyzer\r\ndescription: Analyze system logs for errors and anomalies.\r\n---\r\n# Usage\r\nDetails";
Check(AgentAnalyzer.ExtractFrontmatter(validSkill)?.Contains("name: log-analyzer") == true,
    "CRLF YAML frontmatter is recognized");
Check(AgentAnalyzer.ExtractFrontmatter("---not-a-fence\nname: wrong\n---") is null,
    "Malformed frontmatter fence is rejected");
var validAnalysis = AgentAnalyzer.AnalyzeDocument("SKILL.md", validSkill);
Check(!validAnalysis.Findings.Any(f => f.Severity == FindingSeverity.Critical && f.Category == "Skill"),
    "Valid SKILL.md frontmatter has no critical metadata finding");
var badAnalysis = AgentAnalyzer.AnalyzeDocument("SKILL.md", "---\nname: bad\n---trailing\nbody");
Check(badAnalysis.Findings.Any(f => f.Severity == FindingSeverity.Critical && f.Category == "Skill"),
    "Malformed SKILL.md frontmatter is flagged");

var agentDir = Path.Combine(Path.GetTempPath(), "YalinMarkdownAgentTests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(agentDir);
try
{
    var nested = Path.Combine(agentDir, "docs");
    Directory.CreateDirectory(nested);
    await File.WriteAllTextAsync(Path.Combine(nested, "target.md"), "# Document");
    var instructionPath = Path.Combine(nested, "AGENTS.md");
    var instruction = "Use `target.md` for documentation. Test with dotnet test. Install and review security.";
    await File.WriteAllTextAsync(instructionPath, instruction);
    var analysis = AgentAnalyzer.AnalyzeDocument("AGENTS.md", instruction, instructionPath, agentDir);
    Check(!analysis.Findings.Any(f => f.Category == "Path"),
        "Nested relative file paths are resolved from the source document");
    var skillsDir = Path.Combine(agentDir, "skills", "log-analyzer");
    Directory.CreateDirectory(skillsDir);
    var skillPath = Path.Combine(skillsDir, "SKILL.md");
    await File.WriteAllTextAsync(skillPath, validSkill);
    var workspace = AgentAnalyzer.AnalyzeWorkspace(agentDir, instructionPath);
    Check(workspace.AgentFiles.Count == 2 && workspace.MergedInstructions.Contains("AGENTS.md"),
        "Workspace analysis locates instruction and skill files");
    Check(AgentAnalyzer.MatchSkills(agentDir, "Analyze errors in system logs").Any(m => m.Name == "log-analyzer"),
        "Skill matching uses parsed metadata and task keyword overlap");
}
finally
{
    Directory.Delete(agentDir, recursive: true);
}

// =========================================================================
// MarkdownPreviewRenderer regression and feature tests (Markdig-backed FlowDocument)
// =========================================================================

var previewOptions = new MarkdownPreviewOptions
{
    FontFamily = new FontFamily("Consolas"),
    BaseFontSize = 15,
    PreviewZoom = 100,
    TextBrush = Brushes.Black,
    MutedTextBrush = Brushes.Gray,
    BorderBrush = Brushes.LightGray,
    AccentBrush = Brushes.Green,
    PanelHeaderBrush = Brushes.WhiteSmoke
};

// 1. Headings H1 to H6
var headingsMd = "# H1\n## H2\n### H3\n#### H4\n##### H5\n###### H6";
var headingsDoc = MarkdownPreviewRenderer.Render(headingsMd, previewOptions);
Check(headingsDoc.Blocks.Count == 6, "All 6 heading levels generate FlowDocument blocks");
var h1 = (Paragraph)headingsDoc.Blocks.ElementAt(0);
var h2 = (Paragraph)headingsDoc.Blocks.ElementAt(1);
var h6 = (Paragraph)headingsDoc.Blocks.ElementAt(5);
Check(h1.FontSize > h2.FontSize && h2.FontSize > h6.FontSize, "Heading font sizes scale appropriately");
Check(h1.BorderThickness.Bottom == 1 && h2.BorderThickness.Bottom == 1, "H1 and H2 include bottom divider border");
Check(h6.Foreground == previewOptions.MutedTextBrush, "H6 uses muted foreground");

// 2. GitHub Flavored Markdown Tables (headers, borders, alignment)
var tableMd = @"
| Left | Center | Right | Default |
| :--- | :---: | ---: | --- |
| L1   | C1     | R1   | D1      |
| L2   | C2     | R2   | D2      |
";
var tableDoc = MarkdownPreviewRenderer.Render(tableMd, previewOptions);
Check(tableDoc.Blocks.OfType<Table>().Any(), "GFM table is parsed into a native WPF Table block");
var table = tableDoc.Blocks.OfType<Table>().First();
Check(table.Columns.Count == 4, "Table has 4 columns");
Check(table.RowGroups.Count == 1 && table.RowGroups[0].Rows.Count == 3, "Table has 1 header row and 2 data rows");
var headerRow = table.RowGroups[0].Rows[0];
var dataRow1 = table.RowGroups[0].Rows[1];
Check(headerRow.Cells[0].FontWeight == FontWeights.Bold, "Table header row cells are bold");
Check(dataRow1.Cells[0].TextAlignment == TextAlignment.Left, "Left-aligned column has TextAlignment.Left");
Check(dataRow1.Cells[1].TextAlignment == TextAlignment.Center, "Center-aligned column has TextAlignment.Center");
Check(dataRow1.Cells[2].TextAlignment == TextAlignment.Right, "Right-aligned column has TextAlignment.Right");
Check(dataRow1.Cells[0].BorderThickness == new Thickness(1), "Table cells have border outline");

// 3. Task lists ([ ] and [x])
var taskListMd = "- [ ] Unfinished item\n- [x] Finished item\n- [X] Another finished item";
var taskDoc = MarkdownPreviewRenderer.Render(taskListMd, previewOptions);
Check(taskDoc.Blocks.OfType<List>().Any(), "Task list is parsed into a List block");
var taskList = taskDoc.Blocks.OfType<List>().First();
Check(taskList.ListItems.Count == 3, "Task list has 3 items");
var taskText1 = string.Concat(taskDoc.Blocks.OfType<List>().First().ListItems.ElementAt(0).Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines).OfType<Run>().Select(r => r.Text));
var taskText2 = string.Concat(taskDoc.Blocks.OfType<List>().First().ListItems.ElementAt(1).Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines).OfType<Run>().Select(r => r.Text));
Check(taskText1.Contains("☐") && taskText1.Contains("Unfinished item"), "Unchecked task list item contains unchecked box symbol");
Check(taskText2.Contains("☑") && taskText2.Contains("Finished item"), "Checked task list item contains checked box symbol");

// Mixed task and ordinary list items must not lose the ordinary bullet marker.
var mixedListMd = "- [x] Completed\n- Normal item\n- [ ] Pending";
var mixedListDoc = MarkdownPreviewRenderer.Render(mixedListMd, previewOptions);
var mixedList = mixedListDoc.Blocks.OfType<List>().First();
Check(mixedList.MarkerStyle == TextMarkerStyle.None, "Mixed checkbox list suppresses common WPF marker");
var mixedText = string.Concat(mixedList.ListItems.ElementAt(1).Blocks.OfType<Paragraph>()
    .SelectMany(p => p.Inlines).OfType<Run>().Select(r => r.Text));
Check(mixedText.Contains("• ") && mixedText.Contains("Normal item"),
    "Mixed checkbox list keeps the ordinary item's bullet marker");

// 4. Nested lists (ordered and unordered)
var nestedListMd = @"
- Parent 1
  - Child 1.1
  - Child 1.2
- Parent 2
  1. Ordered 1
  2. Ordered 2
";
var nestedDoc = MarkdownPreviewRenderer.Render(nestedListMd, previewOptions);
var topList = nestedDoc.Blocks.OfType<List>().First();
Check(topList.ListItems.Count == 2, "Top-level list has 2 items");
var childListInItem1 = topList.ListItems.ElementAt(0).Blocks.OfType<List>().FirstOrDefault();
Check(childListInItem1 != null && childListInItem1.ListItems.Count == 2, "Nested unordered list rendered inside parent ListItem");
var childListInItem2 = topList.ListItems.ElementAt(1).Blocks.OfType<List>().FirstOrDefault();
Check(childListInItem2 != null && childListInItem2.MarkerStyle == TextMarkerStyle.Decimal, "Nested ordered list rendered with Decimal marker");

// 5. Blockquotes and nested blockquotes
var quoteMd = "> Top quote\n>> Nested quote\n> Continues";
var quoteDoc = MarkdownPreviewRenderer.Render(quoteMd, previewOptions);
Check(quoteDoc.Blocks.OfType<Section>().Any(), "Blockquote is parsed into a Section block with accent border");
var topQuote = quoteDoc.Blocks.OfType<Section>().First();
Check(topQuote.BorderThickness.Left == 3 && topQuote.BorderBrush == previewOptions.AccentBrush, "Blockquote has accent left border");
Check(topQuote.Blocks.OfType<Section>().Any(), "Nested blockquote rendered as child Section");

// 6. Thematic break (horizontal rule)
var hrMd = "Text before\n\n---\n\nText after";
var hrDoc = MarkdownPreviewRenderer.Render(hrMd, previewOptions);
Check(hrDoc.Blocks.Count == 3, "Horizontal rule rendered between paragraphs");
var hrPara = (Paragraph)hrDoc.Blocks.ElementAt(1);
Check(hrPara.BorderThickness.Bottom == 1, "Horizontal rule has bottom border");

// 7. Bold, Italic, Strikethrough and combinations
var formatMd = "**bold** and *italic* and ***bold italic*** and ~~strikethrough~~ and **~~bold strike~~**";
var formatDoc = MarkdownPreviewRenderer.Render(formatMd, previewOptions);
var pFormat = (Paragraph)formatDoc.Blocks.First();
Check(pFormat.Inlines.OfType<Bold>().Any(), "Bold inline is parsed");
Check(pFormat.Inlines.OfType<Italic>().Any(), "Italic inline is parsed");
Check(pFormat.Inlines.OfType<Span>().Any(s => s.TextDecorations == TextDecorations.Strikethrough), "Strikethrough inline is parsed");

// Toolbar-created font-size spans must survive Markdig's HTML inline parsing.
var styledMd = "Text <span style=\"font-size:22px\">larger **bold**</span> end";
var styledDoc = MarkdownPreviewRenderer.Render(styledMd, previewOptions);
var styledParagraph = (Paragraph)styledDoc.Blocks.First();
var styledSpan = styledParagraph.Inlines.OfType<Span>()
    .FirstOrDefault(span => Math.Abs(span.FontSize - 22) < 0.01);
Check(styledSpan is not null && styledSpan.Inlines.OfType<Bold>().Any(),
    "Toolbar font-size span preserves its size and nested Markdown bold");
Check(!styledParagraph.Inlines.OfType<Run>().Any(run => run.Text.Contains("</span>")),
    "Toolbar styled span does not leak HTML closing tags into preview");

// 8. Code blocks and inline code
var codeMd = "Inline `code snippet` here.\n\n```csharp\nvar x = 1;\nvar y = 2;\n```";
var codeDoc = MarkdownPreviewRenderer.Render(codeMd, previewOptions);
var codePara = (Paragraph)codeDoc.Blocks.ElementAt(0);
Check(codePara.Inlines.OfType<Run>().Any(r => r.Text == "code snippet" && r.FontFamily.Source == "Consolas"), "Inline code has Consolas font");
var codeBlockPara = (Paragraph)codeDoc.Blocks.ElementAt(1);
Check(codeBlockPara.FontFamily.Source == "Consolas" && codeBlockPara.BorderThickness == new Thickness(1), "Code block has Consolas font and border");

// 9. Links and Markdown images
var linksMd = "Visit [OpenAI](https://openai.com) and ![Demo Image](https://example.com/logo.png)";
var linksDoc = MarkdownPreviewRenderer.Render(linksMd, previewOptions);
var linksPara = (Paragraph)linksDoc.Blocks.First();
Check(linksPara.Inlines.OfType<Hyperlink>().Any(h => h.NavigateUri == new Uri("https://openai.com")), "External HTTPS link is parsed into Hyperlink");
Check(linksPara.Inlines.OfType<Span>().Any(s => s.Inlines.OfType<Run>().Any(r => r.Text.Contains("🖼 [Demo Image]"))), "Markdown image rendered safely as an image badge without downloading");

// 10. Malformed or unsafe links (javascript:, file:)
var unsafeMd = "[Evil Link](javascript:alert(1)) and [File Link](file:///C:/Windows/win.ini)";
var unsafeDoc = MarkdownPreviewRenderer.Render(unsafeMd, previewOptions);
var unsafePara = (Paragraph)unsafeDoc.Blocks.First();
Check(!unsafePara.Inlines.OfType<Hyperlink>().Any(), "Unsafe link schemes are not rendered as clickable Hyperlinks");

// 11. Escaped Markdown characters and multiline paragraphs
var escapeMd = "Look at \\*not italic\\* and \\[not link\\] and \\# not heading";
var escapeDoc = MarkdownPreviewRenderer.Render(escapeMd, previewOptions);
var escapePara = (Paragraph)escapeDoc.Blocks.First();
var escapeText = string.Concat(escapePara.Inlines.OfType<Run>().Select(r => r.Text));
Check(!escapePara.Inlines.OfType<Italic>().Any(), "Escaped asterisk is not parsed as italic");
Check(escapeText.Contains("*not italic*") && escapeText.Contains("[not link]"), "Escaped characters render as literal text");

// 12. Multiline paragraph with soft break vs hard break
var multilineMd = "Line 1\nLine 2  \nLine 3";
var multiDoc = MarkdownPreviewRenderer.Render(multilineMd, previewOptions);
var multiPara = (Paragraph)multiDoc.Blocks.First();
Check(multiPara.Inlines.OfType<LineBreak>().Any(), "Hard line break (two spaces) renders as LineBreak");

// 13. Safety: embedded script tags are never executed
var scriptMd = "Normal text\n\n<script>alert('hack');</script>\n\nMore text";
var scriptDoc = MarkdownPreviewRenderer.Render(scriptMd, previewOptions);
var scriptTexts = scriptDoc.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines).OfType<Run>().Select(r => r.Text);
Check(!scriptTexts.Any(t => t.Contains("alert('hack')")), "Embedded script block is suppressed and never executed");

// 14. Document truncation notice
var truncatedDoc = MarkdownPreviewRenderer.Render(large, previewOptions);
Check(truncatedDoc.Blocks.OfType<Paragraph>().First().Inlines.OfType<Run>().Any(r => r.Text.Contains("Preview limited for performance")),
    "Oversized document preview displays defensive truncation banner");

// 15. Verify application assembly version is v0.1.2
var candidateDlls = new[]
{
    Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "bin", "Release", "net10.0-windows", "YalinMarkdown.dll")),
    Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "bin", "Release", "net10.0-windows", "YalinMarkdown.dll")),
    Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "bin", "Release", "net10.0-windows", "YalinMarkdown.dll"))
};
var dllPath = candidateDlls.FirstOrDefault(File.Exists);
if (dllPath is not null)
{
    var asm = System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(dllPath);
    var mainType = asm.GetType("YalinMarkdown.MainWindow");
    var getVersionMethod = mainType?.GetMethod("GetAppVersion", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
    var appVersion = getVersionMethod?.Invoke(null, null) as string;
    Check(appVersion == "v0.1.2", $"Application assembly version is {appVersion} (expected v0.1.2)");
}
else
{
    // On clean CI checkouts where SmokeTests runs before building MarkdownEditor.csproj,
    // verify the version metadata in the project definition.
    var candidateCsprojs = new[]
    {
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "MarkdownEditor.csproj")),
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "MarkdownEditor.csproj")),
        Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "MarkdownEditor.csproj"))
    };
    var csprojPath = candidateCsprojs.FirstOrDefault(File.Exists);
    Check(csprojPath is not null, "Found MarkdownEditor.csproj for version inspection");
    if (csprojPath is not null)
    {
        var csprojText = File.ReadAllText(csprojPath);
        Check(csprojText.Contains("<Version>0.1.2</Version>"), "MarkdownEditor.csproj specifies Version 0.1.2");
    }
}

// 16. Portable file association declarations (read-only; no registry mutations).
Check(PortableFileAssociations.Extensions.Length == 4 &&
      new[] { ".md", ".markdown", ".txt", ".text" }
          .All(extension => PortableFileAssociations.Extensions.Contains(extension)),
    "Exactly four Markdown/text extensions are declared");
Check(PortableFileAssociations.SupportsPath(@"C:\Example Folder\notes.MD") &&
      PortableFileAssociations.SupportsPath(@"C:\Example Folder\notes.TeXt") &&
      !PortableFileAssociations.SupportsPath(@"C:\Example Folder\program.exe"),
    "Supported paths accept case-insensitive extensions and reject EXE files");
Check(PortableFileAssociations.ProgIdFor(".md") ==
      PortableFileAssociations.ProgIdFor(".markdown") &&
      PortableFileAssociations.ProgIdFor(".txt") ==
      PortableFileAssociations.ProgIdFor(".text") &&
      PortableFileAssociations.ProgIdFor(".md") !=
      PortableFileAssociations.ProgIdFor(".txt"),
    "Markdown and text file groups use distinct application ProgIDs");
var expectedOpenCommand = string.Concat('"', @"C:\Test Apps\YalinMarkdown.exe", '"', " ", '"', "%1", '"');
Check(PortableFileAssociations.BuildOpenCommand(@"C:\Test Apps\YalinMarkdown.exe") ==
      expectedOpenCommand,
    "Explorer command quotes executable and file path placeholder");
try
{
    PortableFileAssociations.BuildOpenCommand(@"bad""path.exe");
    Check(false, "Quoted invalid executable path is rejected");
}
catch (ArgumentException)
{
    Check(true, "Quoted invalid executable path is rejected");
}

Console.WriteLine("All YalinMarkdown smoke tests passed.");
