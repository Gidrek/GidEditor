# Welcome to GidEditor

GidEditor keeps your notes as **plain Markdown files** in a folder *you* choose. Put that folder in Dropbox, Google Drive, iCloud or anywhere else, and your notes stay ~~locked in~~ yours.

![Banner](attachments/banner.png)

## Three ways to look at a note

1. **Editor**: just the text, styled as you type
2. **Split**: editor and preview side by side, scrolling together
3. **Preview**: the rendered note, for reading

## Formatting

Use the toolbar or shortcuts: `Ctrl+B` for bold, `Ctrl+I` for italic, `Ctrl+K` for a [link](https://commonmark.org), and `Ctrl+1` to `Ctrl+6` for headings.

> Paste a screenshot with `Ctrl+V` or drop an image onto the editor. It is saved
> in the `attachments` folder next to your notes.

### Tasks

- [x] Open a folder as a workspace
- [ ] Write the first note
- [ ] Try the preview checkboxes, they update the file

### Code

```csharp
var note = File.ReadAllText("Welcome.md");
Console.WriteLine($"{note.Length} characters");
```

### Tables

| Shortcut       | Action            |
| -------------- | ----------------- |
| `Ctrl+N`       | New note          |
| `Ctrl+Shift+F` | Search all notes  |
| `Ctrl+E`       | Switch view       |

---

See also [the roadmap](Projects/Roadmap.md).
