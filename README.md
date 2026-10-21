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

## Download

Get the latest version from the [Releases page](https://github.com/Gidrek/GidEditor/releases).

| Platform | File | Install |
| --- | --- | --- |
| Windows | `GidEditor-<version>-win-x64-setup.exe` | Run the installer. No admin rights needed. A portable `.zip` is also available. |
| macOS | `GidEditor-<version>-osx-arm64.dmg` (Apple Silicon) or `osx-x64` (Intel) | Open the `.dmg` and drag GidEditor to Applications. |
| Linux (Debian, Ubuntu, Mint…) | `gideditor_<version>_amd64.deb` (or `arm64`) | `sudo apt install ./gideditor_<version>_amd64.deb` |
| Linux (other) | `GidEditor-<version>-linux-x64.tar.gz` | Extract and run `GidEditor`. |

The builds are not signed with paid certificates yet, so the first launch shows a warning:

- **Windows:** in "Windows protected your PC", click **More info → Run anyway**.
- **macOS:** open the app once, then go to **System Settings → Privacy & Security** and click **Open Anyway**.

## Releasing

Releases are built by GitHub Actions ([`.github/workflows/release.yml`](.github/workflows/release.yml)).
Push a version tag and the workflow runs the tests, builds every package, and publishes a GitHub Release
with them and a `SHA256SUMS.txt`:

```bash
git tag v0.2.0
git push origin v0.2.0
```

A tag with a suffix (`v0.3.0-beta.1`) is published as a pre-release. To build packages without releasing,
open **Actions → Release → Run workflow**. The files are attached to the run as artifacts.

The packaging scripts also work locally:

```bash
build/linux/package.sh linux-x64 0.2.0          # .deb + .tar.gz (Linux)
build/macos/package.sh osx-arm64 0.2.0          # .app in a .dmg (macOS)
pwsh build/windows/package.ps1 -Rid win-x64 -Version 0.2.0   # installer + .zip (Windows, needs Inno Setup 6+)
```

Output goes to `publish/`.

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
build/                   Packaging scripts for Linux, macOS and Windows
.github/workflows/       CI tests and release builds
```

## License

Copyright © 2026 Giovanni Cortes

GidEditor is open source under the [MIT License](LICENSE).

### Third-party components

GidEditor is built on open source components (Avalonia, AvaloniaEdit, Markdig, SkiaSharp, the Inter font and others).
Their full license texts are in [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt). That file ships next to the app,
and you can read it in the app under **… → About GidEditor**.

When you add a dependency, add its license to `THIRD-PARTY-NOTICES.txt`. The test suite fails if a shipped
library has no entry.
