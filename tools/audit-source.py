"""Offline publication gate: scan only source files slated for version control.

Does not inspect user data or the excluded original ZIP. This is a pattern check,
not a guarantee that every possible secret is detectable.
"""
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]
ALLOWED_SUFFIXES = {'.cs', '.xaml', '.csproj', '.md', '.ps1', '.svg', '.config', '.yml', '.yaml', '.py', '.gitignore'}
SKIP_PARTS = {'.git', 'bin', 'obj', 'dist', 'dist-latest', 'dist-stable', '.vs', 'backup', 'backups', '.appdata', '.nuget', '.localappdata', '.dotnet'}
PATTERNS = {
    'Windows user home directory': re.compile(r'(?i)[a-z]:[\\/]+users[\\/]+[^\\/\s"\']+'),
    'Personal email address': re.compile(r'(?i)\b[a-z0-9._%+-]+@[a-z0-9.-]+\.[a-z]{2,}\b'),
    'GitHub token': re.compile(r'\b(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{30,})\b'),
    'OpenAI-style secret key': re.compile(r'\bsk-(?:proj-)?[A-Za-z0-9_-]{16,}\b'),
    'Private PEM key': re.compile(r'-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----'),
}
failures = []
count = 0
for p in ROOT.rglob('*'):
    if not p.is_file() or any(part.lower() in SKIP_PARTS for part in p.relative_to(ROOT).parts):
        continue
    rel = p.relative_to(ROOT).as_posix()
    if p.name.lower() in {'session.json', 'error.log', '.env'} or p.suffix.lower() in {'.exe', '.dll', '.pdb', '.zip', '.bak'}:
        failures.append((rel, 'sensitive/compiled file type'))
        continue
    if p.suffix.lower() not in ALLOWED_SUFFIXES and p.name not in {'.gitignore', 'NuGet.Config', 'app.ico'}:
        failures.append((rel, 'unrecognized file type'))
        continue
    if p.name == 'app.ico':
        continue
    try:
        content = p.read_text(encoding='utf-8')
    except (UnicodeError, OSError):
        failures.append((rel, 'unreadable source file'))
        continue
    count += 1
    for label, pattern in PATTERNS.items():
        if p.name == 'audit-source.py' and label != 'Private PEM key':
            continue  # the scanner itself contains examples of its patterns
        if pattern.search(content):
            failures.append((rel, label))

if failures:
    for path, label in failures:
        print(f'BLOCK: {path} ({label})')
    print(f'SOURCE AUDIT FAILED: {len(failures)} potential problems')
    sys.exit(1)
print(f'SOURCE AUDIT PASS: {count} source files checked, no detected private-data patterns.')
