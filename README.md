# YalinMarkdown

A lightweight, offline Markdown editor for Windows with a live, side-by-side preview.

YalinMarkdown is built with WPF and .NET 10. It focuses on straightforward text editing, predictable performance, and local files—no account, cloud connection, or API key is required.

## Highlights

- **Live Markdown preview:** write on the left and view formatted text on the right, without switching screens. Includes headings, emphasis, lists, code blocks, and links.
- **Multiple documents:** open `.md`, `.markdown`, and `.txt` files in tabs. Tabs stay in place when selected and can be reordered by dragging.
- **Editing essentials:** formatting actions, undo, font and size controls, zoom, and editor-only or preview-only modes.
- **Dark and light themes:** a consistent interface with a shared accent color.
- **Ten interface languages:** English, Turkish, German, French, Spanish, Italian, Chinese, Japanese, Korean, and Russian. English is the default on first launch; later selections are remembered.
- **Live document statistics:** word and character counts, plus an approximate token estimate.
- **Session recovery:** reopen tabs and recover locally stored unsaved work after restarting.
- **Agent Tools:** inspect local agent instruction files and workspace Markdown with heuristic checks, section-level token estimates, approximate merged context, and keyword-based skill matching.

## Requirements

| Usage | Requirement |
| --- | --- |
| Build from source | Windows and the .NET 10 SDK |
| Run a normal build | Windows and the .NET 10 Desktop Runtime |
| Run a self-contained Windows x64 build | Windows x64; no separate .NET installation |

### Build

```powershell
dotnet build .\MarkdownEditor.csproj -c Release
```

The framework-dependent executable is generated at `bin/Release/net10.0-windows/YalinMarkdown.exe`.

### Publish a standalone executable

```powershell
dotnet publish .\MarkdownEditor.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -p:IncludeNativeLibrariesForSelfExtract=true -o .\dist\win-x64-self-contained
```

The self-contained executable is written to `dist/win-x64-self-contained/YalinMarkdown.exe`. It includes the .NET runtime, so its download size is significantly larger. Trimming is disabled for WPF compatibility.

## Agent Tools

Open **Agent Tools** in the toolbar and choose a local workspace. You can analyze the active document or the workspace, review findings and estimated token usage, inspect a merged instruction view, and try a sample task against the skill-matching heuristics.

Agent Tools runs locally and produces **advisory results**, not security guarantees. Its merged context and skill scores are approximations; they do not reproduce the exact behavior or instruction precedence of a particular AI agent. Some technical finding descriptions remain in English.

## Performance and data safety

- **File opening limit:** files larger than **8 MiB** are currently rejected to avoid excessive WPF editor memory or UI load.
- **Bounded preview:** large documents are previewed up to **80,000 characters / 1,200 lines**. This affects only the preview; the underlying document is not truncated when saved.
- **Responsive editing:** text statistics update as you type, and common line edits update only affected preview blocks. Larger structural changes can require a background-sliced rebuild on the UI thread.
- **Safer file saves:** edits are written to a temporary file before replacing the destination.
- **Text encoding:** UTF-8, UTF-16, and UTF-32 are supported, preserving recognized byte-order marks where present.
- **Local session data:** document contents, document and workspace paths, preferences, and error logs may be saved under `%LOCALAPPDATA%\YalinMarkdown`. Do not share those files publicly.

YalinMarkdown does not upload document content. Before publishing bug reports or screenshots, check that they do not expose private filenames or document text.

## Development

Automated checks are kept in `tests/` and `tools/`, separate from application source files:

```powershell
python tools/audit-source.py
python tools/check-ui.py
dotnet run --project tests/YalinMarkdown.SmokeTests/YalinMarkdown.SmokeTests.csproj -c Release
dotnet build .\MarkdownEditor.csproj -c Release
```

GitHub Actions runs these checks on the repository. The source audit detects common secrets, machine-specific paths, and unexpected files, but automated scanning cannot guarantee that no sensitive material exists. Review every change before public release.

## License

Released under the [MIT License](LICENSE). Maintained by [canyrtcn](https://github.com/canyrtcn).
