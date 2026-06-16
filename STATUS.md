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
│       └── TemplateManager.cs    ← TableTemplate model + opslaan/laden/verwijderen in %APPDATA%\RhinoTable\templates\
├── RhinoTable.UI/
│   ├── ViewModels/
│   │   └── TableEditorViewModel.cs ← Alle commands + undo/redo/clipboard + tabellogica
│   ├── Views/
│   │   ├── TableEditorWindow.xaml        ← Ribbon-toolbar + DataGrid + eigenschappenstrip
│   │   ├── TableEditorWindow.xaml.cs     ← Code-behind: kolommen bouwen, keyboard-handling
│   │   ├── ImportProgressWindow.xaml     ← Voortgangsvenster bij CSV/Excel-import
│   │   ├── UpdateNotificationWindow.xaml ← Popup bij beschikbare update (opent PackageManager)
│   │   ├── TemplateManagerWindow.xaml    ← Sjablonenmanager: laden / opslaan / verwijderen
│   │   └── SaveTemplateDialog.xaml       ← Kleine input-dialog voor sjabloonnaam + beschrijving
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
| `MergeDown` | int | Aantal cellen omlaag samenvoegen (⊞↓ knop) |
| `HatchPatternName` | string? | Naam Rhino-arceerpatroon (bijv. "Hatch1") |
| `HatchColor` | string? | Kleur van het arceerpatroon (hex) |
| `HatchScale` | double | Schaal van het Rhino-arceerpatroon (standaard 1.0) |
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
- ✅ Ongedaan maken / Opnieuw (Ctrl+Z / Ctrl+Y) — snapshot-gebaseerd, max 100 stappen
- ✅ Undo-snapshot op `CellEditEnding` (niet per toetsaanslag) → elke bevestigde celbewerking = 1 stap
- ✅ Deduplicatie: identieke snapshots worden niet gestapeld
- ✅ Ctrl+Z annuleert lopende celbewerking vóór herstel (geen spurious commit)
- ✅ Kopiëren / Plakken (Ctrl+C / Ctrl+V) — inclusief opmaak + systeem-klembord
- ✅ Auto-nummer (eerste kolom, slaat header-rijen over)
- ✅ Auto-breedte (kolommen aanpassen aan inhoud)

### Structuur (uitgebreid 2026-06-15)
- ✅ Cellen horizontaal samenvoegen (⊞→, meerdere cellen tegelijk, toggle = ontkoppelen)
- ✅ Cellen verticaal samenvoegen (⊞↓, nieuw)
- ✅ Kolom herordenen via drag-and-drop op kolomkop (DataGrid `CanUserReorderColumns`)
- ✅ Rij omhoog/omlaag verplaatsen (↑↑ ↓↓ knoppen in STRUCTURE)
- ✅ Kolom links/rechts verplaatsen (←← →→ knoppen in STRUCTURE)
- ✅ Kolombreedte slepen via kolomkopscheider (sync naar model bij Plaatsen)
- ✅ Rijhoogte slepen via rijkopscheider (sync naar model bij Plaatsen)
- ✅ Header-rij (alleen rij 1): markeert rij, past automatisch vet + 0,5 mm onderrand toe
- ✅ Tab / Shift+Tab: commit cel en spring naar volgende/vorige
- ⚠️ Pijltoetsennavigatie: code aanwezig maar werkt nog niet correct (volgende sessie debuggen)

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

### Update-melding (nieuw 2026-06-16)
- ✅ Async versie-check op Yak bij plugin-laden (3 sec vertraging na OnLoad)
- ✅ Max 1 check per 24 uur (timestamp in `%APPDATA%\RhinoTable\update_check.txt`)
- ✅ Popup-venster met huidige + nieuwe versie en "Update openen" knop (opent `_PackageManager`)
- ✅ Stille mislukking bij geen netwerk of API-formaat onbekend

### Sjablonenmanager (nieuw 2026-06-16)
- ✅ 3 ingebouwde sjablonen: Tekening titelblok, Stuklijst (BOM), Revisietabel
- ✅ Eigen sjablonen opslaan als JSON in `%APPDATA%\RhinoTable\templates\`
- ✅ Sjablonenmanager-venster: lijst links, naam + beschrijving rechts
- ✅ Laden, Opslaan als sjabloon, Verwijderen (eigen sjablonen)
- ✅ Knop "TEMPLATES" in toolbar van TableEditorWindow
- ✅ `ViewModel.LoadTemplate()` behoudt de Rhino-blokverwijzing bij laden sjabloon

---

## Toolbar-indeling (ribbon-stijl) — bijgewerkt 2026-06-15

```
[✓ Place] | STRUCTURE                    | TEXT                        | ALIGNMENT               | BACKGROUND         | BORDER | IMPORT      | UTILITIES
            ↑ ↓ ＋ ✕  (rijen)             B I X₂ X²  T▼               ≡L ≡C ≡R | ⊤ ⊢ ⊥ | ↵       V▼ H▼ □ ■           ✕□■—═ R▼   📄 CSV       1,2,3 Auto-nr
            ← → ＋ ✕  (kols)             Font ComboBox  Size           ⊞→Merge  ⊞↓Merge              Hatch▼  Scale                      📊 Excel     ↔ Auto-breedte
            H Header row                                                                                                                  🔗 Link
            ↑↑ ↓↓ ←← →→  (verplaatsen)                                                                                                  🔄 Refresh
```

**Eigenschappenstrip** (onder toolbar):
`Tafelnaam | Breedte (mm) | Hoogte (mm)`

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
- `_undoStack` en `_redoStack` zijn `Stack<string>`, max 100 stappen
- Deduplicatie: als nieuwe snapshot gelijk is aan top van stack → niet pushen
- Snapshot voor tekstbewerking: wordt gepusht in `TableGrid_CellEditEnding` (Commit) vóórdat de LostFocus-binding de tekst naar het model schrijft → elke bevestigde cel = 1 undo-stap
- Ctrl+Z roept eerst `CancelEdit()` aan zodat lopende TextBox-tekst niet over de herstelde snapshot heen wordt geschreven
- Alle opmaakacties (bold, kleur, etc.) roepen `PushUndoSnapshot()` aan vóór de wijziging

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
| ⚠️ Pijltoetsnavigatie werkt niet | Hoog | Code staat in PreviewKeyDown maar navigeert nog niet correct; debuggen volgende sessie |
| Randvisualisatie in WPF-editor | Middel | Waarden worden opgeslagen, alleen zichtbaar in Rhino |
| Export naar Excel/CSV | Laag | Alleen import is geïmplementeerd |
| Rechtermuisknop-menu | Laag | Handig maar niet kritiek |
| Plakken vanuit Windows-klembord (Excel) | Laag | Tab-tekst import vanuit externe kopie |
| Drag-and-drop rijvolgorde | Laag | Nu via ↑↑↓↓ knoppen; drag-drop is complex in WPF DataGrid |

---

## Build & Deploy (lokaal)

```powershell
# Debug-build (voor ontwikkeling in Rhino)
cd "D:\03_code\02_rhino\Table plugin\Freeks table plugin"
dotnet build --configuration Debug

# Verwachte output: Build succeeded, 5 warnings (NU1701 - niet kritiek), 0 errors

# Plugin laden in Rhino:
# Rhino → Tools → Options → Plugins → Install → kies .rhp uit bin/Debug map
# Commands: TableCreate, TableEdit, TableSync
```

---

## Yak (Rhino Package Manager)

Gepubliceerd op 2026-06-15. Versie **1.0.1** — eerste werkende versie (1.0.0 was kapot: DLL's ontbraken).

### Publicatieproces (voor volgende update)

```powershell
# 1. Versie ophogen in .csproj
#    Freeks table plugin\Freeks table plugin.csproj → <Version>1.0.2</Version>

# 2. Release-build
cd "D:\03_code\02_rhino\Table plugin\Freeks table plugin"
dotnet build --configuration Release

# 3. Kopieer .rhp + alle DLL's naar dist-map
$src = "Freeks table plugin\bin\Release\net7.0-windows"
$dst = "yak-dist"
Copy-Item "$src\Freeks table plugin.rhp" "$dst\RhinoTable.rhp"
# DLL's die mee moeten:
#   RhinoTable.Core.dll, RhinoTable.UI.dll,
#   ClosedXML.dll, CsvHelper.dll, DocumentFormat.OpenXml.dll,
#   ExcelNumberFormat.dll, Irony.dll, SixLabors.Fonts.dll, XLParser.dll

# 4. Versie in manifest bijwerken
#    yak-dist\manifest.yml → version: 1.0.2

# 5. Build en push
cd yak-dist
& "C:\Program Files\Rhino 8\System\Yak.exe" build
& "C:\Program Files\Rhino 8\System\Yak.exe" push rhinotable-1.0.2-rh8_0-any.yak
```

### manifest.yml (huidige staat)

```yaml
---
name: rhinotable
version: 1.0.1
authors:
- Freek Baars
description: Create and edit Excel-like annotation tables directly in the Rhino 8 viewport.
url: https://github.com/Freekbaars/Freeks-table-plugin
keywords:
- table
- annotation
- excel
- drawing
```

### Installatie door collega's

```
Rhino 8 → _PackageManager → zoek "rhinotable" → Install → herstart Rhino
```

Commands na installatie: `TableCreate`, `TableEdit`, `TableSync`

### Yak-locaties

- Yak CLI: `C:\Program Files\Rhino 8\System\Yak.exe`
- Dist-map: `D:\03_code\02_rhino\Table plugin\Freeks table plugin\yak-dist\`
- Gepubliceerd als: `rhinotable` op `https://yak.rhino3d.com/`
- ⚠️ Waarschuwing "Content name doesn't match manifest: 'Freeks table plugin' != 'rhinotable'" — is cosmetisch, heeft geen invloed op installatie

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
