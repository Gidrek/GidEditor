# GidEditor

A simple, good-looking Markdown editor for **Windows, macOS and Linux**, built with C# and [Avalonia](https://avaloniaui.net).

GidEditor opens a **folder as a workspace**. Your notes are plain `.md` files in that folder, so you can keep it on your
computer, in Dropbox, Google Drive, iCloud or a Git repository. There is no database or proprietary format, so your notes stay yours.

## Features

- **Three views**: *Editor* (styled source), *Split* (editor and live preview, scroll-synced) and *Preview* (reading mode). Cycle with `Ctrl+E`.
- **Styled source editing**: headings are large, bold is bold, code is monospaced, and links are colored, while the Markdown stays plain text. Syntax markers are dimmed rather than hidden.
- **Formatting toolbar and shortcuts** for bold, italic, strikethrough, code, headings, lists, tasks, quotes, links, images, tables, code blocks and dividers.
- **Smart lists**: Enter continues bullets, numbers, tasks and quotes. Tab and Shift+Tab indent and outdent.
- **Native preview** rendered with [Markdig](https://github.com/xoofx/markdig) (GitHub-flavored: tables, task lists, footnotes, front matter …). Task checkboxes in the preview update the file.
- **Workspace sidebar**: file tree with new note, new folder, rename, move to trash, and show in file manager.
- **Tabs**, **full-text search** across all notes (`Ctrl+Shift+F`).
- **Images**: paste screenshots, drop image files, or use the toolbar. They are copied into `attachments/` and linked relatively.
- **Autosave**, plus **reload on external change**: if Dropbox or Drive updates a note from another device, it reloads in place. If you had unsaved edits, a banner lets you choose which version to keep.
- **Light and dark themes** (follows the OS, or pick one), remembered workspace, tabs, view and window size.

## Keyboard shortcuts

On macOS use `Cmd` instead of `Ctrl`.

| Shortcut | Action | Shortcut | Action |
| --- | --- | --- | --- |
| `Ctrl+N` | New note | `Ctrl+B` / `Ctrl+I` | Bold / italic |
| `Ctrl+O` | Open folder | `Ctrl+Shift+X` | Strikethrough |
| `Ctrl+S` | Save now | ``Ctrl+` `` | Inline code |
| `Ctrl+W` | Close tab | `Ctrl+K` | Link |
| `Ctrl+Tab` | Next tab | `Ctrl+1` … `Ctrl+6` | Heading 1–6 |
| `Ctrl+E` | Editor → Split → Preview | `Ctrl+Shift+8` / `7` / `9` | Bullets / numbers / tasks |
| `Ctrl+\` | Toggle sidebar | `Ctrl+Shift+.` | Quote |
| `Ctrl+Shift+F` | Search notes | `Ctrl+Shift+C` | Code block |
| `Ctrl+Shift+E` | Notes tree | `Ctrl+=` / `Ctrl+-` / `Ctrl+0` | Text size |
| `Ctrl+F` | Find in note | `F2` / `Delete` (tree) | Rename / trash |

## Build and run

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet run --project src/GidEditor
```

Try it with the bundled sample notes: open the `samples/Notes` folder from the welcome screen.

## Tests

```bash
dotnet test
```

The tests include headless UI tests that render the real window with Skia and save screenshots to
`tests/GidEditor.Tests/bin/Debug/net10.0/snapshots/`.

## Publishing

Self-contained builds (no .NET install needed on the target machine):

```bash
dotnet publish src/GidEditor -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish/win-x64
dotnet publish src/GidEditor -c Release -r osx-arm64 --self-contained -o publish/osx-arm64
dotnet publish src/GidEditor -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o publish/linux-x64
```

Use `osx-x64` for Intel Macs. On macOS, the published output still needs to be wrapped in a `.app` bundle
(with an `Info.plist`) and signed for distribution.

## Project layout

```
src/GidEditor/
  Markdown/      Editor colorizer, block scanner, formatting commands, Markdig → Avalonia preview renderer
  Controls/      MarkdownViewer (preview pane) and MarkdownTextBlock (clickable links)
  ViewModels/    MainViewModel (workspace, tabs, search), DocumentViewModel (autosave, reload), tree nodes
  Services/      Settings, search, trash, workspace file rules
  Views/         Main window and dialogs
tests/GidEditor.Tests/   Unit tests and headless UI/screenshot tests
samples/Notes/           Example workspace
```
