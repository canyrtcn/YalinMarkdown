"""Publication gate for a deliberately small, source-only repository.

Checks filenames and common sensitive-content patterns. This is defense in
 depth, not a guarantee that arbitrary personal information is detectable.
"""

from pathlib import Path
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]

# Fail closed if a new tracked document, fixture, dump or binary is introduced.
# Update this list intentionally when adding a legitimate source file.
ALLOWED_FILES = {
    '.github/workflows/ci.yml',
    '.gitignore',
    'AgentAnalysis.cs',
    'AgentPanelLocalization.cs',
    'App.xaml',
    'App.xaml.cs',
    'DocumentSafety.cs',
    'LICENSE',
    'LiveTextMetrics.cs',
    'Localization.cs',
    'MainWindow.xaml',
    'MainWindow.xaml.cs',
    'MarkdownEditor.csproj',
    'NuGet.Config',
    'README.md',
    'assets/app-icon.svg',
    'assets/app.ico',
    'assets/screenshots/yalinmarkdown-preview.png',
    'tests/YalinMarkdown.SmokeTests/Program.cs',
    'tests/YalinMarkdown.SmokeTests/YalinMarkdown.SmokeTests.csproj',
    'tools/GenerateIcon.ps1',
    'tools/audit-source.py',
    'tools/check-ui.py',
}

SKIP_DIRS = {
    '.git', '.vs', '.vscode', '.idea', '.nuget', '.dotnet', '.venv',
    '.appdata', '.localappdata', '__pycache__', 'bin', 'obj', 'build',
    'release', 'dist', 'artifacts', 'backup', 'backups', 'testresults',
}

PATTERNS = {
    'absolute Windows user directory': re.compile(r'(?i)[a-z]:[\\/]+users[\\/]+[^\\/\s"\']+'),
    'absolute Unix user directory': re.compile(r'(?i)(?:/home/|/Users/)[A-Za-z0-9_.-]+/'),
    'personal email address': re.compile(r'(?i)\b[a-z0-9._%+-]+@[a-z0-9.-]+\.[a-z]{2,}\b'),
    'GitHub access token': re.compile(r'\b(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{30,})\b'),
    'OpenAI-style API key': re.compile(r'\bsk-(?:proj-)?[A-Za-z0-9_-]{16,}\b'),
    'other service key': re.compile(r'\b(?:AKIA[0-9A-Z]{16}|AIza[0-9A-Za-z_-]{30,}|xox[baprs]-[A-Za-z0-9-]{16,})\b'),
    'private key': re.compile(r'-----BEGIN (?:RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----'),
    'inline bearer credential': re.compile(r'(?i)\bBearer\s+[A-Za-z0-9_\-\.]{24,}\b'),
    'assigned credential': re.compile(r'(?i)\b(?:api[_-]?key|access[_-]?token|client[_-]?secret|password)\s*[:=]\s*["\'][A-Za-z0-9_\-./+]{12,}["\']'),
}

failures: list[tuple[str, str]] = []

try:
    tracked = {
        Path(name).as_posix()
        for name in subprocess.check_output(
            ['git', 'ls-files', '-z'], cwd=ROOT
        ).decode('utf-8').split('\0') if name
    }
except (OSError, subprocess.CalledProcessError, UnicodeError):
    tracked = set()

for name in tracked - ALLOWED_FILES:
    failures.append((name, 'unexpected tracked file'))

scanned = 0
for path in ROOT.rglob('*'):
    if not path.is_file():
        continue
    relative = path.relative_to(ROOT).as_posix()
    if any(part.lower() in SKIP_DIRS or part.lower().startswith('dist-')
           for part in path.relative_to(ROOT).parts[:-1]):
        continue
    if relative not in ALLOWED_FILES:
        failures.append((relative, 'unexpected file in source directory'))
        continue
    if relative in {'assets/app.ico', 'assets/screenshots/yalinmarkdown-preview.png'}:
        # Binary icon and screenshot are intentional, checked-in application assets.
        continue
    try:
        body = path.read_text(encoding='utf-8')
    except (UnicodeError, OSError):
        failures.append((relative, 'source file could not be decoded as UTF-8'))
        continue
    scanned += 1
    if relative == 'tools/audit-source.py':
        # Avoid self-matches from regex definitions in this scanner.
        continue
    for description, pattern in PATTERNS.items():
        if pattern.search(body):
            failures.append((relative, description))

for filename, issue in sorted(set(failures)):
    # Never print a credential or file content into CI logs.
    print(f'BLOCK: {filename} ({issue})')
if failures:
    print(f'SOURCE AUDIT FAILED: {len(failures)} issue(s).')
    sys.exit(1)

print(f'SOURCE AUDIT PASS: {scanned} source files checked; no unexpected or detected sensitive files.')
