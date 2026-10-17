# YalinMarkdown

**YalinMarkdown** is a lightweight Markdown and plain-text editor for Windows, built with WPF and .NET 10. It keeps the source editor and a live Markdown preview side by side. No account, cloud service, API key, or third-party NuGet package is required.

## Features

- **Live split view:** edit Markdown on the left and see the rendered preview on the right. The layout stays intact; document tabs keep their positions when you select another tab.
- **Tabbed editing:** open multiple `.md`, `.markdown`, and `.txt` files, drag and drop files, and manually reorder tabs by dragging them.
- **Editing tools:** save, Save As, undo, inline and block formatting, font choices, zoom, and optional editor-only or preview-only viewing.
- **Themes:** dark and light appearances with consistent accent colors in the toolbar and menus.
- **Languages:** English (default on first launch), Turkish, German, French, Spanish, Italian, Chinese, Japanese, Korean, and Russian. Your chosen language is restored with the local session.
- **Live metrics:** word count, character count, and an approximate token estimate for Markdown documents.
- **Local session recovery:** restores tabs and unsaved changes after restart. No document is uploaded to any server.
- **Agent Tools:** heuristic checks for instruction files, approximate per-section token cost, suspicious command/secret patterns, duplicate or conflicting instructions, an approximate merged instruction context, and skill-description matching. These results are advisory, **not** a security guarantee or a faithful simulation of any particular agent runtime.

## System requirements

- Windows 10/11 (64-bit Windows recommended).
- **To build:** .NET 10 SDK with the Windows Desktop/WPF tooling.
- **To run a normal framework-dependent build:** .NET 10 **Desktop Runtime** (not the SDK).
- **To run a self-contained release:** no separate .NET installation is required. A self-contained single-file executable is larger because it contains the runtime.

## Build from source

```powershell
dotnet build .\MarkdownEditor.csproj -c Release
```

Output: `bin/Release/net10.0-windows/YalinMarkdown.exe` (framework-dependent).

To publish an independent Windows x64 executable:

```powershell
dotnet publish .\MarkdownEditor.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -p:IncludeNativeLibrariesForSelfExtract=true -o .\dist\win-x64-self-contained
```

Output: `dist/win-x64-self-contained/YalinMarkdown.exe`. WPF trimming is disabled to avoid compatibility problems. A .NET-free installation test is recommended before publishing binaries to end users.

## Agent Tools

Select **Agent Tools** in the toolbar, then:

1. Select a local workspace with **Choose Workspace**.
2. Run **Analyze Active** for the current document, or **Analyze Workspace** for Markdown files in the selected folder tree.
3. Review **Findings**, **Sections**, and **Files**; double-click findings or paths to open their source and jump to a line where available.
4. Inspect **Merged Context**, an *approximation* based on common agent instruction files, not the exact precedence or content of a specific AI tool.
5. Enter an example task in **Skill Match** to see simple keyword-based skill scores.

Scanning remains local. Common build, dependency, hidden, and backup folders are skipped. File-size and result limits protect the interface; incomplete or skipped analysis is not equivalent to a clean security audit. Some diagnostic output is in English, including technical finding descriptions.

## Keyboard shortcuts

| Shortcut | Action |
| --- | --- |
| `Ctrl+N` | New document |
| `Ctrl+O` | Open document |
| `Ctrl+S` | Save |
| `Ctrl+Shift+S` | Save As |
| `Ctrl+B` | Bold selected text |
| `Ctrl+Z` | Undo |

## Large files and data safety

- **8 MiB maximum file size for opening:** the current WPF `TextBox` editor intentionally rejects larger files to avoid freezes. Files up to that limit may still behave differently depending on their contents and hardware.
- **Bounded preview:** for large documents the preview displays up to **80,000 characters / 1,200 lines** and clips unusually long lines. The underlying editor text is **not** truncated, and the complete source is saved.
- **Incremental updates:** live counters update with every edit, and common line edits update only the affected preview blocks. Some structural changes require a sliced full preview rebuild.
- **Safer saves:** a temporary file is written in the same directory and used to replace an existing destination, rather than truncating it in place. Save errors are reported.
- **Text encodings:** UTF-8, UTF-16, and UTF-32 are supported with BOM preservation where present. Invalid unrecognized encodings are rejected instead of silently rewritten.
- **Local recovery:** unsaved content, filenames, workspace paths, and language/theme settings may be stored in `%LOCALAPPDATA%\YalinMarkdown\session.json`. **Do not publish or share your session file.**

## Development checks

Run these on Windows with the .NET 10 SDK:

```powershell
python tools/audit-source.py
python tools/check-ui.py
dotnet run --project tests/YalinMarkdown.SmokeTests/YalinMarkdown.SmokeTests.csproj -c Release
dotnet build .\MarkdownEditor.csproj -c Release
```

The included privacy scanner detects common credentials and personal-path patterns in proposed source files, but no pattern-based scanner can guarantee that a repository contains no sensitive information. Review changes before making the repository public. Compiled executables, personal sessions, logs, user-specific configuration, and archives are excluded from source control.

## Project

Author's GitHub: [canyrtcn](https://github.com/canyrtcn). The footer's GitHub link opens this profile in your default browser. This project is currently being prepared for a public release; no license is granted by this README.
