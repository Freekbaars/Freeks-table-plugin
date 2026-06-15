# RhinoTable — Projectstatus & Sessielog

> Aangemaakt: 2026-06-14  
> Bedoeld als snelstart voor de volgende sessie.

---

## Oplossingsstructuur

```
Freeks table plugin.sln
├── Freeks table plugin/          ← .rhp entry-point (Rhino laadt dit)
│   ├── Freeks_table_plugin.cs    ← OnLoad, FileSystemWatcher voor live Excel-sync
│   └── Commands/
│       ├── TableCreateCommand.cs ← "TableCreate" Rhino-commando
│       ├── TableEditCommand.cs   ← "TableEdit" Rhino-commando (klik bestaand blok)
│       └── TableSyncCommand.cs   ← "TableSync" Rhino-commando (handmatig vernieuwen)
├── RhinoTable.Core/
│   ├── Models/
│   │   ├── TableData.cs          ← Hoofdmodel (JSON-seriaalbaar, opgeslagen in blokbeschrijving)
│   │   ├── TableCellData.cs      ← Celmodel met alle opmaakvelden
│   │   └── TableRowData.cs       ← Rijmodel
│   ├── Import/
│   │   ├── CsvImporter.cs        ← CSV (auto-scheiding: , ; | tab)
│   │   └── ExcelImporter.cs      ← Excel via ClosedXML (FileShare.ReadWrite voor open bestanden)
│   └── Layout/
│       ├── RhinoTableDrawer.cs   ← Geometrie bouwen + Rhino-blok plaatsen/bijwerken
│       ├── DrawHelper.cs         ← RTF-tekst bouwen (vet, cursief, sub, sup)
│       └── AutoWidthCalculator.cs
├── RhinoTable.UI/
│   ├── ViewModels/
│   │   └── TableEditorViewModel.cs ← Alle commands + undo/redo/clipboard + tabellogica
│   ├── Views/
│   │   ├── TableEditorWindow.xaml    ← Ribbon-toolbar + DataGrid + eigenschappenstrip
│   │   ├── TableEditorWindow.xaml.cs ← Code-behind: kolommen bouwen, keyboard-handling
│   │   └── ImportProgressWindow.xaml ← Voortgangsvenster bij CSV/Excel-import
│   ├── Converters/               ← IValueConverter implementaties
│   └── Themes/
│       └── TableEditorTheme.xaml ← Alle WPF-stijlen (knoppen, DataGrid, popup-toggle)
```

---

## Datamodel: TableCellData — volledige veldenlijst

| Veld | Type | Beschrijving |
|---|---|---|
| `Text` | string | Celinhoud (ondersteunt ^{sup} en _{sub}) |
| `FontName` | string? | Lettertype (null = tabel-standaard "Arial") |
| `FontSize` | double? | Puntengrootte (null = tabel-standaard 3.5) |
| `Bold` | bool | Vet |
| `Italic` | bool | Cursief |
| `HorizontalAlignment` | enum | Left / Center / Right |
| `VerticalAlignment` | enum | Top / Middle / Bottom |
| `TextColor` | string? | Hex kleur bijv. "#C0392B", null = zwart |
| `BackgroundColor` | string? | Hex vulkleur, null = transparant |
| `FillPattern` | int | 0=Geen 1=Effen 2=H 3=V 4=Diag 5=Kruis |
| `WordWrap` | bool | Tekst afbreken op kolombreedte |
| `MergeRight` | int | Aantal cellen rechts samenvoegen |
| `MergeDown` | int | (gereserveerd, nog niet actief in UI) |
| `IsMergedHidden` | bool | True = verborgen door samenvoeging |
| `BorderTop/Bottom/Left/Right` | float | Randdikte in mm (0 = geen aangepaste rand) |
| `BorderColor` | string? | Hex randkleur, null = standaard donkergrijs |

**TableData** bevat daarnaast:
- `TableName` (string?) — blok-naam in Rhino, null = automatisch `RhinoTable_<timestamp>`
- `LinkedExcelPath` (string?) — pad naar gekoppeld Excel-bestand
- `SourceObjectId` (Guid?) — ID van het Rhino-blok bij bewerken (TableEdit-flow)
- `DefaultFontName`, `DefaultFontSize`, `ColumnWidths`, `RowHeights`

---

## Geïmplementeerde functies

### Tekstopmaak
- ✅ Vet / Cursief (Ctrl+B / Ctrl+I)
- ✅ Subscript `_{tekst}` / Superscript `^{tekst}` (via RTF)
- ✅ Horizontale uitlijning: links / midden / rechts
- ✅ Verticale uitlijning: boven / midden / onder
- ✅ Lettertype & grootte per cel (via eigenschappenstrip)
- ✅ Tekstkleur (10 kleuren via T▼ dropdown-popup)
- ✅ Woordterugloop (↵ knop, WPF: native wrap; Rhino: handmatige regelafbreking)

### Celachtergrond
- ✅ Vulkleur (10 kleuren via V▼ dropdown-popup)
- ✅ 6 arceerpatronen: geen/effen/horizontaal/verticaal/diagonaal/kruis

### Celranden (RAND-groep in toolbar)
- ✅ Alle randen wissen (✕)
- ✅ Alle randen instellen (□)
- ✅ Alleen buitenrand (■)
- ✅ Per-zijde toggle: ⊤ ⊥ ⊣ ⊢
- ✅ Dikte: dun (0.25 mm) / dik (0.5 mm)
- ✅ Randkleur (6 kleuren via R▼ dropdown-popup)

### Structuur
- ✅ Rij invoegen boven/onder, toevoegen einde, verwijderen
- ✅ Kolom invoegen links/rechts, toevoegen einde, verwijderen
- ✅ Cellen samenvoegen (naar rechts)
- ✅ Multi-cel selectie (rechthoek of hele rij)

### Bewerkingsstromen
- ✅ Ongedaan maken / Opnieuw (Ctrl+Z / Ctrl+Y) — snapshot-gebaseerd
- ✅ Kopiëren / Plakken (Ctrl+C / Ctrl+V) — inclusief opmaak + systeem-klembord
- ✅ Auto-nummer (eerste kolom)
- ✅ Auto-breedte (kolommen aanpassen aan inhoud)

### Import
- ✅ CSV-import (auto-scheiding: `,` `;` `|` tab, max 500 rijen)
- ✅ Excel-import via ClosedXML (ook als bestand open staat in Excel)
- ✅ Live Excel-koppeling (FileSystemWatcher, 2-sec debounce, automatische update)
- ✅ Voortgangsvenster bij grote imports

### Rhino-integratie
- ✅ Tabel plaatsen als Rhino-blok (InstanceDefinition)
- ✅ Tabel bewerken (TableEdit, klik bestaand blok)
- ✅ TableSync-commando (handmatig alle gekoppelde tabellen vernieuwen)
- ✅ Tafelnaam instellen (= bloknaam in Rhino)
- ✅ Tekstkleur via `ObjectAttributes.ColorFromObject`
- ✅ H+V uitlijning gecombineerd in `TextJustification` (TopLeft t/m BottomRight)
- ✅ Celranden: aparte `LineCurve`-objecten per zijde met `PlotWeight`

---

## Toolbar-indeling (ribbon-stijl)

```
[✓ Plaatsen] | STRUCTUUR         | OPMAAK                        | KLEUR & PATROON | RAND           | EXTRA▼
               ↑ ↓ ＋ ✕ (rijen)   B I | ≡L≡C≡R | ⊤⊢⊥ | ↵           T▼  V▼  □■≡⦀╱#    ✕□■ — ═ R▼     popup:
               ← → ＋ ✕ (kols)   X₂ X² | ⊞ Samen                                    ⊤ ⊥ ⊣ ⊢         CSV/Excel/
               Rijen   Kolommen                                                                         Koppel/Sync
                                                                                                        Auto-nr/breedte
```

**Eigenschappenstrip** (onder toolbar):
`Tafelnaam | Breedte (mm) | Hoogte (mm) | Lettertype | Grootte (pt)`

---

## Kleurpopups — hoe ze werken

Alle drie de kleur-dropdowns (T▼ tekst, V▼ vulling, R▼ rand) gebruiken hetzelfde patroon:

1. Een `ToggleButton` met `x:Name` in de toolbar
2. Een `<Popup>` met `IsOpen="{Binding IsChecked, ElementName=..., Mode=TwoWay}"` en `StaysOpen="False"`
3. Elke kleurvlak-knop heeft `Click="ColorPopupButton_Click"` + `Tag="{Binding ElementName=...Toggle}"`
4. `ColorPopupButton_Click` in code-behind zet de Toggle op `IsChecked=false` → popup sluit

---

## Undo/Redo — hoe het werkt

- `PushUndoSnapshot()` serialiseert `_tableData` naar JSON vóór elke bewerking
- `_undoStack` en `_redoStack` zijn `Stack<string>`
- Undo: pop van `_undoStack`, push naar `_redoStack`, deserialize en rebuild
- `_suppressUndo = true` tijdens Undo/Redo zelf (geen snapshots van snapshots)
- Toetsenbord: Ctrl+Z en Ctrl+Y afgehandeld in `TableGrid_PreviewKeyDown`
- **Opgelet**: tekst typen maakt geen snapshot per karakter — de TextBox heeft zijn eigen undo (Ctrl+Z werkt eerst in de TextBox, pas daarna op tabel-niveau)

---

## Copy/Paste — hoe het werkt

- `CopyCommand` (Ctrl+C): slaat `List<ClipCell>` op met relatieve positie + kloon van `TableCellData`
- Zet ook tab-gescheiden tekst op het Windows-klembord (voor Excel-compatibiliteit)
- `PasteCommand` (Ctrl+V): plakt intern klembord op huidige cel, of valt terug op systeem-klembord als tab-tekst
- `CloneCell` / `CopyProperties` kopiëren ALLE velden inclusief randen en terugloop

---

## Celranden — hoe ze werken

**In de WPF-editor**: *nog niet zichtbaar als apart visueel element* — de DataGrid toont altijd zijn eigen gridlijnen. De randwaarden worden wel opgeslagen en zichtbaar bij plaatsen in Rhino.

> **TODO voor volgende sessie**: randvisualisatie toevoegen aan de cel-DataTemplate in `MakeColumn()` (code-behind). Een `Border`-element bovenop de cel met binding op `BorderTop/Bottom/Left/Right` en `BorderColor`.

**In Rhino**: `BuildGeometry()` in `RhinoTableDrawer.cs` — als een cel aangepaste randen heeft, worden 4 aparte `LineCurve`-objecten getekend (één per zijde) met `ObjectAttributes.PlotWeight = cell.BorderTop` etc. Cellen zonder aangepaste randen krijgen een standaard `PolylineCurve` rechthoek.

---

## Bekende beperkingen / TODO-lijst

| Item | Prioriteit | Notitie |
|---|---|---|
| Randvisualisatie in WPF-editor | Hoog | Waarden worden al opgeslagen, alleen display ontbreekt |
| MergeDown (vertikaal samenvoegen) | Middel | Veld bestaat in model, geen UI-commando |
| Export naar Excel/CSV | Laag | Alleen import is geïmplementeerd |
| Rechtermuisknop-menu | Laag | Handig maar niet kritiek |
| Per-cel rijhoogte instellen | Laag | Nu alleen via eigenschappenstrip |

---

## Build & Deploy

```powershell
# Build
cd "D:\03_code\02_rhino\Table plugin\Freeks table plugin"
dotnet build --configuration Debug

# Verwachte output: Build succeeded, 4 warnings (NU1701 - niet kritiek), 0 errors

# Plugin laden in Rhino:
# Rhino → Tools → Options → Plugins → Install → kies .rhp uit bin/Debug map
# Commands: TableCreate, TableEdit, TableSync
```

---

## Kritieke implementatie-details

### RhinoTableDrawer — tekstplaatsing
- Anker Y-positie hangt af van `VerticalAlignment`: `y0-margin` (Top), `(y0+y1)/2` (Middle), `y1+margin` (Bottom)
- `GetJustification(h, v)` combineert beide richtingen naar `TextJustification` enum
- Woordterugloop: `WrapText()` schat `fontSize * 0.55 mm` per karakter (bij benadering)

### WPF-editor — tekstkleur
- Kleur wordt gezet via `element.SetValue(ForegroundProperty, brush)` in een `Loaded`-event handler
- Dit geeft LocalValue (prioriteit 3) en wint van geërfde waarden uit `DataGridCell`-stijl
- NIET via `SetBinding` — dat heeft onzekere prioriteit t.o.v. stijl-triggers

### Popup DataContext
- Popups erven DataContext via de logische boom (XAML-naamscope)
- DataContext expliciet gezet: `DataContext="{Binding DataContext, ElementName=EditorWindow}"`
- `x:Name="EditorWindow"` staat op het `<Window>`-element
