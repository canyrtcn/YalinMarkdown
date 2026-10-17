"""Static structural checks; complements (does not replace) Windows WPF UI tests."""
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
xaml = (root / 'MainWindow.xaml').read_text(encoding='utf-8')
code = (root / 'MainWindow.xaml.cs').read_text(encoding='utf-8')
translations = (root / 'Localization.cs').read_text(encoding='utf-8')
agent_labels = (root / 'AgentPanelLocalization.cs').read_text(encoding='utf-8')
ET.fromstring(xaml)
ET.parse(root / 'App.xaml')
checks = {
    'window title': 'Title="YalinMarkdown"' in xaml,
    'brand header': 'Text="YalinMarkdown"' in xaml,
    'English fallback': 'private string currentLanguage = "en"' in code and 'public string Language { get; set; } = "en"' in code,
    'stable document tabs template': 'Style="{StaticResource StableDocumentTabsStyle}"' in xaml and '<WrapPanel IsItemsHost="True" Orientation="Horizontal" />' in xaml,
    'accent menu hover': 'TargetType="ComboBoxItem"' in xaml and 'TargetName="OptionBorder" Property="BorderBrush" Value="{DynamicResource AccentBrush}"' in xaml,
    'GitHub profile footer': 'x:Name="GitHubProfileButton"' in xaml and 'https://github.com/canyrtcn' in code,
    'two-pane editor preserved': 'x:Name="EditorColumn"' in xaml and 'x:Name="PreviewColumn"' in xaml,
    'light/dark resource switching': 'Resources["AccentSoftBrush"]' in code and 'isDark ? "#00E676" : "#056233"' in code,
    'Agent Tools localized labels': all(f'x:Name="{name}"' in xaml for name in (
        'AgentOverviewTab', 'AgentFindingsTab', 'AgentSectionsTab', 'AgentFilesTab',
        'AgentMergedContextTab', 'AgentSkillMatchTab', 'AgentToolsHeaderText')),
    'Agent finding navigation awaits open': 'await OpenFileAsync(finding.FilePath)' in code,
    'all ten core locales': all(f'"{lang}" => new(' in translations for lang in ('tr', 'de', 'fr', 'es', 'it', 'zh', 'ja', 'ko', 'ru')),
    'all ten Agent Tools locales': all(f'"{lang}" => new(' in agent_labels for lang in ('tr', 'de', 'fr', 'es', 'it', 'zh', 'ja', 'ko', 'ru')),
    'no old application brand': '"Yalın Markdown"' not in translations and 'YALIN MARKDOWN' not in xaml,
}

# Validate XAML event hookup names against declared C# methods.
for attribute in ('Click', 'SelectionChanged', 'TextChanged', 'PreviewMouseLeftButtonDown',
                  'PreviewMouseMove', 'MouseDoubleClick', 'DragOver', 'Drop', 'Closing',
                  'SourceInitialized', 'PreviewMouseWheel', 'MouseWheel', 'Loaded', 'ScrollChanged'):
    for handler in re.findall(rf'\b{attribute}="([A-Za-z_]\w*)"', xaml):
        if handler not in code:
            checks[f'event {handler}'] = False

for label, passed in checks.items():
    print(f'{"PASS" if passed else "FAIL"}: {label}')
if not all(checks.values()):
    sys.exit(1)
print(f'UI STRUCTURE PASS: {len(checks)} static checks')
