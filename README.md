# RhinoTable

A professional table creator for Rhino 8. Create and edit Excel-like annotation tables directly in the Rhino viewport — no external tools required.

![RhinoTable banner](DOCS/ai-github-banner.png)

---

## Features

### Table editing
- **Spreadsheet-style editor** with inline text editing, Tab and arrow key navigation
- **Undo / Redo** — up to 20 steps (Ctrl+Z / Ctrl+Y)
- **Copy / Paste** — with formatting, including paste from Excel (tab-separated)
- **Auto-number** — fills the first column with sequential row numbers
- **Auto-width** — fits column widths to content

### Text formatting
- Bold, Italic (Ctrl+B / Ctrl+I)
- Subscript `_{text}` and Superscript `^{text}`
- Per-cell font family and size
- Horizontal alignment: left / center / right
- Vertical alignment: top / middle / bottom
- Text color with 12-color recent history

### Fill & Hatch
- Solid fill color per cell
- Hatch patterns loaded directly from the active Rhino document
- Independent hatch line color
- Adjustable scale (dropdown 1–50 or custom)
- Adjustable rotation (dropdown 0–360° in 45° steps or custom)

### Borders
- All borders, outside border, or per-side toggle (top / bottom / left / right)
- Thin (0.25 mm) or thick (0.5 mm) line weight
- Custom border color

### Structure
- Insert / remove rows and columns
- Merge cells horizontally and vertically
- Reorder columns by drag-and-drop on the column header
- Resize column widths and row heights by dragging

### Templates
- Three built-in templates: Drawing Title Block, Bill of Materials (BOM), Revision Table
- Save any layout as a personal template
- Templates stored in `%APPDATA%\RhinoTable\templates\`

### Import
- Import from CSV (auto-detects `,` `;` `|` tab separator)
- Import from Excel (`.xlsx`) — works even while the file is open in Excel
- Link to an Excel file for live updates (auto-refreshes on file change)

### Other
- **Shared recent colors** — last 12 used colors appear across all four color pickers
- **Auto-update notification** — checks Yak once per day, shows a popup when a new version is available
- **Help window** — keyboard shortcuts and feature overview built in

---

## Requirements

- **Rhino 8** for Windows (8.0 or later)
- .NET 7 (included with Rhino 8)

---

## Installation

### Via Rhino Package Manager (recommended)

```
Rhino 8 → _PackageManager → search "rhinotable" → Install → restart Rhino
```

### Manual

1. Download the latest `.yak` from the [Releases](https://github.com/Freekbaars/Freeks-table-plugin/releases) page
2. In Rhino: `Tools → Options → Plugins → Install` → select the `.rhp` file

---

## Commands

| Command | Description |
|---|---|
| `TableCreate` | Open the table editor to create a new table |
| `TableEdit` | Click an existing RhinoTable block to edit it |
| `TableSync` | Manually refresh all linked Excel tables in the document |

---

## Keyboard shortcuts

| Key | Action |
|---|---|
| Tab / Shift+Tab | Move to next / previous cell |
| ↑ ↓ ← → | Navigate between cells |
| Delete | Clear selected cell(s) |
| Ctrl+C / Ctrl+V | Copy / Paste |
| Ctrl+Z / Ctrl+Y | Undo / Redo |
| Ctrl+B / Ctrl+I | Bold / Italic |
| Ctrl+Enter | Place or update table in Rhino |

---

## Building from source

```powershell
git clone https://github.com/Freekbaars/Freeks-table-plugin.git
cd "Freeks table plugin"
dotnet build --configuration Debug
```

Load the plugin in Rhino: `Tools → Options → Plugins → Install` → select the `.rhp` from `bin\Debug\net7.0-windows\`.

---

## Project structure

```
Freeks table plugin.sln
├── Freeks table plugin/      ← .rhp entry point (loaded by Rhino)
│   └── Commands/             ← TableCreate, TableEdit, TableSync
├── RhinoTable.Core/
│   ├── Models/               ← TableData, TableCellData, TableRowData
│   ├── Import/               ← CSV and Excel importers
│   ├── Layout/               ← Geometry builder, hatch drawing, auto-width
│   └── Settings/             ← UpdateChecker, TemplateManager, RecentColorsManager
└── RhinoTable.UI/
    ├── ViewModels/            ← TableEditorViewModel (all commands + undo/redo)
    ├── Views/                 ← Editor window, template manager, help, update popup
    ├── Converters/            ← WPF value converters
    └── Themes/                ← WPF styles and resources
```

---

## License

GPL-3.0 license
