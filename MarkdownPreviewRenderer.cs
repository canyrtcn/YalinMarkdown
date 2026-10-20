using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Navigation;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

using WpfBlock = System.Windows.Documents.Block;
using WpfInline = System.Windows.Documents.Inline;
using WpfTable = System.Windows.Documents.Table;
using WpfTableRow = System.Windows.Documents.TableRow;
using WpfTableCell = System.Windows.Documents.TableCell;

using MdBlock = Markdig.Syntax.Block;
using MdInline = Markdig.Syntax.Inlines.Inline;
using MdTable = Markdig.Extensions.Tables.Table;
using MdTableRow = Markdig.Extensions.Tables.TableRow;
using MdTableCell = Markdig.Extensions.Tables.TableCell;

namespace YalinMarkdown;

public sealed class MarkdownPreviewOptions
{
    public FontFamily FontFamily { get; set; } = new("Consolas");
    public double BaseFontSize { get; set; } = 15;
    public int PreviewZoom { get; set; } = 100;
    public Brush TextBrush { get; set; } = Brushes.Black;
    public Brush MutedTextBrush { get; set; } = Brushes.Gray;
    public Brush BorderBrush { get; set; } = Brushes.LightGray;
    public Brush AccentBrush { get; set; } = Brushes.Green;
    public Brush PanelHeaderBrush { get; set; } = Brushes.WhiteSmoke;
    public RequestNavigateEventHandler? HyperlinkNavigateHandler { get; set; }

    public double Scale(double value) => value * (BaseFontSize / 15.0) * PreviewZoom / 100.0;
}

public static class MarkdownPreviewRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseGridTables()
        .UseTaskLists()
        .UseEmphasisExtras()
        .UseAutoLinks()
        .Build();

    private static readonly Regex StyledSpanOpeningPattern =
        new(@"^<span\s+style=""(?<property>font-family|font-size)\s*:\s*(?<value>[^""]+)""\s*>$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250));

    private static readonly Regex StyledSpanPattern =
        new(@"^<span style=""(?<property>font-family|font-size):(?<value>[^""]+)"">(?<text>.+)</span>$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250));

    public static FlowDocument Render(string markdownText, MarkdownPreviewOptions options)
    {
        var excerpt = DocumentSafety.GetPreviewExcerpt(markdownText, out var truncated);

        var document = new FlowDocument
        {
            PagePadding = new Thickness(10),
            FontFamily = options.FontFamily,
            FontSize = options.Scale(15),
            Foreground = options.TextBrush,
            Background = Brushes.Transparent,
            LineHeight = options.Scale(24)
        };

        if (truncated)
        {
            document.Blocks.Add(new Paragraph(new Run(
                "Preview limited for performance. The complete file is still editable and will be saved."))
            {
                Foreground = options.MutedTextBrush,
                Margin = new Thickness(0, 0, 0, 12),
                FontStyle = FontStyles.Italic
            });
        }

        var ast = Markdown.Parse(excerpt, Pipeline);
        RenderContainer(ast, document.Blocks, options, listLevel: 0);

        return document;
    }

    private static void RenderContainer(ContainerBlock container, BlockCollection target, MarkdownPreviewOptions options, int listLevel)
    {
        foreach (var block in container)
        {
            var wpfBlock = RenderBlock(block, options, listLevel);
            if (wpfBlock is not null)
                target.Add(wpfBlock);
        }
    }

    private static WpfBlock? RenderBlock(MdBlock block, MarkdownPreviewOptions options, int listLevel) => block switch
    {
        HeadingBlock heading => RenderHeading(heading, options),
        ParagraphBlock paragraph => RenderParagraph(paragraph, options),
        QuoteBlock quote => RenderQuote(quote, options, listLevel),
        ThematicBreakBlock => RenderThematicBreak(options),
        CodeBlock code => RenderCodeBlock(code, options),
        ListBlock list => RenderList(list, options, listLevel),
        MdTable table => RenderTable(table, options),
        HtmlBlock html => RenderHtmlBlock(html, options),
        _ => null
    };

    private static Paragraph RenderHeading(HeadingBlock heading, MarkdownPreviewOptions options)
    {
        var (size, margin, addBottomBorder) = heading.Level switch
        {
            1 => (options.Scale(28), new Thickness(0, 16, 0, 8), true),
            2 => (options.Scale(23), new Thickness(0, 14, 0, 6), true),
            3 => (options.Scale(19), new Thickness(0, 12, 0, 4), false),
            4 => (options.Scale(16), new Thickness(0, 10, 0, 4), false),
            5 => (options.Scale(14), new Thickness(0, 8, 0, 4), false),
            _ => (options.Scale(12.5), new Thickness(0, 6, 0, 4), false)
        };

        var paragraph = new Paragraph
        {
            FontSize = size,
            FontWeight = FontWeights.Bold,
            Margin = margin,
            Foreground = heading.Level == 6 ? options.MutedTextBrush : options.TextBrush
        };

        if (addBottomBorder)
        {
            paragraph.BorderBrush = options.BorderBrush;
            paragraph.BorderThickness = new Thickness(0, 0, 0, 1);
            paragraph.Padding = new Thickness(0, 0, 0, 4);
        }

        if (heading.Inline is not null)
            RenderInlines(heading.Inline, paragraph.Inlines, options, isHeader: true);

        return paragraph;
    }

    private static Paragraph RenderParagraph(ParagraphBlock paragraphBlock, MarkdownPreviewOptions options)
    {
        var paragraph = new Paragraph
        {
            Margin = new Thickness(0, 4, 0, 8)
        };

        if (paragraphBlock.Inline is not null)
            RenderInlines(paragraphBlock.Inline, paragraph.Inlines, options);

        return paragraph;
    }

    private static Section RenderQuote(QuoteBlock quote, MarkdownPreviewOptions options, int listLevel)
    {
        var section = new Section
        {
            BorderThickness = new Thickness(3, 0, 0, 0),
            BorderBrush = options.AccentBrush,
            Padding = new Thickness(12, 2, 0, 2),
            Margin = new Thickness(8, 6, 8, 8),
            Foreground = options.MutedTextBrush
        };

        RenderContainer(quote, section.Blocks, options, listLevel);
        return section;
    }

    private static Paragraph RenderThematicBreak(MarkdownPreviewOptions options) => new()
    {
        BorderBrush = options.BorderBrush,
        BorderThickness = new Thickness(0, 0, 0, 1),
        Margin = new Thickness(0, 12, 0, 12)
    };

    private static Paragraph RenderCodeBlock(CodeBlock codeBlock, MarkdownPreviewOptions options)
    {
        var sb = new StringBuilder();
        foreach (var line in codeBlock.Lines)
        {
            sb.AppendLine(DocumentSafety.ClipPreviewLine(line.ToString() ?? string.Empty));
        }
        var codeText = sb.ToString().TrimEnd('\r', '\n');

        return new Paragraph(new Run(codeText)
        {
            FontFamily = new FontFamily("Consolas"),
            Foreground = options.AccentBrush
        })
        {
            FontSize = options.Scale(13.5),
            Background = options.PanelHeaderBrush,
            BorderBrush = options.BorderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 6, 0, 10)
        };
    }

    private static List RenderList(ListBlock listBlock, MarkdownPreviewOptions options, int listLevel)
    {
        var list = new List
        {
            Margin = new Thickness(listLevel > 0 ? 12 : 8, 2, 0, 6),
            Padding = new Thickness(18, 0, 0, 0)
        };

        if (listBlock.IsOrdered)
        {
            list.MarkerStyle = TextMarkerStyle.Decimal;
            if (int.TryParse(listBlock.OrderedStart, out var start))
                list.StartIndex = start;
        }
        else
        {
            list.MarkerStyle = (listLevel % 3) switch
            {
                0 => TextMarkerStyle.Disc,
                1 => TextMarkerStyle.Circle,
                _ => TextMarkerStyle.Square
            };
        }

        var listItems = listBlock.OfType<ListItemBlock>().ToList();
        var hasTaskItems = listItems.Any(item => HasTaskList(item, out _));
        // WPF's List.MarkerStyle applies to *all* items, unlike GFM task markers.
        // Render every marker explicitly when mixing normal items and checkboxes.
        if (hasTaskItems)
            list.MarkerStyle = TextMarkerStyle.None;

        var orderedStart = listBlock.IsOrdered && int.TryParse(listBlock.OrderedStart, out var startIndex)
            ? startIndex : 1;
        for (var itemIndex = 0; itemIndex < listItems.Count; itemIndex++)
        {
            var listItemBlock = listItems[itemIndex];
            var listItem = new ListItem();
            var isTaskItem = HasTaskList(listItemBlock, out var isChecked);

            var firstBlock = true;
            foreach (var childBlock in listItemBlock)
            {
                if (firstBlock && childBlock is ParagraphBlock firstPara)
                {
                    var p = isTaskItem ? RenderTaskParagraph(firstPara, isChecked, options)
                        : RenderParagraph(firstPara, options);
                    p.Margin = new Thickness(0, 2, 0, 2);
                    if (hasTaskItems && !isTaskItem)
                    {
                        var bullet = (listLevel % 3) switch { 0 => "• ", 1 => "◦ ", _ => "▪ " };
                        var marker = listBlock.IsOrdered ? $"{orderedStart + itemIndex}. " : bullet;
                        var markerRun = new Run(marker)
                        {
                            Foreground = options.AccentBrush,
                            FontWeight = FontWeights.Bold
                        };
                        if (p.Inlines.FirstInline is { } firstInline)
                            p.Inlines.InsertBefore(firstInline, markerRun);
                        else
                            p.Inlines.Add(markerRun);
                    }
                    listItem.Blocks.Add(p);
                }
                else
                {
                    var renderedChild = RenderBlock(childBlock, options, listLevel + 1);
                    if (renderedChild is not null)
                    {
                        if (renderedChild is Paragraph p)
                            p.Margin = new Thickness(0, 2, 0, 2);
                        listItem.Blocks.Add(renderedChild);
                    }
                }
                firstBlock = false;
            }

            list.ListItems.Add(listItem);
        }

        return list;
    }

    private static bool HasTaskList(ListItemBlock item, out bool isChecked)
    {
        isChecked = false;
        foreach (var block in item)
        {
            if (block is ParagraphBlock para && para.Inline is not null)
            {
                foreach (var inline in para.Inline)
                {
                    if (inline is TaskList taskList)
                    {
                        isChecked = taskList.Checked;
                        return true;
                    }
                }
            }
        }
        return false;
    }

    private static Paragraph RenderTaskParagraph(ParagraphBlock para, bool isChecked, MarkdownPreviewOptions options)
    {
        var paragraph = new Paragraph
        {
            Margin = new Thickness(0, 2, 0, 2)
        };

        var boxGlyph = isChecked ? "☑ " : "☐ ";
        paragraph.Inlines.Add(new Run(boxGlyph)
        {
            FontFamily = new FontFamily("Segoe UI Symbol, Segoe UI, Consolas"),
            Foreground = isChecked ? options.AccentBrush : options.MutedTextBrush,
            FontWeight = isChecked ? FontWeights.Bold : FontWeights.Normal
        });

        if (para.Inline is not null)
        {
            foreach (var inline in para.Inline)
            {
                if (inline is not TaskList)
                    RenderInline(inline, paragraph.Inlines, options);
            }
        }

        return paragraph;
    }

    private static WpfTable RenderTable(MdTable tableBlock, MarkdownPreviewOptions options)
    {
        var table = new WpfTable
        {
            CellSpacing = 0,
            Margin = new Thickness(0, 8, 0, 12),
            BorderThickness = new Thickness(1),
            BorderBrush = options.BorderBrush
        };

        var columnCount = tableBlock.ColumnDefinitions.Count;
        for (var i = 0; i < columnCount; i++)
        {
            table.Columns.Add(new TableColumn());
        }

        var rowGroup = new TableRowGroup();
        table.RowGroups.Add(rowGroup);

        var rowIndex = 0;
        foreach (var block in tableBlock)
        {
            if (block is not MdTableRow tableRow)
                continue;

            var wpfRow = new WpfTableRow();
            if (tableRow.IsHeader)
            {
                wpfRow.Background = options.PanelHeaderBrush;
            }
            else if (rowIndex % 2 == 1)
            {
                wpfRow.Background = options.PanelHeaderBrush;
            }

            var colIndex = 0;
            foreach (var cellBlock in tableRow)
            {
                if (cellBlock is not MdTableCell tableCell)
                    continue;

                var align = TextAlignment.Left;
                if (colIndex < tableBlock.ColumnDefinitions.Count)
                {
                    align = tableBlock.ColumnDefinitions[colIndex].Alignment switch
                    {
                        TableColumnAlign.Center => TextAlignment.Center,
                        TableColumnAlign.Right => TextAlignment.Right,
                        _ => TextAlignment.Left
                    };
                }

                var wpfCell = new WpfTableCell
                {
                    BorderThickness = new Thickness(1),
                    BorderBrush = options.BorderBrush,
                    Padding = new Thickness(10, 6, 10, 6),
                    TextAlignment = align
                };

                if (tableRow.IsHeader)
                    wpfCell.FontWeight = FontWeights.Bold;

                foreach (var child in tableCell)
                {
                    if (child is ParagraphBlock cellPara)
                    {
                        var p = new Paragraph
                        {
                            Margin = new Thickness(0, 2, 0, 2),
                            TextAlignment = align
                        };
                        if (cellPara.Inline is not null)
                            RenderInlines(cellPara.Inline, p.Inlines, options, isHeader: tableRow.IsHeader);
                        wpfCell.Blocks.Add(p);
                    }
                    else
                    {
                        var rendered = RenderBlock(child, options, 0);
                        if (rendered is not null)
                            wpfCell.Blocks.Add(rendered);
                    }
                }

                wpfRow.Cells.Add(wpfCell);
                colIndex++;
            }

            rowGroup.Rows.Add(wpfRow);
            if (!tableRow.IsHeader)
                rowIndex++;
        }

        return table;
    }

    private static WpfBlock? RenderHtmlBlock(HtmlBlock htmlBlock, MarkdownPreviewOptions options)
    {
        var text = htmlBlock.Lines.ToString().Trim();
        if (string.IsNullOrWhiteSpace(text))
            return null;

        if (text.StartsWith("<script", StringComparison.OrdinalIgnoreCase))
            return null;

        return new Paragraph(new Run(text)
        {
            FontFamily = new FontFamily("Consolas"),
            Foreground = options.MutedTextBrush
        })
        {
            FontSize = options.Scale(12),
            Margin = new Thickness(0, 2, 0, 4)
        };
    }

    private static void RenderInlines(ContainerInline container, InlineCollection target, MarkdownPreviewOptions options, bool isHeader = false)
    {
        // Markdig represents HTML tags as separate HtmlInline nodes. Reassemble only
        // our toolbar's restricted font-size/font-family spans; never interpret scripts.
        var parents = new Stack<InlineCollection>();
        var destination = target;
        foreach (var inline in container)
        {
            if (inline is HtmlInline html)
            {
                if (TryCreateStyledSpan(html.Tag, options, out var styled))
                {
                    destination.Add(styled);
                    parents.Push(destination);
                    destination = styled.Inlines;
                    continue;
                }
                if (string.Equals(html.Tag.Trim(), "</span>", StringComparison.OrdinalIgnoreCase) &&
                    parents.Count > 0)
                {
                    destination = parents.Pop();
                    continue;
                }
            }
            RenderInline(inline, destination, options, isHeader);
        }
    }

    private static bool TryCreateStyledSpan(string tag, MarkdownPreviewOptions options, out Span span)
    {
        span = new Span();
        var match = StyledSpanOpeningPattern.Match(tag);
        if (!match.Success)
            return false;

        var property = match.Groups["property"].Value;
        var value = match.Groups["value"].Value.Trim();
        if (property.Equals("font-size", StringComparison.OrdinalIgnoreCase))
        {
            if (!double.TryParse(value.Replace("px", string.Empty, StringComparison.OrdinalIgnoreCase),
                    NumberStyles.Number, CultureInfo.InvariantCulture, out var size) ||
                !double.IsFinite(size))
                return false;
            span.FontSize = Math.Clamp(options.Scale(size), 6, 144);
            return true;
        }

        if (value.Length > 100 || !Fonts.SystemFontFamilies.Any(font =>
                font.Source.Equals(value, StringComparison.OrdinalIgnoreCase)))
            return false;
        span.FontFamily = new FontFamily(value);
        return true;
    }

    private static void RenderInline(MdInline inline, InlineCollection target, MarkdownPreviewOptions options, bool isHeader = false)
    {
        switch (inline)
        {
            case LiteralInline literal:
                RenderLiteral(literal.Content.ToString(), target, options, isHeader);
                break;

            case CodeInline code:
                target.Add(new Run(code.Content)
                {
                    FontFamily = new FontFamily("Consolas"),
                    Background = options.PanelHeaderBrush,
                    Foreground = options.AccentBrush
                });
                break;

            case EmphasisInline emphasis:
                RenderEmphasis(emphasis, target, options, isHeader);
                break;

            case LinkInline link:
                RenderLink(link, target, options);
                break;

            case AutolinkInline autolink:
                RenderAutolink(autolink, target, options);
                break;

            case LineBreakInline lineBreak:
                if (lineBreak.IsHard)
                    target.Add(new LineBreak());
                else
                    target.Add(new Run(" "));
                break;

            case TaskList taskList:
                target.Add(new Run(taskList.Checked ? "☑ " : "☐ ")
                {
                    Foreground = taskList.Checked ? options.AccentBrush : options.MutedTextBrush
                });
                break;

            case HtmlInline html:
                RenderHtmlInline(html, target, options);
                break;

            case ContainerInline container:
                RenderInlines(container, target, options, isHeader);
                break;

            default:
                break;
        }
    }

    private static void RenderLiteral(string text, InlineCollection target, MarkdownPreviewOptions options, bool isHeader)
    {
        if (text.Contains("<span style=", StringComparison.OrdinalIgnoreCase))
        {
            var match = StyledSpanPattern.Match(text);
            if (match.Success)
            {
                var run = new Run(match.Groups["text"].Value);
                var property = match.Groups["property"].Value;
                var value = match.Groups["value"].Value;
                if (property.Equals("font-family", StringComparison.OrdinalIgnoreCase))
                {
                    run.FontFamily = new FontFamily(value);
                }
                else if (property.Equals("font-size", StringComparison.OrdinalIgnoreCase) &&
                         double.TryParse(value.Replace("px", string.Empty, StringComparison.OrdinalIgnoreCase),
                             NumberStyles.Number, CultureInfo.InvariantCulture, out var size))
                {
                    run.FontSize = Math.Clamp(options.Scale(size), 6, 144);
                }
                if (isHeader)
                    run.FontWeight = FontWeights.Bold;
                target.Add(run);
                return;
            }
        }

        var r = new Run(text);
        if (isHeader)
            r.FontWeight = FontWeights.Bold;
        target.Add(r);
    }

    private static void RenderEmphasis(EmphasisInline emphasis, InlineCollection target, MarkdownPreviewOptions options, bool isHeader)
    {
        Span wrapperSpan;
        if (emphasis.DelimiterChar == '~')
        {
            wrapperSpan = new Span { TextDecorations = TextDecorations.Strikethrough };
        }
        else if (emphasis.DelimiterCount >= 3)
        {
            var bold = new Bold();
            var italic = new Italic();
            bold.Inlines.Add(italic);
            RenderInlines(emphasis, italic.Inlines, options, isHeader);
            target.Add(bold);
            return;
        }
        else if (emphasis.DelimiterCount == 2)
        {
            wrapperSpan = new Bold();
        }
        else
        {
            wrapperSpan = new Italic();
        }

        RenderInlines(emphasis, wrapperSpan.Inlines, options, isHeader);
        target.Add(wrapperSpan);
    }

    private static void RenderLink(LinkInline link, InlineCollection target, MarkdownPreviewOptions options)
    {
        if (link.IsImage)
        {
            // Markdown Image: ![alt](url)
            // Defensive guarantee: NEVER automatically download remote images over the network.
            var altBuilder = new StringBuilder();
            foreach (var child in link)
            {
                if (child is LiteralInline literal)
                    altBuilder.Append(literal.Content.ToString());
            }
            var altText = altBuilder.ToString().Trim();
            var label = string.IsNullOrWhiteSpace(altText) ? "Image" : altText;

            var imageBadge = new Span
            {
                Foreground = options.MutedTextBrush,
                FontStyle = FontStyles.Italic
            };
            imageBadge.Inlines.Add(new Run($"🖼 [{label}]"));
            if (!string.IsNullOrWhiteSpace(link.Url))
            {
                imageBadge.ToolTip = $"Image: {link.Url}" + (string.IsNullOrWhiteSpace(link.Title) ? "" : $" ({link.Title})");
            }
            target.Add(imageBadge);
            return;
        }

        var url = link.Url;
        if (!string.IsNullOrWhiteSpace(url) &&
            Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            var hyperlink = new Hyperlink
            {
                NavigateUri = uri,
                Foreground = options.AccentBrush
            };

            if (!string.IsNullOrWhiteSpace(link.Title))
                hyperlink.ToolTip = link.Title;

            if (options.HyperlinkNavigateHandler is not null)
                hyperlink.RequestNavigate += options.HyperlinkNavigateHandler;

            RenderInlines(link, hyperlink.Inlines, options);
            target.Add(hyperlink);
        }
        else
        {
            var safeSpan = new Span();
            RenderInlines(link, safeSpan.Inlines, options);
            if (!string.IsNullOrWhiteSpace(url))
                safeSpan.ToolTip = $"Link: {url}";
            target.Add(safeSpan);
        }
    }

    private static void RenderAutolink(AutolinkInline autolink, InlineCollection target, MarkdownPreviewOptions options)
    {
        var url = autolink.Url;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            var hyperlink = new Hyperlink(new Run(url))
            {
                NavigateUri = uri,
                Foreground = options.AccentBrush
            };

            if (options.HyperlinkNavigateHandler is not null)
                hyperlink.RequestNavigate += options.HyperlinkNavigateHandler;

            target.Add(hyperlink);
        }
        else
        {
            target.Add(new Run(url));
        }
    }

    private static void RenderHtmlInline(HtmlInline html, InlineCollection target, MarkdownPreviewOptions options)
    {
        var tag = html.Tag;
        if (tag.Equals("<br>", StringComparison.OrdinalIgnoreCase) ||
            tag.Equals("<br/>", StringComparison.OrdinalIgnoreCase) ||
            tag.Equals("<br />", StringComparison.OrdinalIgnoreCase))
        {
            target.Add(new LineBreak());
            return;
        }

        if (tag.StartsWith("<span style=", StringComparison.OrdinalIgnoreCase))
            return;

        if (tag.StartsWith("<script", StringComparison.OrdinalIgnoreCase) ||
            tag.StartsWith("</script", StringComparison.OrdinalIgnoreCase))
            return;

        target.Add(new Run(tag) { Foreground = options.MutedTextBrush });
    }
}
