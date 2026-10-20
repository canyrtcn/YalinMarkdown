using System.Reflection;
using Microsoft.Win32;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Diagnostics;
using System.Windows.Navigation;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Threading;

namespace YalinMarkdown;

public partial class MainWindow : Window
{
    private readonly List<DocumentTab> documents = [];
    private readonly HashSet<string> loadingFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly LiveTextMetrics liveMetrics = new();
    private readonly DispatcherTimer recoveryTimer = new() { Interval = TimeSpan.FromSeconds(60) };
    // Coalesce rapid keystrokes before constructing an expensive WPF FlowDocument.
    private readonly DispatcherTimer previewRefreshTimer = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromMilliseconds(120)
    };
    private DocumentTab? activeDocument;
    private bool isDark = true;
    private bool suppressChanges;
    private int editorZoom = 100;
    private int previewZoom = 100;
    private int baseFontSize = 15;
    private ScrollViewer? previewScrollViewer;
    private ScrollViewer? editorScrollViewer;
    private ScrollViewer? hookedEditorScrollViewer;
    private ScrollViewer? hookedPreviewScrollViewer;
    private double previewScrollTarget;
    private double editorScrollTarget;
    private bool isPreviewSmoothScrolling;
    private bool isEditorSmoothScrolling;
    private bool syncingScrollControls;
    private bool suppressComboChanges;
    private bool isToolbarVisible = true;
    private bool isAgentToolsVisible;
    private string? workspaceRoot;
    private Point tabDragStart;
    private DocumentTab? draggedDocument;
    private string viewMode = "both";
    private string currentLanguage = "en";
    private Translation currentTranslation = Localization.Get("en");
    private Translation T => currentTranslation;
    private AgentPanelTranslation AgentUi => AgentPanelLocalization.Get(currentLanguage);
    private int activeAnalysisGeneration;
    private int workspaceAnalysisGeneration;
    private int skillMatchGeneration;
    private int previewGeneration;
    private static readonly string AppDataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YalinMarkdown");
    private static readonly string SessionPath = Path.Combine(AppDataDirectory, "session.json");
    private static readonly string LegacySessionPath = Path.Combine(AppContext.BaseDirectory, "session.json");
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);
    private const int DwmUseImmersiveDarkMode = 20;

    public MainWindow()
    {
        InitializeComponent();
        ApplyTheme();
        suppressComboChanges = true;
        LanguageCombo.ItemsSource = Localization.Languages;
        LanguageCombo.SelectedItem = Localization.Languages[0];
        suppressComboChanges = false;

        InitializeLocalizedControls();
        InitializeVersionDisplay();
        recoveryTimer.Tick += (_, _) => SaveSession();
        previewRefreshTimer.Tick += (_, _) =>
        {
            previewRefreshTimer.Stop();
            RenderPreview(previewGeneration);
        };

        CommandBindings.Add(new CommandBinding(ApplicationCommands.New, (_, _) => NewDocument()));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Open, (_, _) => OpenDocument()));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Save, (_, _) => SaveDocument(false)));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.SaveAs, (_, _) => SaveDocument(true)));
        CommandBindings.Add(new CommandBinding(EditingCommands.ToggleBold, (_, _) => MakeBold()));

        if (!RestoreSession())
        {
            CreateDocument(
                "# YalinMarkdown\n\nEdit your Markdown file here.\n\n- Fast\n- Simple\n- Live preview\n\n**Start writing.**",
                false);
        }

        ApplyLanguage();
        ApplyViewMode(viewMode);
        ApplyToolbarVisibility();
        ApplyAgentToolsVisibility();
        Dispatcher.BeginInvoke(InitializeOverlayScrollbars, DispatcherPriority.Loaded);
        if (StatusText.Text == "Hazır" || StatusText.Text == "Ready")
            StatusText.Text = T.Ready;
        recoveryTimer.Start();
    }

    private void InitializeVersionDisplay()
    {
        if (AppVersionText is not null)
        {
            var version = GetAppVersion();
            AppVersionText.Text = version;
            AppVersionText.ToolTip = $"YalinMarkdown {version}";
        }
    }

    internal static string GetAppVersion()
    {
        var assembly = typeof(MainWindow).Assembly;
        var infoVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(infoVersion))
        {
            var plusIndex = infoVersion.IndexOf('+');
            var clean = plusIndex >= 0 ? infoVersion[..plusIndex] : infoVersion;
            return clean.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? clean : $"v{clean}";
        }
        var version = assembly.GetName().Version;
        return version is not null ? $"v{version.Major}.{version.Minor}.{version.Build}" : "v0.1.1";
    }

    private void InitializeLocalizedControls()
    {
        suppressComboChanges = true;
        FontCombo.ItemsSource = new[] { T.Select }.Concat(Fonts.SystemFontFamilies
            .Select(font => font.Source)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(name => name)
            .ToList()).ToList();
        FontCombo.SelectedIndex = 0;
        FontSizeCombo.ItemsSource = new object[] { T.ChooseSize, 8, 9, 10, 11, 12, 13, 14, 15, 16, 18, 20, 22, 24, 28, 32, 36, 40, 48, 64, 72 };
        FontSizeCombo.SelectedIndex = 0;
        FormatCombo.ItemsSource = GetFormatOptions();
        FormatCombo.SelectedIndex = 0;
        suppressComboChanges = false;
    }

    private FormatOption[] GetFormatOptions() =>
    [
        new("none", $"{T.Format}..."),
        new("bold", T.Bold),
        new("italic", T.Italic),
        new("strike", T.Strike),
        new("inlineCode", T.InlineCode),
        new("heading1", T.Heading1),
        new("heading2", T.Heading2),
        new("heading3", T.Heading3),
        new("quote", T.Quote),
        new("bulletList", T.BulletList),
        new("numberedList", T.NumberedList),
        new("codeBlock", T.CodeBlock),
        new("horizontalRule", T.HorizontalRule)
    ];

    private void ApplyLanguage()
    {
        suppressComboChanges = true;
        LanguageCombo.SelectedItem = Localization.Languages.First(language => language.Code == currentLanguage);
        suppressComboChanges = false;

        NewButton.Content = T.New;
        var newTypeLabels = GetNewTypeLabels();
        NewMarkdownMenuItem.Header = newTypeLabels.Markdown;
        NewTextMenuItem.Header = newTypeLabels.Text;
        OpenButton.Content = T.Open;
        SaveButton.Content = T.Save;
        SaveAsButton.Content = T.SaveAs;
        UndoButton.Content = T.Undo;
        UndoButton.ToolTip = T.UndoTip;
        LanguageLabel.Text = AgentUi.Language;
        AgentToolsButton.Content = AgentUi.Tools;
        AgentToolsHeaderText.Text = AgentUi.Tools.ToUpper(CultureInfo.CurrentCulture);
        ChooseWorkspaceButton.Content = AgentUi.ChooseWorkspace;
        AnalyzeActiveButton.Content = AgentUi.AnalyzeActive;
        AnalyzeWorkspaceButton.Content = AgentUi.AnalyzeWorkspace;
        AgentOverviewTab.Header = AgentUi.Overview;
        AgentFindingsTab.Header = AgentUi.Findings;
        AgentSectionsTab.Header = AgentUi.Sections;
        AgentFilesTab.Header = AgentUi.Files;
        AgentMergedContextTab.Header = AgentUi.MergedContext;
        AgentSkillMatchTab.Header = AgentUi.SkillMatch;
        SimulateSkillTriggerButton.Content = AgentUi.Simulate;
        SkillTaskText.ToolTip = AgentUi.ExampleTask;
        if (string.IsNullOrWhiteSpace(workspaceRoot))
            WorkspacePathText.Text = AgentUi.NoWorkspace;
        FormatLabel.Text = T.Format;
        FontLabel.Text = T.Font;
        SizeLabel.Text = T.Size;
        EditorHeaderText.Text = T.Editor.ToUpper(CultureInfo.CurrentCulture);
        PreviewHeaderText.Text = T.Preview.ToUpper(CultureInfo.CurrentCulture);
        EditorVisibilityButton.Content = T.Editor;
        PreviewVisibilityButton.Content = T.Preview;
        FontSizeCombo.ToolTip = T.ChooseSize;
        UpdateToolbarVisibilityButton();
        InitializeLocalizedControls();
        ApplyTheme();
        UpdateTitle();
        UpdateCounts();
        foreach (var document in documents)
            UpdateTabHeader(document);
    }

    private (string Markdown, string Text) GetNewTypeLabels() => currentLanguage switch
    {
        "tr" => ("Markdown belgesi (.md)", "Metin belgesi (.txt)"),
        "de" => ("Markdown-Dokument (.md)", "Textdokument (.txt)"),
        "fr" => ("Document Markdown (.md)", "Document texte (.txt)"),
        "es" => ("Documento Markdown (.md)", "Documento de texto (.txt)"),
        "it" => ("Documento Markdown (.md)", "Documento di testo (.txt)"),
        "zh" => ("Markdown 文档 (.md)", "文本文档 (.txt)"),
        "ja" => ("Markdown 文書 (.md)", "テキスト文書 (.txt)"),
        "ko" => ("Markdown 문서 (.md)", "텍스트 문서 (.txt)"),
        "ru" => ("Документ Markdown (.md)", "Текстовый документ (.txt)"),
        _ => ("Markdown document (.md)", "Text document (.txt)")
    };

    private (string Hide, string Show) GetToolbarVisibilityLabels() => currentLanguage switch
    {
        "tr" => ("Araç Çubuğunu Gizle", "Araç Çubuğunu Göster"),
        "de" => ("Symbolleiste Ausblenden", "Symbolleiste Anzeigen"),
        "fr" => ("Masquer la Barre d’Outils", "Afficher la Barre d’Outils"),
        "es" => ("Ocultar Barra de Herramientas", "Mostrar Barra de Herramientas"),
        "it" => ("Nascondi Barra degli Strumenti", "Mostra Barra degli Strumenti"),
        "zh" => ("隐藏工具栏", "显示工具栏"),
        "ja" => ("ツールバーを隠す", "ツールバーを表示"),
        "ko" => ("도구 모음 숨기기", "도구 모음 표시"),
        "ru" => ("Скрыть Панель Инструментов", "Показать Панель Инструментов"),
        _ => ("Hide Toolbar", "Show Toolbar")
    };

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressComboChanges || LanguageCombo.SelectedItem is not LanguageOption language)
            return;

        currentLanguage = language.Code;
        currentTranslation = Localization.Get(currentLanguage);
        ApplyLanguage();
        SaveSession();
    }

    private void ApplyTheme()
    {
        Resources["WindowBrush"] = Brush(isDark ? "#101112" : "#F1F2F3");
        Resources["HeaderBrush"] = Brush(isDark ? "#0B0C0D" : "#FFFFFF");
        Resources["ToolbarBrush"] = Brush(isDark ? "#141617" : "#F7F8F8");
        Resources["PanelHeaderBrush"] = Brush(isDark ? "#181A1B" : "#ECEEEF");
        Resources["EditorBrush"] = Brush(isDark ? "#111314" : "#FFFFFF");
        Resources["PreviewBrush"] = Brush(isDark ? "#151718" : "#FAFAFA");
        Resources["ControlBrush"] = Brush(isDark ? "#181A1B" : "#FFFFFF");
        Resources["TextBrush"] = Brush(isDark ? "#E7E9E8" : "#151817");
        Resources["MutedTextBrush"] = Brush(isDark ? "#898F8C" : "#626865");
        Resources["BorderBrush"] = Brush(isDark ? "#2B302D" : "#D2D6D4");
        Resources["AccentBrush"] = Brush(isDark ? "#00E676" : "#056233");
        Resources["AccentSoftBrush"] = Brush(isDark ? "#244C35" : "#B8DCC6");
        Resources["ScrollTrackBrush"] = Brush(isDark ? "#0B0C0D" : "#E2E5E3");
        Resources["ScrollThumbBrush"] = Brush(isDark ? "#89928D" : "#69716D");
        ApplyNativeTitleBarTheme();

        if (ThemeButton is not null)
            ThemeButton.Content = isDark ? T.LightTheme : T.DarkTheme;
        if (EditorVisibilityButton is not null && PreviewVisibilityButton is not null)
            UpdateVisibilityButtons(
                EditorPanel.Visibility == Visibility.Visible,
                PreviewPanel.Visibility == Visibility.Visible);
    }

    private static SolidColorBrush Brush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));

    private void Window_SourceInitialized(object? sender, EventArgs e) =>
        ApplyNativeTitleBarTheme();

    private void ApplyNativeTitleBarTheme()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
            return;

        var enabled = isDark ? 1 : 0;
        DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int attributeValue,
        int attributeSize);

    private void Editor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (suppressChanges)
            return;

        ++activeAnalysisGeneration; // Invalidate any in-flight analysis of the old content.
        if (activeDocument is not null && !activeDocument.IsDirty)
        {
            activeDocument.IsDirty = true;
            UpdateTabHeader(activeDocument);
            UpdateTitle();
        }

        // The display changes on every edit, with no 400 ms counter delay.
        // TextChanged.Changes reports UTF-16 offsets for TextBox edits.
        var current = Editor.Text;
        var changes = e.Changes;
        if (changes.Count != 1 ||
            !liveMetrics.TryApplySingleChange(current, changes.First().Offset,
                changes.First().RemovedLength, changes.First().AddedLength))
            liveMetrics.Reset(current);
        UpdateCounts();

        SchedulePreview();
    }

    private void SchedulePreview()
    {
        ++previewGeneration;
        previewRefreshTimer.Stop();
        if (PreviewPanel?.Visibility != Visibility.Visible)
            return;
        previewRefreshTimer.Start();
    }

    private void RenderPreview(int generation)
    {
        if (generation != previewGeneration || Preview is null || Editor is null)
            return;

        try
        {
            var options = new MarkdownPreviewOptions
            {
                FontFamily = Editor.FontFamily,
                BaseFontSize = baseFontSize,
                PreviewZoom = previewZoom,
                TextBrush = (Brush)Resources["TextBrush"],
                MutedTextBrush = (Brush)Resources["MutedTextBrush"],
                BorderBrush = (Brush)Resources["BorderBrush"],
                AccentBrush = (Brush)Resources["AccentBrush"],
                PanelHeaderBrush = (Brush)Resources["PanelHeaderBrush"],
                HyperlinkNavigateHandler = PreviewLink_RequestNavigate
            };

            var document = MarkdownPreviewRenderer.Render(Editor.Text, options);

            if (generation != previewGeneration)
                return;

            var previousOffset = (previewScrollViewer ?? FindVisualChild<ScrollViewer>(Preview))?.VerticalOffset ?? 0;
            if (isPreviewSmoothScrolling)
            {
                CompositionTarget.Rendering -= SmoothScrollPreview;
                isPreviewSmoothScrolling = false;
            }

            Preview.Document = document;
            previewScrollViewer = null;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                InitializeOverlayScrollbars();
                previewScrollViewer?.ScrollToVerticalOffset(previousOffset);
                SyncOverlayScrollbars();
            }), DispatcherPriority.Loaded);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"{T.PreviewFailed}: {ex.GetType().Name}";
        }
    }

    private void UpdateCounts()
    {
        var characters = liveMetrics.Characters;
        var words = liveMetrics.Words;
        var fileName = activeDocument is null ? null : GetDisplayName(activeDocument);
        var extension = fileName is null ? string.Empty : Path.GetExtension(fileName);
        var showEstimate =
            extension.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".markdown", StringComparison.OrdinalIgnoreCase);
        CountText.Text = showEstimate
            ? $"{words} {T.Words}  /  {characters} {T.Characters}  /  ~{liveMetrics.ApproximateTokens:N0} {T.Token}"
            : $"{words} {T.Words}  /  {characters} {T.Characters}";
    }

    private void UpdateTitle()
    {
        var name = activeDocument is null ? T.Untitled : GetDisplayName(activeDocument);
        var dirty = activeDocument?.IsDirty == true;
        FileNameText.Text = $"  /  {name}{(dirty ? "  *" : "")}";
        Title = $"{(dirty ? "* " : "")}{name} - {T.AppName}";
    }

    private bool ConfirmClose(DocumentTab document)
    {
        if (!document.IsDirty)
            return true;

        var result = MessageBox.Show(
            string.Format(T.UnsavedQuestion, GetDisplayName(document)),
            T.AppName,
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Cancel)
            return false;
        if (result == MessageBoxResult.Yes)
            return SaveDocument(document, false);
        return true;
    }

    private void CreateDocument(
        string content,
        bool isDirty,
        string? filePath = null,
        bool select = true,
        string defaultExtension = ".md",
        bool needsDiskLoad = false,
        Encoding? encoding = null)
    {
        var document = new DocumentTab
        {
            Content = content,
            FilePath = filePath,
            IsDirty = isDirty,
            NeedsDiskLoad = needsDiskLoad,
            TextEncoding = encoding ?? DocumentSafety.RestoreEncoding(65001, false),
            DefaultExtension = IsSupportedExtension(defaultExtension) ? defaultExtension : ".md"
        };
        documents.Add(document);

        var tab = new TabItem { Tag = document };
        document.Tab = tab;
        UpdateTabHeader(document);
        DocumentTabs.Items.Add(tab);
        if (select)
            DocumentTabs.SelectedItem = tab;
    }

    private void NewDocument(string extension = ".md")
    {
        CreateDocument(string.Empty, false, defaultExtension: extension);
        StatusText.Text = T.NewDocument;
    }

    private void OpenDocument()
    {
        var dialog = new OpenFileDialog
        {
            Filter = $"{T.SupportedFiles} (*.md;*.markdown;*.txt)|*.md;*.markdown;*.txt|{T.MarkdownFiles} (*.md;*.markdown)|*.md;*.markdown|{T.TextFiles} (*.txt)|*.txt",
            Title = T.OpenTitle,
            Multiselect = true
        };

        if (dialog.ShowDialog() != true)
            return;

        foreach (var file in dialog.FileNames)
            OpenFile(file);
    }

    private async void OpenFile(string filePath) => await OpenFileAsync(filePath);

    private async Task<DocumentTab?> OpenFileAsync(string filePath)
    {
        if (!IsSupportedFile(filePath))
            return null;

        string? fullPath = null;
        try
        {
            fullPath = Path.GetFullPath(filePath);
            var existing = documents.FirstOrDefault(document =>
                document.FilePath is not null &&
                string.Equals(Path.GetFullPath(document.FilePath), fullPath, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                DocumentTabs.SelectedItem = existing.Tab;
                if (existing.NeedsDiskLoad)
                    existing.LoadTask ??= LoadRestoredDocumentAsync(existing);
                if (existing.LoadTask is { } pendingLoad)
                    await pendingLoad;
                return existing.LoadFailed ? null : existing;
            }
            if (!loadingFiles.Add(fullPath))
                return null;

            var size = new FileInfo(fullPath).Length;
            if (size > DocumentSafety.MaxOpenFileBytes)
            {
                MessageBox.Show(
                    $"This file is larger than the {DocumentSafety.MaxOpenFileBytes / (1024 * 1024)} MiB safe editing limit. " +
                    "It was not opened to avoid an unresponsive WPF editor.",
                    T.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }

            StatusText.Text = "Opening file...";
            var (content, encoding) = await DocumentSafety.ReadDocumentAsync(fullPath);
            CreateDocument(content, false, fullPath, encoding: encoding);
            StatusText.Text = T.FileOpened;
            return activeDocument;
        }
        catch (Exception ex)
        {
            MessageBox.Show(string.Format(T.OpenFailed, ex.Message), T.Error, MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }
        finally
        {
            if (fullPath is not null)
                loadingFiles.Remove(fullPath);
        }
    }

    private static bool IsSupportedFile(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return IsSupportedExtension(extension);
    }

    private static bool IsSupportedExtension(string extension) =>
        extension.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".markdown", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".txt", StringComparison.OrdinalIgnoreCase);

    private bool SaveDocument(bool saveAs) =>
        activeDocument is not null && SaveDocument(activeDocument, saveAs);

    private bool SaveDocument(DocumentTab document, bool saveAs)
    {
        // Never overwrite a restored file with an empty placeholder during async loading.
        if (document.IsLoading || document.NeedsDiskLoad || document.LoadFailed)
        {
            MessageBox.Show("The document has not finished loading. Save was cancelled.",
                T.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        if (document == activeDocument)
            document.Content = Editor.Text;

        var file = document.FilePath;
        if (saveAs || string.IsNullOrEmpty(file))
        {
            var dialog = new SaveFileDialog
            {
                Filter = $"{T.MarkdownFiles} (*.md)|*.md|{T.MarkdownFiles} (*.markdown)|*.markdown|{T.TextFiles} (*.txt)|*.txt",
                DefaultExt = document.FilePath is null ? document.DefaultExtension : Path.GetExtension(document.FilePath),
                AddExtension = true,
                FileName = GetDisplayName(document),
                Title = saveAs ? T.SaveAsTitle : T.SaveTitle
            };
            if (dialog.ShowDialog() != true)
                return false;
            file = dialog.FileName;
        }

        try
        {
            var destination = Path.GetFullPath(file!);
            if (documents.Any(other => other != document && other.FilePath is not null &&
                    string.Equals(Path.GetFullPath(other.FilePath), destination,
                        StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("Another open tab already uses this destination. Save As a different path.",
                    T.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            DocumentSafety.AtomicWriteAllText(destination, document.Content, document.TextEncoding);
            document.FilePath = file;
            document.DefaultExtension = Path.GetExtension(file);
            document.IsDirty = false;
            StatusText.Text = T.Saved;
            UpdateTabHeader(document);
            UpdateTitle();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(string.Format(T.SaveFailed, ex.Message), T.Error, MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void UpdateTabHeader(DocumentTab document)
    {
        if (document.Tab is null)
            return;

        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new TextBlock
        {
            Text = $"{GetDisplayName(document)}{(document.IsDirty ? " *" : "")}",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Resources["TextBrush"]
        });

        var closeButton = new Button
        {
            Content = "x",
            Tag = document,
            Margin = new Thickness(10, 0, 0, 0),
            Padding = new Thickness(3, 0, 3, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = (Brush)Resources["MutedTextBrush"],
            Cursor = Cursors.Hand,
            Focusable = false
        };
        closeButton.Click += CloseTabButton_Click;
        header.Children.Add(closeButton);
        document.Tab.Header = header;
    }

    private void CloseDocument(DocumentTab document)
    {
        if (!ConfirmClose(document))
            return;

        documents.Remove(document);
        DocumentTabs.Items.Remove(document.Tab);
        if (documents.Count == 0)
            CreateDocument(string.Empty, false);
    }

    private string GetDisplayName(DocumentTab document) =>
        document.FilePath is null
            ? Path.ChangeExtension(T.Untitled, document.DefaultExtension)
            : Path.GetFileName(document.FilePath);

    private void MakeBold()
    {
        ApplyInlineFormat("**", "**", T.BoldPlaceholder);
    }

    private void ApplyInlineFormat(string before, string after, string placeholder)
    {
        var start = Editor.SelectionStart;
        var length = Editor.SelectionLength;
        var selected = Editor.SelectedText;
        var content = length > 0 ? selected : placeholder;
        var replacement = $"{before}{content}{after}";
        Editor.SelectedText = replacement;
        Editor.SelectionStart = start + before.Length;
        Editor.SelectionLength = content.Length;
        Editor.Focus();
    }

    private void ApplyLineFormat(string prefix, string placeholder)
    {
        var start = Editor.SelectionStart;
        var selected = Editor.SelectedText;
        if (string.IsNullOrEmpty(selected))
        {
            Editor.SelectedText = prefix + placeholder;
            Editor.SelectionStart = start + prefix.Length;
            Editor.SelectionLength = placeholder.Length;
        }
        else
        {
            var formatted = string.Join(Environment.NewLine,
                selected.Replace("\r\n", "\n").Split('\n').Select(line => prefix + line));
            Editor.SelectedText = formatted;
            Editor.SelectionStart = start;
            Editor.SelectionLength = formatted.Length;
        }
        Editor.Focus();
    }

    private void ApplyFormat(string format)
    {
        switch (format)
        {
            case "bold": ApplyInlineFormat("**", "**", T.BoldPlaceholder); break;
            case "italic": ApplyInlineFormat("*", "*", T.ItalicPlaceholder); break;
            case "strike": ApplyInlineFormat("~~", "~~", T.StrikePlaceholder); break;
            case "inlineCode": ApplyInlineFormat("`", "`", T.CodePlaceholder); break;
            case "heading1": ApplyLineFormat("# ", T.HeadingPlaceholder); break;
            case "heading2": ApplyLineFormat("## ", T.HeadingPlaceholder); break;
            case "heading3": ApplyLineFormat("### ", T.HeadingPlaceholder); break;
            case "quote": ApplyLineFormat("> ", T.QuotePlaceholder); break;
            case "bulletList": ApplyLineFormat("- ", T.ListPlaceholder); break;
            case "numberedList": ApplyLineFormat("1. ", T.ListPlaceholder); break;
            case "codeBlock": ApplyInlineFormat("```\n", "\n```", T.CodePlaceholder); break;
            case "horizontalRule":
                Editor.SelectedText = "\n---\n";
                Editor.Focus();
                break;
        }
    }

    private void GitHubProfileButton_Click(object sender, RoutedEventArgs e) =>
        OpenExternalLink(new Uri("https://github.com/canyrtcn"));

    private void PreviewLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        e.Handled = true;
        OpenExternalLink(e.Uri);
    }

    private void OpenExternalLink(Uri uri)
    {
        if (!uri.IsAbsoluteUri || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return;
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, T.Error, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        isDark = !isDark;
        ApplyTheme();
        foreach (var document in documents)
            UpdateTabHeader(document);
        SchedulePreview();
    }

    private void ToolbarVisibilityButton_Click(object sender, RoutedEventArgs e)
    {
        isToolbarVisible = !isToolbarVisible;
        ApplyToolbarVisibility();
    }

    private void ApplyToolbarVisibility()
    {
        ToolbarPanel.Visibility = isToolbarVisible ? Visibility.Visible : Visibility.Collapsed;
        UpdateToolbarVisibilityButton();
    }

    private void UpdateToolbarVisibilityButton()
    {
        if (ToolbarVisibilityButton is null)
            return;

        var labels = GetToolbarVisibilityLabels();
        ToolbarVisibilityButton.Content = isToolbarVisible ? labels.Hide : labels.Show;
        ToolbarVisibilityButton.ToolTip = isToolbarVisible ? labels.Hide : labels.Show;
    }

    private void AgentToolsButton_Click(object sender, RoutedEventArgs e)
    {
        isAgentToolsVisible = !isAgentToolsVisible;
        ApplyAgentToolsVisibility();
        if (isAgentToolsVisible)
            AnalyzeActiveDocument();
    }

    private void CloseAgentToolsButton_Click(object sender, RoutedEventArgs e)
    {
        isAgentToolsVisible = false;
        ApplyAgentToolsVisibility();
    }

    private void ApplyAgentToolsVisibility()
    {
        if (AgentToolsPanel is null)
            return;
        AgentToolsPanel.Visibility = isAgentToolsVisible ? Visibility.Visible : Visibility.Collapsed;
        if (AgentToolsButton is not null)
        {
            AgentToolsButton.BorderBrush = (Brush)Resources[isAgentToolsVisible ? "AccentBrush" : "BorderBrush"];
            AgentToolsButton.Foreground = (Brush)Resources[isAgentToolsVisible ? "AccentBrush" : "TextBrush"];
        }
    }

    private async void ChooseWorkspaceButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose agent workspace",
            Multiselect = false
        };
        if (dialog.ShowDialog() != true)
            return;

        workspaceRoot = dialog.FolderName;
        WorkspacePathText.Text = workspaceRoot;
        await AnalyzeWorkspaceAsync();
    }

    private void AnalyzeActiveButton_Click(object sender, RoutedEventArgs e) => AnalyzeActiveDocument();
    private async void AnalyzeWorkspaceButton_Click(object sender, RoutedEventArgs e) => await AnalyzeWorkspaceAsync();

    private async void AnalyzeActiveDocument()
    {
        if (activeDocument is null)
            return;

        var generation = ++activeAnalysisGeneration;
        var document = activeDocument;
        var text = Editor.Text;
        if (workspaceRoot is null && document.FilePath is not null)
        {
            workspaceRoot = FindLikelyWorkspaceRoot(document.FilePath);
            WorkspacePathText.Text = workspaceRoot;
        }

        var fileName = GetDisplayName(document);
        var filePath = document.FilePath;
        var root = workspaceRoot;
        AgentSummaryText.Text = AgentUi.AnalyzingDocument;
        try
        {
            var result = await Task.Run(() => AgentAnalyzer.AnalyzeDocument(fileName, text, filePath, root));
            if (generation != activeAnalysisGeneration || activeDocument != document || Editor.Text != text)
                return;
            AgentFindingsList.ItemsSource = result.Findings;
            SectionTokensList.ItemsSource = result.Sections.OrderByDescending(section => section.Tokens).ToList();
            AgentSummaryText.Text = BuildActiveAnalysisSummary(fileName, result);
        }
        catch (Exception ex)
        {
            if (generation == activeAnalysisGeneration && activeDocument == document)
                AgentSummaryText.Text = $"Analysis could not finish: {ex.Message}";
        }
    }

    private async Task AnalyzeWorkspaceAsync()
    {
        if (workspaceRoot is null || !Directory.Exists(workspaceRoot))
        {
            AgentSummaryText.Text = AgentUi.ChooseWorkspaceFirst;
            return;
        }

        var generation = ++workspaceAnalysisGeneration;
        WorkspacePathText.Text = workspaceRoot;
        AgentSummaryText.Text = AgentUi.AnalyzingWorkspace;
        var root = workspaceRoot;
        var activePath = activeDocument?.FilePath;
        try
        {
            var result = await Task.Run(() => AgentAnalyzer.AnalyzeWorkspace(root, activePath));
            if (generation != workspaceAnalysisGeneration ||
                !string.Equals(workspaceRoot, root, StringComparison.OrdinalIgnoreCase))
                return;
            AgentFindingsList.ItemsSource = result.Findings;
            AgentFilesList.ItemsSource = result.AgentFiles
                .Select(file => new AgentFileItem(file, Path.GetRelativePath(root, file)))
                .ToList();
            MergedInstructionsText.Text = result.MergedInstructions;
            AgentSummaryText.Text = BuildWorkspaceAnalysisSummary(result);
        }
        catch (Exception ex)
        {
            if (generation == workspaceAnalysisGeneration)
                AgentSummaryText.Text = $"Workspace analysis could not finish: {ex.Message}";
        }
    }

    private async void SimulateSkillTriggerButton_Click(object sender, RoutedEventArgs e)
    {
        if (workspaceRoot is null || !Directory.Exists(workspaceRoot))
        {
            AgentSummaryText.Text = AgentUi.ChooseWorkspaceFirst;
            return;
        }
        var task = SkillTaskText.Text.Trim();
        if (task.Length == 0)
            return;

        var generation = ++skillMatchGeneration;
        SkillMatchesList.ItemsSource = new[] { AgentUi.AnalyzingSkills };
        var root = workspaceRoot;
        try
        {
            var matches = await Task.Run(() => AgentAnalyzer.MatchSkills(root, task));
            if (generation != skillMatchGeneration || SkillTaskText.Text.Trim() != task ||
                !string.Equals(workspaceRoot, root, StringComparison.OrdinalIgnoreCase))
                return;
            SkillMatchesList.ItemsSource = matches.Count > 0
                ? matches
                : new List<SkillMatch> { new(string.Empty, AgentUi.NoSkillMatch, 0, string.Empty) };
        }
        catch (Exception ex)
        {
            if (generation == skillMatchGeneration)
                SkillMatchesList.ItemsSource = new[] { $"Skill analysis failed: {ex.Message}" };
        }
    }

    private void SkillMatchesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SkillMatchesList.SelectedItem is SkillMatch { FilePath.Length: > 0 } match)
            OpenFile(match.FilePath);
    }

    private static string BuildActiveAnalysisSummary(string fileName, AgentAnalysisResult result)
    {
        var critical = result.Findings.Count(finding => finding.Severity == FindingSeverity.Critical);
        var warning = result.Findings.Count(finding => finding.Severity == FindingSeverity.Warning);
        var info = result.Findings.Count(finding => finding.Severity == FindingSeverity.Info);
        var expensive = result.Sections.OrderByDescending(section => section.Tokens).Take(5);
        var builder = new StringBuilder();
        builder.AppendLine($"ACTIVE DOCUMENT · {fileName}");
        builder.AppendLine();
        builder.AppendLine($"~{result.Tokens:N0} tokens · {result.Lines:N0} lines");
        builder.AppendLine($"{critical} critical · {warning} warnings · {info} notes");
        builder.AppendLine();
        builder.AppendLine("MOST EXPENSIVE SECTIONS");
        foreach (var section in expensive)
            builder.AppendLine($"  {section}");
        if (result.Findings.Count == 0)
        {
            builder.AppendLine();
            builder.AppendLine("No obvious issues found.");
        }
        return builder.ToString();
    }

    private static string BuildWorkspaceAnalysisSummary(WorkspaceAnalysisResult result)
    {
        var critical = result.Findings.Count(finding => finding.Severity == FindingSeverity.Critical);
        var warning = result.Findings.Count(finding => finding.Severity == FindingSeverity.Warning);
        var info = result.Findings.Count(finding => finding.Severity == FindingSeverity.Info);
        return $"WORKSPACE · {result.RootPath}\r\n\r\n" +
               $"{result.AgentFiles.Count:N0} agent files · ~{result.TotalTokens:N0} total Markdown tokens\r\n" +
               $"{critical} critical · {warning} warnings · {info} notes\r\n\r\n" +
               "Use Findings to inspect risks, Files to open agent documents, and Merged Context to preview the approximate instruction context.";
    }

    private static string FindLikelyWorkspaceRoot(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath) ?? Environment.CurrentDirectory;
        for (var current = directory; current is not null; current = Path.GetDirectoryName(current))
        {
            if (Directory.Exists(Path.Combine(current, ".git")) ||
                File.Exists(Path.Combine(current, "AGENTS.md")) ||
                File.Exists(Path.Combine(current, "SOUL.md")))
                return current;
        }
        return directory;
    }

    private void AgentFilesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (AgentFilesList.SelectedItem is AgentFileItem item)
            OpenFile(item.FullPath);
    }

    private async void AgentFindingsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (AgentFindingsList.SelectedItem is not AgentFinding finding || finding.FilePath is null)
            return;

        var document = await OpenFileAsync(finding.FilePath);
        if (document is not null && document == activeDocument && finding.Line is int line)
            NavigateToLine(line);
    }

    private void NavigateToLine(int line)
    {
        var target = Math.Max(1, line);
        var index = 0;
        for (var currentLine = 1; currentLine < target && index < Editor.Text.Length; currentLine++)
        {
            var next = Editor.Text.IndexOf('\n', index);
            if (next < 0)
                break;
            index = next + 1;
        }
        Editor.SelectionStart = index;
        Editor.SelectionLength = 0;
        Editor.Focus();
        Editor.ScrollToLine(target - 1);
    }

    private void FontCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressComboChanges || activeDocument is null || FontCombo.SelectedItem is not string family ||
            family == T.Select || Editor is null)
            return;

        ApplySpanFormat($"font-family:{family.Replace("\"", string.Empty)}", T.TextPlaceholder);
        suppressComboChanges = true;
        FontCombo.SelectedIndex = 0;
        suppressComboChanges = false;
        FocusEditorAfterFormatting();
    }

    private void FontSizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ApplyFontSizeFromCombo();

    private void ApplyFontSizeFromCombo()
    {
        if (suppressComboChanges || activeDocument is null || FontSizeCombo?.SelectedItem is not int size)
            return;

        size = Math.Clamp(size, 6, 144);
        ApplySpanFormat($"font-size:{size}px", T.TextPlaceholder);
        suppressComboChanges = true;
        FontSizeCombo.IsDropDownOpen = false;
        FontSizeCombo.SelectedIndex = -1;
        FontSizeCombo.SelectedIndex = 0;
        suppressComboChanges = false;
        FocusEditorAfterFormatting();
    }

    private void ApplySpanFormat(string style, string placeholder)
    {
        var start = Editor.SelectionStart;
        var selected = Editor.SelectedText;
        var content = string.IsNullOrEmpty(selected) ? placeholder : selected;
        var before = $"<span style=\"{style}\">";
        Editor.SelectedText = $"{before}{content}</span>";
        Editor.SelectionStart = start + before.Length;
        Editor.SelectionLength = content.Length;
    }

    private void FocusEditorAfterFormatting()
    {
        Dispatcher.BeginInvoke(() =>
        {
            Keyboard.Focus(Editor);
            Editor.Focus();
            Editor.CaretIndex = Editor.SelectionStart + Editor.SelectionLength;
            Editor.SelectionLength = 0;
        }, DispatcherPriority.ApplicationIdle);
    }

    private void ChangeEditorZoom(int amount)
    {
        editorZoom = Math.Clamp(editorZoom + amount, 40, 200);
        Editor.FontSize = baseFontSize * editorZoom / 100.0;
        TextBlock.SetLineHeight(Editor, baseFontSize * 1.6 * editorZoom / 100.0);
        EditorZoomText.Text = $"%{editorZoom}";
    }

    private void ChangePreviewZoom(int amount)
    {
        previewZoom = Math.Clamp(previewZoom + amount, 40, 200);
        PreviewZoomText.Text = $"%{previewZoom}";
        SchedulePreview();
    }

    private void DocumentTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DocumentTabs.SelectedItem is not TabItem { Tag: DocumentTab document })
            return;

        if (activeDocument is not null && activeDocument != document)
            activeDocument.Content = Editor.Text;

        activeDocument = document;
        ++activeAnalysisGeneration;
        ++workspaceAnalysisGeneration;
        suppressChanges = true;
        try
        {
            Editor.Text = document.Content;
        }
        finally
        {
            suppressChanges = false;
        }
        UpdateTitle();
        liveMetrics.Reset(Editor.Text);
        UpdateCounts();
        SchedulePreview();
        Editor.Focus();
        Editor.IsReadOnly = document.NeedsDiskLoad || document.IsLoading || document.LoadFailed;
        if (document.LoadFailed)
            StatusText.Text = "This session tab could not be restored. The editor is read-only to prevent data loss.";
        if (document.NeedsDiskLoad)
            document.LoadTask ??= LoadRestoredDocumentAsync(document);
    }

    private async Task LoadRestoredDocumentAsync(DocumentTab document)
    {
        document.NeedsDiskLoad = false;
        document.IsLoading = true;
        if (document.FilePath is null)
        {
            document.IsLoading = false;
            return;
        }
        try
        {
            if (new FileInfo(document.FilePath).Length > DocumentSafety.MaxOpenFileBytes)
            {
                document.IsLoading = false;
                document.LoadFailed = true;
                if (document == activeDocument)
                    Editor.IsReadOnly = true;
                StatusText.Text = "Restored file exceeds the safe editing limit; the editor remains read-only.";
                return;
            }
            var (content, encoding) = await DocumentSafety.ReadDocumentAsync(document.FilePath);
            if (!documents.Contains(document) || document.IsDirty)
                return;
            document.Content = content;
            document.TextEncoding = encoding;
            document.IsLoading = false;
            if (document == activeDocument)
            {
                suppressChanges = true;
                try { Editor.Text = content; }
                finally { suppressChanges = false; }
                Editor.IsReadOnly = false;
                liveMetrics.Reset(Editor.Text);
                UpdateCounts();
                SchedulePreview();
            }
        }
        catch (Exception ex)
        {
            document.IsLoading = false;
            document.LoadFailed = true;
            if (document == activeDocument)
                Editor.IsReadOnly = true;
            StatusText.Text = $"Restored file could not be read: {ex.Message}";
        }
    }

    private void DocumentTabs_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        tabDragStart = e.GetPosition(DocumentTabs);
        draggedDocument = null;

        if (e.OriginalSource is not DependencyObject source ||
            FindVisualParent<Button>(source) is not null)
            return;

        if (FindVisualParent<TabItem>(source)?.Tag is DocumentTab document)
            draggedDocument = document;
    }

    private void DocumentTabs_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || draggedDocument?.Tab is null)
            return;

        var position = e.GetPosition(DocumentTabs);
        if (Math.Abs(position.X - tabDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - tabDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var data = new DataObject(typeof(DocumentTab), draggedDocument);
        DragDrop.DoDragDrop(draggedDocument.Tab, data, DragDropEffects.Move);
        draggedDocument = null;
    }

    private void DocumentTabs_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(DocumentTab))
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void DocumentTabs_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(DocumentTab)) ||
            e.Data.GetData(typeof(DocumentTab)) is not DocumentTab sourceDocument ||
            sourceDocument.Tab is null)
        {
            e.Handled = true;
            return;
        }

        var targetTab = e.OriginalSource is DependencyObject source
            ? FindVisualParent<TabItem>(source)
            : null;
        var targetDocument = targetTab?.Tag as DocumentTab;

        var sourceIndex = documents.IndexOf(sourceDocument);
        var targetIndex = targetDocument is null ? documents.Count : documents.IndexOf(targetDocument);
        if (sourceIndex < 0 || targetIndex < 0)
        {
            e.Handled = true;
            return;
        }

        if (targetTab is not null && e.GetPosition(targetTab).X > targetTab.ActualWidth / 2)
            targetIndex++;
        if (sourceIndex < targetIndex)
            targetIndex--;
        targetIndex = Math.Clamp(targetIndex, 0, documents.Count - 1);

        if (sourceIndex != targetIndex)
        {
            documents.RemoveAt(sourceIndex);
            documents.Insert(targetIndex, sourceDocument);
            DocumentTabs.Items.Remove(sourceDocument.Tab);
            DocumentTabs.Items.Insert(targetIndex, sourceDocument.Tab);
            DocumentTabs.SelectedItem = sourceDocument.Tab;
        }

        e.Handled = true;
    }

    private void CloseTabButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button { Tag: DocumentTab document })
            CloseDocument(document);
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) &&
                    ((string[])e.Data.GetData(DataFormats.FileDrop)).Any(IsSupportedFile)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return;

        foreach (var file in (string[])e.Data.GetData(DataFormats.FileDrop))
            OpenFile(file);
    }

    private void Preview_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            ChangePreviewZoom(Math.Sign(e.Delta) * 10);
            e.Handled = true;
            return;
        }

        previewScrollViewer ??= FindVisualChild<ScrollViewer>(Preview);
        if (previewScrollViewer is null)
            return;

        if (!isPreviewSmoothScrolling)
            previewScrollTarget = previewScrollViewer.VerticalOffset;

        previewScrollTarget = Math.Clamp(
            previewScrollTarget - Math.Sign(e.Delta) * 180,
            0,
            previewScrollViewer.ScrollableHeight);

        if (!isPreviewSmoothScrolling)
        {
            isPreviewSmoothScrolling = true;
            CompositionTarget.Rendering += SmoothScrollPreview;
        }
        e.Handled = true;
    }

    private void SmoothScrollPreview(object? sender, EventArgs e)
    {
        if (previewScrollViewer is null)
            return;

        var distance = previewScrollTarget - previewScrollViewer.VerticalOffset;
        if (Math.Abs(distance) < 0.75)
        {
            previewScrollViewer.ScrollToVerticalOffset(previewScrollTarget);
            SyncOverlayScrollbars();
            CompositionTarget.Rendering -= SmoothScrollPreview;
            isPreviewSmoothScrolling = false;
            return;
        }

        previewScrollViewer.ScrollToVerticalOffset(previewScrollViewer.VerticalOffset + distance * 0.28);
        SyncOverlayScrollbars();
    }

    private void Editor_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            ChangeEditorZoom(Math.Sign(e.Delta) * 10);
            e.Handled = true;
            return;
        }

        editorScrollViewer ??= FindVisualChild<ScrollViewer>(Editor);
        if (editorScrollViewer is null)
            return;

        if (!isEditorSmoothScrolling)
            editorScrollTarget = editorScrollViewer.VerticalOffset;

        editorScrollTarget = Math.Clamp(
            editorScrollTarget - Math.Sign(e.Delta) * 180,
            0,
            editorScrollViewer.ScrollableHeight);

        if (!isEditorSmoothScrolling)
        {
            isEditorSmoothScrolling = true;
            CompositionTarget.Rendering += SmoothScrollEditor;
        }
        e.Handled = true;
    }

    private void SmoothScrollEditor(object? sender, EventArgs e)
    {
        if (editorScrollViewer is null)
            return;

        var distance = editorScrollTarget - editorScrollViewer.VerticalOffset;
        if (Math.Abs(distance) < 0.75)
        {
            editorScrollViewer.ScrollToVerticalOffset(editorScrollTarget);
            SyncOverlayScrollbars();
            CompositionTarget.Rendering -= SmoothScrollEditor;
            isEditorSmoothScrolling = false;
            return;
        }

        editorScrollViewer.ScrollToVerticalOffset(editorScrollViewer.VerticalOffset + distance * 0.28);
        SyncOverlayScrollbars();
    }

    private void InitializeOverlayScrollbars()
    {
        editorScrollViewer ??= FindVisualChild<ScrollViewer>(Editor);
        previewScrollViewer ??= FindVisualChild<ScrollViewer>(Preview);
        if (editorScrollViewer is not null && !ReferenceEquals(editorScrollViewer, hookedEditorScrollViewer))
        {
            editorScrollViewer.ScrollChanged += (_, _) => SyncOverlayScrollbars();
            hookedEditorScrollViewer = editorScrollViewer;
        }
        if (previewScrollViewer is not null && !ReferenceEquals(previewScrollViewer, hookedPreviewScrollViewer))
        {
            previewScrollViewer.ScrollChanged += (_, _) => SyncOverlayScrollbars();
            hookedPreviewScrollViewer = previewScrollViewer;
        }
        SyncOverlayScrollbars();
    }

    private void SyncOverlayScrollbars()
    {
        if (syncingScrollControls)
            return;

        syncingScrollControls = true;
        if (editorScrollViewer is not null)
        {
            EditorVerticalScroll.Maximum = Math.Max(1, editorScrollViewer.ScrollableHeight);
            EditorVerticalScroll.Value = Math.Clamp(editorScrollViewer.VerticalOffset, 0, EditorVerticalScroll.Maximum);
            EditorVerticalScroll.Visibility = editorScrollViewer.ScrollableHeight > 0 ? Visibility.Visible : Visibility.Collapsed;
            EditorHorizontalScroll.Maximum = Math.Max(1, editorScrollViewer.ScrollableWidth);
            EditorHorizontalScroll.Value = Math.Clamp(editorScrollViewer.HorizontalOffset, 0, EditorHorizontalScroll.Maximum);
            EditorHorizontalScroll.Visibility = editorScrollViewer.ScrollableWidth > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        if (previewScrollViewer is not null)
        {
            PreviewVerticalScroll.Maximum = Math.Max(1, previewScrollViewer.ScrollableHeight);
            PreviewVerticalScroll.Value = Math.Clamp(previewScrollViewer.VerticalOffset, 0, PreviewVerticalScroll.Maximum);
            PreviewVerticalScroll.Visibility = previewScrollViewer.ScrollableHeight > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        syncingScrollControls = false;
    }

    private void EditorVerticalScroll_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!syncingScrollControls)
            editorScrollViewer?.ScrollToVerticalOffset(e.NewValue);
    }

    private void EditorHorizontalScroll_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!syncingScrollControls)
            editorScrollViewer?.ScrollToHorizontalOffset(e.NewValue);
    }

    private void PreviewVerticalScroll_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!syncingScrollControls)
            previewScrollViewer?.ScrollToVerticalOffset(e.NewValue);
    }

    private void FormatCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressComboChanges || FormatCombo.SelectedItem is not FormatOption format || format.Id == "none")
            return;

        ApplyFormat(format.Id);
        suppressComboChanges = true;
        FormatCombo.SelectedIndex = 0;
        suppressComboChanges = false;
    }

    private void ApplyViewMode(string selectedMode)
    {
        viewMode = NormalizeViewMode(selectedMode);
        var showEditor = viewMode != "preview";
        var showPreview = viewMode != "editor";

        EditorPanel.Visibility = showEditor ? Visibility.Visible : Visibility.Collapsed;
        PreviewPanel.Visibility = showPreview ? Visibility.Visible : Visibility.Collapsed;
        PanelSplitter.Visibility = showEditor && showPreview ? Visibility.Visible : Visibility.Collapsed;
        EditorColumn.Width = showEditor ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        SplitterColumn.Width = showEditor && showPreview ? new GridLength(5) : new GridLength(0);
        PreviewColumn.Width = showPreview ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        UpdateVisibilityButtons(showEditor, showPreview);

        if (showPreview)
            SchedulePreview();
    }

    private void UpdateVisibilityButtons(bool showEditor, bool showPreview)
    {
        EditorVisibilityButton.BorderBrush = (Brush)Resources[showEditor ? "AccentBrush" : "BorderBrush"];
        EditorVisibilityButton.Foreground = (Brush)Resources[showEditor ? "AccentBrush" : "MutedTextBrush"];
        EditorVisibilityButton.Background = (Brush)Resources[showEditor ? "AccentSoftBrush" : "ControlBrush"];
        PreviewVisibilityButton.BorderBrush = (Brush)Resources[showPreview ? "AccentBrush" : "BorderBrush"];
        PreviewVisibilityButton.Foreground = (Brush)Resources[showPreview ? "AccentBrush" : "MutedTextBrush"];
        PreviewVisibilityButton.Background = (Brush)Resources[showPreview ? "AccentSoftBrush" : "ControlBrush"];
    }

    private void EditorVisibilityButton_Click(object sender, RoutedEventArgs e)
    {
        var showEditor = EditorPanel.Visibility != Visibility.Visible;
        var showPreview = PreviewPanel.Visibility == Visibility.Visible;
        if (!showEditor && !showPreview)
            showPreview = true;

        ApplyViewMode(showEditor && showPreview ? "both" : showEditor ? "editor" : "preview");
    }

    private void PreviewVisibilityButton_Click(object sender, RoutedEventArgs e)
    {
        var showEditor = EditorPanel.Visibility == Visibility.Visible;
        var showPreview = PreviewPanel.Visibility != Visibility.Visible;
        if (!showEditor && !showPreview)
            showEditor = true;

        ApplyViewMode(showEditor && showPreview ? "both" : showEditor ? "editor" : "preview");
    }

    private static string NormalizeViewMode(string? mode) => mode switch
    {
        "editor" or "Sadece Editör" => "editor",
        "preview" or "Sadece Önizleme" => "preview",
        _ => "both"
    };

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T result)
                return result;

            var nested = FindVisualChild<T>(child);
            if (nested is not null)
                return nested;
        }

        return null;
    }

    private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
    {
        DependencyObject? current = child;
        while (current is not null)
        {
            if (current is T result)
                return result;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void NewButton_Click(object sender, RoutedEventArgs e)
    {
        NewDocumentMenu.PlacementTarget = NewButton;
        NewDocumentMenu.IsOpen = true;
    }
    private void NewMarkdownMenuItem_Click(object sender, RoutedEventArgs e) => NewDocument(".md");
    private void NewTextMenuItem_Click(object sender, RoutedEventArgs e) => NewDocument(".txt");
    private void OpenButton_Click(object sender, RoutedEventArgs e) => OpenDocument();
    private void SaveButton_Click(object sender, RoutedEventArgs e) => SaveDocument(false);
    private void SaveAsButton_Click(object sender, RoutedEventArgs e) => SaveDocument(true);
    private void UndoButton_Click(object sender, RoutedEventArgs e)
    {
        if (Editor.CanUndo)
            Editor.Undo();
        Editor.Focus();
    }
    private void EditorZoomOutButton_Click(object sender, RoutedEventArgs e) => ChangeEditorZoom(-10);
    private void EditorZoomInButton_Click(object sender, RoutedEventArgs e) => ChangeEditorZoom(10);
    private void PreviewZoomOutButton_Click(object sender, RoutedEventArgs e) => ChangePreviewZoom(-10);
    private void PreviewZoomInButton_Click(object sender, RoutedEventArgs e) => ChangePreviewZoom(10);

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        ++previewGeneration; // Cancel any pending preview work.
        previewRefreshTimer.Stop();
        recoveryTimer.Stop();
        CompositionTarget.Rendering -= SmoothScrollPreview;
        CompositionTarget.Rendering -= SmoothScrollEditor;
        SaveSession();
    }

    private void SaveSession()
    {
        try
        {
            if (activeDocument is not null)
                activeDocument.Content = Editor.Text;

            var session = new SessionState
            {
                ActiveIndex = activeDocument is null ? 0 : documents.IndexOf(activeDocument),
                IsDark = isDark,
                Language = currentLanguage,
                IsToolbarVisible = isToolbarVisible,
                IsAgentToolsVisible = isAgentToolsVisible,
                WorkspaceRoot = workspaceRoot,
                FontFamily = "Consolas",
                BaseFontSize = baseFontSize,
                EditorZoom = editorZoom,
                PreviewZoom = previewZoom,
                ViewMode = viewMode,
                Documents = documents.Select(document => new SessionDocument
                {
                    FilePath = document.FilePath,
                    // Clean named files are reloaded from disk on the next launch.
                    // Avoid storing their potentially large/private content in a session JSON.
                    Content = document.IsDirty || document.FilePath is null ||
                              !File.Exists(document.FilePath) ? document.Content : string.Empty,
                    IsDirty = document.IsDirty,
                    EncodingCodePage = document.TextEncoding.CodePage,
                    EncodingHasBom = document.TextEncoding.GetPreamble().Length > 0,
                    DefaultExtension = document.DefaultExtension
                }).ToList()
            };

            var json = JsonSerializer.Serialize(session);
            TryWriteSession(SessionPath, json);
        }
        catch
        {
            // Session persistence must never prevent the app from closing.
        }
    }

    private static bool TryWriteSession(string path, string json)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
            File.Move(temporaryPath, path, true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool RestoreSession()
    {
        try
        {
            var path = File.Exists(SessionPath) ? SessionPath : LegacySessionPath;
            if (!File.Exists(path))
                return false;

            var session = JsonSerializer.Deserialize<SessionState>(File.ReadAllText(path));
            if (session?.Documents is not { Count: > 0 })
                return false;

            isDark = session.IsDark;
            isToolbarVisible = session.IsToolbarVisible;
            isAgentToolsVisible = session.IsAgentToolsVisible;
            workspaceRoot = Directory.Exists(session.WorkspaceRoot) ? session.WorkspaceRoot : null;
            currentLanguage = Localization.Languages.Any(language => language.Code == session.Language)
                ? session.Language
                : "en";
            currentTranslation = Localization.Get(currentLanguage);
            ApplyLanguage();
            ApplyTheme();
            FontCombo.SelectedIndex = 0;
            baseFontSize = 15;
            FontSizeCombo.SelectedIndex = 0;
            editorZoom = session.EditorZoom;
            previewZoom = session.PreviewZoom;
            ChangeEditorZoom(0);
            ChangePreviewZoom(0);
            ApplyViewMode(NormalizeViewMode(session.ViewMode));
            ApplyToolbarVisibility();
            ApplyAgentToolsVisibility();
            WorkspacePathText.Text = workspaceRoot ?? AgentUi.NoWorkspace;

            foreach (var document in session.Documents)
            {
                var diskAvailable = !document.IsDirty &&
                                    !string.IsNullOrWhiteSpace(document.FilePath) &&
                                    File.Exists(document.FilePath);
                var missingCleanFile = !document.IsDirty &&
                                       !string.IsNullOrWhiteSpace(document.FilePath) &&
                                       !File.Exists(document.FilePath) &&
                                       string.IsNullOrEmpty(document.Content);
                CreateDocument(diskAvailable ? string.Empty : document.Content,
                    document.IsDirty || (!diskAvailable && !missingCleanFile && document.FilePath is not null),
                    document.FilePath, false, document.DefaultExtension,
                    needsDiskLoad: diskAvailable,
                    encoding: DocumentSafety.RestoreEncoding(document.EncodingCodePage, document.EncodingHasBom));
                if (missingCleanFile)
                    documents[^1].LoadFailed = true;
            }

            var index = Math.Clamp(session.ActiveIndex, 0, documents.Count - 1);
            DocumentTabs.SelectedItem = documents[index].Tab;
            StatusText.Text = T.SessionRestored;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private sealed class DocumentTab
    {
        public string? FilePath { get; set; }
        public string Content { get; set; } = string.Empty;
        public bool IsDirty { get; set; }
        public bool NeedsDiskLoad { get; set; }
        public bool IsLoading { get; set; }
        public bool LoadFailed { get; set; }
        public Task? LoadTask { get; set; }
        public Encoding TextEncoding { get; set; } = DocumentSafety.RestoreEncoding(65001, false);
        public string DefaultExtension { get; set; } = ".md";
        public TabItem? Tab { get; set; }
    }

    private sealed record FormatOption(string Id, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record AgentFileItem(string FullPath, string RelativePath)
    {
        public override string ToString() => RelativePath;
    }

    private sealed class SessionState
    {
        public int ActiveIndex { get; set; }
        public bool IsDark { get; set; } = true;
        public string Language { get; set; } = "en";
        public bool IsToolbarVisible { get; set; } = true;
        public bool IsAgentToolsVisible { get; set; }
        public string? WorkspaceRoot { get; set; }
        public string FontFamily { get; set; } = "Consolas";
        public int BaseFontSize { get; set; } = 15;
        public int EditorZoom { get; set; } = 100;
        public int PreviewZoom { get; set; } = 100;
        public string ViewMode { get; set; } = "both";
        public List<SessionDocument> Documents { get; set; } = [];
    }

    private sealed class SessionDocument
    {
        public string? FilePath { get; set; }
        public string Content { get; set; } = string.Empty;
        public bool IsDirty { get; set; }
        public int EncodingCodePage { get; set; } = 65001;
        public bool EncodingHasBom { get; set; }
        public string DefaultExtension { get; set; } = ".md";
    }
}
