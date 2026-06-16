# RhinoTable — Projectstatus & Sessielog

> Aangemaakt: 2026-06-14 | Bijgewerkt: 2026-06-16  
> Bedoeld als snelstart voor de volgende sessie.

---

## Oplossingsstructuur

```
Freeks table plugin.sln
├── Freeks table plugin/          ← .rhp entry-point (Rhino laadt dit)
│   ├── Freeks_table_plugin.cs    ← OnLoad, FileSystemWatcher, async update-check
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
│   ├── Layout/
│   │   ├── RhinoTableDrawer.cs   ← Geometrie bouwen + Rhino-blok plaatsen/bijwerken
│   │   ├── DrawHelper.cs         ← RTF-tekst bouwen (vet, cursief, sub, sup)
│   │   └── AutoWidthCalculator.cs
│   └── Settings/
│       ├── UpdateChecker.cs      ← Async Yak-versie check (max 1×/dag), %APPDATA%\RhinoTable\update_check.txt
│       ├── TemplateManager.cs    ← TableTemplate model + opslaan/laden/verwijderen in %APPDATA%\RhinoTable\templates\
│       └── RecentColorsManager.cs ← Laadt/slaat max 12 recente kleuren op in %APPDATA%\RhinoTable\recent_colors.json
├── RhinoTable.UI/
│   ├── ViewModels/
│   │   └── TableEditorViewModel.cs ← Alle commands + undo/redo/clipboard + tabellogica
│   ├── Views/
│   │   ├── TableEditorWindow.xaml        ← Ribbon-toolbar + DataGrid + eigenschappenstrip
│   │   ├── TableEditorWindow.xaml.cs     ← Code-behind: kolommen bouwen, keyboard-handling
│   │   ├── ImportProgressWindow.xaml     ← Voortgangsvenster bij CSV/Excel-import
│   │   ├── UpdateNotificationWindow.xaml ← Popup bij beschikbare update (opent PackageManager)
│   │   ├── TemplateManagerWindow.xaml    ← Sjablonenmanager: laden / opslaan / verwijderen
│   │   ├── SaveTemplateDialog.xaml       ← Kleine input-dialog voor sjabloonnaam + beschrijving
│   │   └── HelpWindow.xaml               ← Help-venster: sneltoetsen + functies-overzicht (2 tabs)
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
| `FillPattern` | int | 0=Geen 1=Effen 2=H 3=V 4=Diag 5=Kruis (legacy) |
| `HatchColor` | string? | Kleur van het Rhino-arceerpatroon (hex) |
| `HatchPatternName` | string? | Naam Rhino-arceerpatroon (bijv. "Hatch1") |
| `HatchScale` | double | Schaal van het Rhino-arceerpatroon (standaard 1.0) |
| `HatchRotation` | double | Rotatie van het arceerpatroon in graden (standaard 0) |
| `WordWrap` | bool | Tekst afbreken op kolombreedte |
| `MergeRight` | int | Aantal cellen rechts samenvoegen |
| `MergeDown` | int | Aantal cellen omlaag samenvoegen (⊞↓ knop) |
| `IsMergedHidden` | bool | True = verborgen door samenvoeging |
| `BorderTop/Bottom/Left/Right` | float | Randdikte in mm (0 = geen aangepaste rand) |
| `BorderColor` | string? | Hex randkleur, null = standaard donkergrijs |

**TableRowData** bevat daarnaast:
- `IsHeader` (bool) — eerste rij als koptekstrij markeren (H in rijkopje, telt niet mee bij auto-nummer)

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
- ✅ Tekstkleur (kleurpicker met recente kleuren)
- ✅ Woordterugloop (↵ knop, WPF: native wrap; Rhino: handmatige regelafbreking)

### Celachtergrond & Arcering
- ✅ Vulkleur per cel (kleurpicker met recente kleuren)
- ✅ Rhino-arceerpatronen geladen vanuit het actieve document (auto-refresh bij openen popup)
- ✅ Aparte arceerkleur (H▼ kleurpicker)
- ✅ Schaal: dropdown (1–50 in stappen van 5) of eigen invoer
- ✅ Rotatie: dropdown (0–360° in stappen van 45°) of eigen invoer
- ✅ Patroon-preview in dropdown toont per naam een andere visuele indicatie

### Recente kleuren (gedeeld)
- ✅ Laatste 12 gebruikte kleuren zichtbaar in alle 4 kleurpickers
- ✅ Automatisch bijgewerkt bij elke kleurkeuze (ook via hex-invoer)
- ✅ Opgeslagen in `%APPDATA%\RhinoTable\recent_colors.json`
- ✅ Sectie verborgen zolang er nog geen kleuren zijn gebruikt

### Celranden (BORDER-groep in toolbar)
- ✅ Alle randen wissen (✕)
- ✅ Alle randen instellen (□)
- ✅ Alleen buitenrand (■)
- ✅ Per-zijde toggle: ⊤ ⊥ ⊣ ⊢
- ✅ Dikte: dun (0.25 mm) / dik (0.5 mm)
- ✅ Randkleur (kleurpicker met recente kleuren)

### Structuur
- ✅ Rij invoegen boven/onder, toevoegen einde, verwijderen
- ✅ Kolom invoegen links/rechts, toevoegen einde, verwijderen
- ✅ Cellen horizontaal samenvoegen (⊞→, toggle = ontkoppelen)
- ✅ Cellen verticaal samenvoegen (⊞↓)
- ✅ Kolom herordenen via drag-and-drop op kolomkop
- ✅ Kolombreedte slepen via kolomkopscheider
- ✅ Rijhoogte slepen via rijkopscheider
- ✅ Header-rij (rij 1): markeert rij, past automatisch vet + 0,5 mm onderrand toe

### Navigatie & bewerking
- ✅ Tab / Shift+Tab: commit cel en spring naar volgende/vorige
- ✅ Pijltoetsen: commit huidige cel en navigeer (gefixte sessie 2026-06-16)
- ✅ Tekst blijft behouden bij Tab/pijl-navigatie (UpdateSource geforceerd vóór CommitEdit)
- ✅ Undo / Redo (Ctrl+Z / Ctrl+Y) — snapshot-gebaseerd, max 20 stappen
- ✅ Kopiëren / Plakken (Ctrl+C / Ctrl+V) — inclusief opmaak + systeem-klembord
- ✅ Delete: cel(len) leegmaken
- ✅ Auto-nummer (eerste kolom, slaat header-rijen over)
- ✅ Auto-breedte (kolommen aanpassen aan inhoud)

### Sjablonenmanager
- ✅ 3 ingebouwde sjablonen: Tekening titelblok, Stuklijst (BOM), Revisietabel
- ✅ Eigen sjablonen opslaan als JSON in `%APPDATA%\RhinoTable\templates\`
- ✅ Sjablonenmanager-venster: lijst links, naam + beschrijving rechts
- ✅ `ViewModel.LoadTemplate()` behoudt de Rhino-blokverwijzing bij laden sjabloon

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
- ✅ Rhino-arceerpatroon via `Hatch.Create` met schaal én rotatie (graden → radialen)

### Update-melding
- ✅ Async versie-check op Yak bij plugin-laden (3 sec vertraging na OnLoad)
- ✅ Max 1 check per 24 uur (timestamp in `%APPDATA%\RhinoTable\update_check.txt`)
- ✅ Popup-venster met huidige + nieuwe versie en "Update openen" knop
- ✅ JSON-parsing gefilterd op `"name" == "rhinotable"` (geen valse meldingen van dependencies)

### Help-venster
- ✅ "? Help" knop rechts in toolbar
- ✅ Tab 1: Sneltoetsen (Navigation / Editing / Undo & Redo)
- ✅ Tab 2: Functies-overzicht per groep (Structure, Text, Colors, Fill & Hatch, Borders, Templates, Import, Auto-update)
- ✅ GitHub-link in footer, versienummer automatisch uit assembly geladen

---

## Toolbar-indeling (ribbon-stijl)

```
[✓ Place  ] | STRUCTURE      | TEXT                    | ALIGNMENT        | BACKGROUND              | BORDER          | IMPORT      | UTILITIES       | ? Help
[📋 Sjab. ]   ↑ ↓ ＋ ✕ (rijen) B I X₂ X²  T▼            ≡L ≡C ≡R           V▼  H▼  □None  ■Solid     ✕ □ ■ — ═  R▼     📄 CSV         1,2,3 Auto-nr
               ← → ＋ ✕ (kols)  Font   Size               ⊤⊥⊣⊢ ↵            ⬚ Hatch▼  ↺                ⊤ ⊥ ⊣ ⊢       📊 Excel
               H Header row      ⊞→ ⊞↓  ↔ Auto-width       ≡L ≡C ≡R                                                    🔗 Link
                                                                              Scale [dropdown]                            🔄 Refresh
                                                                              Rot°  [dropdown]
```

**Eigenschappenstrip** (onder toolbar): `Tafelnaam | Breedte (mm) | Hoogte (mm)`

---

## Kleurpopups — hoe ze werken

Alle 4 kleur-dropdowns (T▼ tekst, V▼ vulling, H▼ hatch, R▼ rand) gebruiken hetzelfde patroon:

1. Een `ToggleButton` met `x:Name` in de toolbar
2. Een `<Popup>` met `IsOpen="{Binding IsChecked, ElementName=..., Mode=TwoWay}"` en `StaysOpen="False"`
3. Bovenaan: "Recent" rij (zichtbaar zodra ≥1 kleur is gebruikt) — gedeeld via `RecentColors` ObservableCollection
4. Elke kleurvlak-knop heeft `Click="ColorPopupButton_Click"` + `Tag="{Binding ElementName=...Toggle}"`
5. Recente kleuren gebruiken `Click="RecentColor_Click"` + `Tag="ToggleButtonName"` (string, opgezocht via `FindName`)

---

## Undo/Redo — hoe het werkt

- `PushUndoSnapshot()` serialiseert `_tableData` naar JSON vóór elke bewerking
- `_undoStack` en `_redoStack` zijn `Stack<string>`, max 20 stappen
- Deduplicatie: als nieuwe snapshot gelijk is aan top van stack → niet pushen
- Snapshot voor tekstbewerking: gepusht in `TableGrid_CellEditEnding` (Commit) vóórdat de LostFocus-binding schrijft
- Ctrl+Z roept eerst `CancelEdit()` aan zodat lopende TextBox-tekst niet over de herstelde snapshot heen wordt geschreven

---

## Copy/Paste — hoe het werkt

- `CopyCommand` (Ctrl+C): slaat `List<ClipCell>` op met relatieve positie + kloon van `TableCellData`
- Zet ook tab-gescheiden tekst op het Windows-klembord (voor Excel-compatibiliteit)
- `PasteCommand` (Ctrl+V): plakt intern klembord op huidige cel, of valt terug op systeem-klembord
- `CloneCell` / `CopyProperties` kopiëren ALLE velden inclusief randen, arcering en terugloop

---

## Kritieke implementatie-details

### Tab/pijl navigatie — tekst verdwijnt (OPGELOST)
**Oorzaak**: `DataGridTemplateColumn.CommitCellEdit()` forceert `UpdateSourceTrigger = LostFocus` bindings NIET.  
**Oplossing**: Vóór elke `CommitEdit`-aanroep in `PreviewKeyDown`:
```csharp
if (Keyboard.FocusedElement is TextBox editBox)
    editBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
```

### Insert Row/Column — verkeerde positie + inhoud gekopieerd (OPGELOST)
**Oorzaak**: `RebuildGridItems(syncFirst: true)` schreef OLD GridItems terug naar het al-gewijzigde model.  
**Oplossing**: Alle structurele mutaties (insert, remove, move) gebruiken `RebuildGridItems(syncFirst: false)`.

### Rhino-arceerpatroon rotatie
- `Hatch.Create(boundary, patternIndex, rotationRadians, scale, tolerance)`
- `HatchRotation` opgeslagen in graden, omgezet: `cell.HatchRotation * Math.PI / 180.0`

### RhinoTableDrawer — tekstplaatsing
- Anker Y-positie hangt af van `VerticalAlignment`: `y0-margin` (Top), `(y0+y1)/2` (Middle), `y1+margin` (Bottom)
- `GetJustification(h, v)` combineert beide richtingen naar `TextJustification` enum

### ObservableRow — celreferenties
```csharp
Cells = new ObservableCollection<TableCellData>(row.Cells);
```
Bevat dezelfde `TableCellData`-objectreferenties als `_tableData.Rows[r].Cells` — wijzigingen via binding raken direct het model.

### Popup DataContext
- Popups erven DataContext NIET automatisch via de visuele boom
- DataContext expliciet gezet: `DataContext="{Binding DataContext, ElementName=EditorWindow}"`

---

## Bekende beperkingen / TODO-lijst

| Item | Prioriteit | Notitie |
|---|---|---|
| Randvisualisatie in WPF-editor | Middel | Waarden opgeslagen, zichtbaar in Rhino, niet in editor |
| Export naar Excel/CSV | Laag | Alleen import geïmplementeerd |
| Rechtermuisknop-menu | Laag | Handig maar niet kritiek |
| Drag-and-drop rijvolgorde | Laag | Nu via toolbar-knoppen; drag-drop is complex in WPF DataGrid |
| Icoontjes op toolbar-knoppen | Laag | Nu tekst/symbolen; SVG-iconen verbeteren look & feel |

---

## Build & Deploy (lokaal)

```powershell
# Debug-build (voor ontwikkeling in Rhino)
cd "D:\03_code\02_rhino\Table plugin\Freeks table plugin"
dotnet build --configuration Debug

# Verwachte output: Build succeeded, 3 warnings (NU1701 - niet kritiek), 0 errors
```

---

## Yak (Rhino Package Manager)

Huidige versie: **1.0.2**

### Publicatieproces

```powershell
# 1. Versie ophogen in .csproj → <Version>x.x.x</Version>

# 2. Release-build
cd "D:\03_code\02_rhino\Table plugin\Freeks table plugin"
dotnet build --configuration Release

# 3. Kopieer bestanden naar dist-map
$src = "Freeks table plugin\bin\Release\net7.0-windows"
$dst = "yak-dist"
Copy-Item "$src\Freeks table plugin.rhp" "$dst\RhinoTable.rhp"
# DLL's: RhinoTable.Core.dll, RhinoTable.UI.dll,
#         ClosedXML.dll, CsvHelper.dll, DocumentFormat.OpenXml.dll,
#         ExcelNumberFormat.dll, Irony.dll, SixLabors.Fonts.dll, XLParser.dll

# 4. Versie in manifest bijwerken: yak-dist\manifest.yml → version: x.x.x

# 5. Build en push
cd yak-dist
& "C:\Program Files\Rhino 8\System\Yak.exe" build
& "C:\Program Files\Rhino 8\System\Yak.exe" push rhinotable-x.x.x-rh8_0-any.yak
```

### Yak-locaties

- Yak CLI: `C:\Program Files\Rhino 8\System\Yak.exe`
- Dist-map: `D:\03_code\02_rhino\Table plugin\Freeks table plugin\yak-dist\`
- Gepubliceerd als: `rhinotable` op `https://yak.rhino3d.com/`
- ⚠️ Waarschuwing "Content name doesn't match manifest" — cosmetisch, geen effect op installatie

### Installatie

```
Rhino 8 → _PackageManager → zoek "rhinotable" → Install → herstart Rhino
```

Commands na installatie: `TableCreate`, `TableEdit`, `TableSync`
