using Microsoft.Win32;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;
using RhinoTable.Core.Import;
using RhinoTable.Core.Layout;
using RhinoTable.Core.Models;
using RhinoTable.Core.Settings;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Threading.Tasks;

namespace RhinoTable.UI.ViewModels
{
    public class TableEditorViewModel : INotifyPropertyChanged
    {
        private readonly RhinoDoc _doc;
        private TableData _tableData;
        private TableCellData? _selectedCell;
        private List<TableCellData> _selectedCells = new();
        private List<(int Row, int Col)> _selectedCellPositions = new();
        private int _selectedRow = -1;
        private int _selectedCol = -1;
        private string _statusText = "Ready";

        // ── Undo/redo ─────────────────────────────────────────────────────────
        private readonly Stack<string> _undoStack = new();
        private readonly Stack<string> _redoStack = new();
        private bool _suppressUndo;

        // ── Klembord ──────────────────────────────────────────────────────────
        private record ClipCell(int RelRow, int RelCol, TableCellData Data);
        private List<ClipCell>? _clipboard;

        // ── Randen ────────────────────────────────────────────────────────────
        private float  _borderThickness = 0.25f;
        private string _borderColor     = "#000000";

        public TableData TableData => _tableData;
        public ObservableCollection<ObservableRow> GridItems { get; private set; } = new();

        public int CurrentRow => _selectedRow;
        public int CurrentCol => _selectedCol;
        public IReadOnlyList<(int Row, int Col)> SelectedCellPositions => _selectedCellPositions;

        public event Action? ColumnsChanged;
        public event Action? RequestClose;
        public event Action? GridRefreshRequested;

        // ── Commands ──────────────────────────────────────────────────────────
        public ICommand PlaceTableCommand      { get; }
        public ICommand ImportCsvCommand       { get; }
        public ICommand ImportExcelCommand     { get; }
        public ICommand AutoWidthCommand       { get; }
        public ICommand AutoNumberCommand      { get; }

        public ICommand AddRowCommand          { get; }
        public ICommand RemoveRowCommand       { get; }
        public ICommand InsertRowAboveCommand  { get; }
        public ICommand InsertRowBelowCommand  { get; }

        public ICommand AddColumnCommand       { get; }
        public ICommand RemoveColumnCommand    { get; }
        public ICommand InsertColumnLeftCommand  { get; }
        public ICommand InsertColumnRightCommand { get; }

        public ICommand MergeCellsCommand      { get; }
        public ICommand MergeDownCommand       { get; }
        public ICommand BoldCommand            { get; }
        public ICommand ItalicCommand          { get; }
        public ICommand InsertSubCommand       { get; }
        public ICommand InsertSupCommand       { get; }
        public ICommand AlignLeftCommand       { get; }
        public ICommand AlignCenterCommand     { get; }
        public ICommand AlignRightCommand      { get; }

        // Kleur & patroon
        public ICommand SetTextColorCommand       { get; }
        public ICommand SetFillColorCommand       { get; }
        public ICommand SetHatchColorCommand      { get; }
        public ICommand SetFillPatternCommand     { get; }
        public ICommand SetHatchPatternNameCommand { get; }

        // Rhino arceerpatronen (geladen uit het document)
        private List<string> _availableHatchPatterns = new();
        public List<string> AvailableHatchPatterns => _availableHatchPatterns;

        // Recente kleuren — gedeeld over alle 4 kleurpickers, max 12
        public ObservableCollection<string> RecentColors { get; } = new();

        // Systeemfonts — eenmalig geladen bij opstart
        public static List<string> AvailableFonts { get; } =
            System.Windows.Media.Fonts.SystemFontFamilies
                .Select(f => f.Source)
                .Where(s => !string.IsNullOrEmpty(s))
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();

        // Excel-koppeling
        public ICommand LinkExcelCommand       { get; }
        public ICommand RefreshLinkedCommand   { get; }

        // Undo / Redo / Copy / Paste
        public ICommand UndoCommand            { get; }
        public ICommand RedoCommand            { get; }
        public ICommand CopyCommand            { get; }
        public ICommand PasteCommand           { get; }

        // Verticale uitlijning
        public ICommand AlignTopCommand        { get; }
        public ICommand AlignMiddleCommand     { get; }
        public ICommand AlignBottomCommand     { get; }

        // Woordterugloop
        public ICommand ToggleWordWrapCommand  { get; }

        // Randen
        public ICommand BorderNoneCommand      { get; }
        public ICommand BorderAllCommand       { get; }
        public ICommand BorderOutsideCommand   { get; }
        public ICommand BorderTopCommand       { get; }
        public ICommand BorderBottomCommand    { get; }
        public ICommand BorderLeftCommand      { get; }
        public ICommand BorderRightCommand     { get; }
        public ICommand SetBorderThicknessCommand { get; }
        public ICommand SetBorderColorCommand  { get; }

        // Header rij
        public ICommand ToggleHeaderRowCommand { get; }

        // Rij/kolom verplaatsen
        public ICommand MoveRowUpCommand       { get; }
        public ICommand MoveRowDownCommand     { get; }
        public ICommand MoveColumnLeftCommand  { get; }
        public ICommand MoveColumnRightCommand { get; }

        // Grid-grootte synchronisatie (view → model vóór Place)
        public event Action? GridSyncRequested;

        // ── Bound properties ──────────────────────────────────────────────────
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; Notify(); }
        }

        // True als we een bestaand blok bijwerken (TableEdit flow)
        public bool IsEditingExisting => _tableData.SourceObjectId.HasValue;

        // Tafelnaam — gekoppeld aan TableData.TableName
        public string TableName
        {
            get => _tableData.TableName ?? string.Empty;
            set
            {
                _tableData.TableName = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
                Notify();
            }
        }

        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;

        public bool IsCurrentRowHeader =>
            _selectedRow >= 0 && _selectedRow < _tableData.Rows.Count
            && _tableData.Rows[_selectedRow].IsHeader;

        // Label voor de gekoppelde Excel — toont bestandsnaam of "(geen koppeling)"
        public string LinkedFileLabel => _tableData.LinkedExcelPath != null
            ? System.IO.Path.GetFileName(_tableData.LinkedExcelPath)
            : "(no link)";

        public bool HasLinkedExcel => _tableData.LinkedExcelPath != null;
        public string PlaceButtonLabel => IsEditingExisting ? "Update" : "Place";

        public double SelectedColumnWidth
        {
            get => _selectedCol >= 0 && _selectedCol < _tableData.ColumnWidths.Count
                ? _tableData.ColumnWidths[_selectedCol] : 30;
            set
            {
                if (_selectedCol >= 0 && _selectedCol < _tableData.ColumnWidths.Count && value > 0)
                { _tableData.ColumnWidths[_selectedCol] = value; ColumnsChanged?.Invoke(); UpdateStatus(); }
            }
        }

        public double SelectedRowHeight
        {
            get => _selectedRow >= 0 && _selectedRow < _tableData.RowHeights.Count
                ? _tableData.RowHeights[_selectedRow] : 8;
            set
            {
                if (_selectedRow >= 0 && _selectedRow < _tableData.RowHeights.Count && value > 0)
                { _tableData.RowHeights[_selectedRow] = value; ColumnsChanged?.Invoke(); UpdateStatus(); }
            }
        }

        public string SelectedCellFont
        {
            get => _selectedCell?.FontName ?? _tableData.DefaultFontName;
            set
            {
                if (string.IsNullOrWhiteSpace(value) || _selectedCells.Count == 0) return;
                PushUndoSnapshot();
                foreach (var cell in _selectedCells) cell.FontName = value;
                GridRefreshRequested?.Invoke();
            }
        }

        public string SelectedCellFontSize
        {
            get => (_selectedCell?.FontSize ?? _tableData.DefaultFontSize).ToString("F1");
            set
            {
                if (_selectedCells.Count == 0) return;
                if (double.TryParse(value.Replace(',', '.'),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double sz) && sz > 0)
                {
                    PushUndoSnapshot();
                    foreach (var cell in _selectedCells) cell.FontSize = sz;
                    GridRefreshRequested?.Invoke();
                }
            }
        }

        public string SelectedHatchScale
        {
            get => (_selectedCell?.HatchScale ?? 1.0).ToString("F1");
            set
            {
                if (_selectedCells.Count == 0) return;
                if (double.TryParse(value.Replace(',', '.'),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double s) && s > 0)
                {
                    PushUndoSnapshot();
                    foreach (var cell in _selectedCells) cell.HatchScale = s;
                    GridRefreshRequested?.Invoke();
                }
            }
        }

        public string SelectedHatchRotation
        {
            get => (_selectedCell?.HatchRotation ?? 0.0).ToString("F0");
            set
            {
                if (_selectedCells.Count == 0) return;
                if (double.TryParse(value.Replace(',', '.'),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double r))
                {
                    PushUndoSnapshot();
                    foreach (var cell in _selectedCells) cell.HatchRotation = r;
                    GridRefreshRequested?.Invoke();
                }
            }
        }

        // ── Constructor ───────────────────────────────────────────────────────
        public TableEditorViewModel(RhinoDoc doc, TableData tableData)
        {
            _doc = doc;
            _tableData = tableData;

            foreach (var c in RecentColorsManager.Load())
                RecentColors.Add(c);

            PlaceTableCommand       = new RelayCommand(PlaceTable);
            ImportCsvCommand        = new RelayCommand(ImportCsv);
            ImportExcelCommand      = new RelayCommand(ImportExcel);
            AutoWidthCommand        = new RelayCommand(ApplyAutoWidth);
            AutoNumberCommand       = new RelayCommand(AutoNumber);

            AddRowCommand           = new RelayCommand(AddRow);
            RemoveRowCommand        = new RelayCommand(RemoveRow,       () => _selectedRow >= 0);
            InsertRowAboveCommand   = new RelayCommand(InsertRowAbove,  () => _selectedRow >= 0);
            InsertRowBelowCommand   = new RelayCommand(InsertRowBelow,  () => _selectedRow >= 0);

            AddColumnCommand        = new RelayCommand(AddColumn);
            RemoveColumnCommand     = new RelayCommand(RemoveColumn,    () => _selectedCol >= 0);
            InsertColumnLeftCommand = new RelayCommand(InsertColLeft,   () => _selectedCol >= 0);
            InsertColumnRightCommand= new RelayCommand(InsertColRight,  () => _selectedCol >= 0);

            MergeCellsCommand       = new RelayCommand(MergeCells,      () => _selectedCells.Count > 0);
            MergeDownCommand        = new RelayCommand(MergeDown,       () => _selectedCells.Count > 0);
            BoldCommand             = new RelayCommand(ToggleBold,      () => _selectedCells.Count > 0);
            ItalicCommand           = new RelayCommand(ToggleItalic,    () => _selectedCells.Count > 0);
            InsertSubCommand        = new RelayCommand(InsertSubscript,  () => _selectedCell != null);
            InsertSupCommand        = new RelayCommand(InsertSuperscript,() => _selectedCell != null);
            AlignLeftCommand        = new RelayCommand(() => SetAlignment(HorizontalAlignment.Left),   () => _selectedCells.Count > 0);
            AlignCenterCommand      = new RelayCommand(() => SetAlignment(HorizontalAlignment.Center), () => _selectedCells.Count > 0);
            AlignRightCommand       = new RelayCommand(() => SetAlignment(HorizontalAlignment.Right),  () => _selectedCells.Count > 0);

            SetTextColorCommand   = new RelayCommand<string?>(color => {
                if (_selectedCells.Count == 0) return;
                PushUndoSnapshot();
                string? val = string.IsNullOrEmpty(color) ? null : color;
                foreach (var cell in _selectedCells) cell.TextColor = val;
                if (!string.IsNullOrEmpty(color)) AddRecentColor(color!);
                GridRefreshRequested?.Invoke();
            });
            SetFillColorCommand   = new RelayCommand<string?>(color => {
                if (_selectedCells.Count == 0) return;
                PushUndoSnapshot();
                string? val = string.IsNullOrEmpty(color) ? null : color;
                foreach (var cell in _selectedCells)
                {
                    cell.BackgroundColor = val;
                    if (val == null) cell.FillPattern = 0;
                    else if (cell.FillPattern == 0) cell.FillPattern = 1;
                }
                if (!string.IsNullOrEmpty(color)) AddRecentColor(color!);
                GridRefreshRequested?.Invoke();
            });
            SetHatchColorCommand = new RelayCommand<string?>(color => {
                if (_selectedCells.Count == 0) return;
                PushUndoSnapshot();
                string? val = string.IsNullOrEmpty(color) ? null : color;
                foreach (var cell in _selectedCells) cell.HatchColor = val;
                if (!string.IsNullOrEmpty(color)) AddRecentColor(color!);
                GridRefreshRequested?.Invoke();
            });
            SetFillPatternCommand = new RelayCommand<string?>(param => {
                if (_selectedCells.Count == 0 || !int.TryParse(param, out int pat)) return;
                PushUndoSnapshot();
                foreach (var cell in _selectedCells)
                {
                    cell.FillPattern = pat;
                    if (pat == 0) cell.HatchPatternName = null;
                    if (pat > 0 && string.IsNullOrEmpty(cell.BackgroundColor))
                        cell.BackgroundColor = "#4472C4";
                }
                GridRefreshRequested?.Invoke();
            });
            SetHatchPatternNameCommand = new RelayCommand<string?>(name => {
                if (_selectedCells.Count == 0) return;
                PushUndoSnapshot();
                string? val = string.IsNullOrEmpty(name) || name == "(no hatch pattern)" ? null : name;
                foreach (var cell in _selectedCells)
                {
                    cell.HatchPatternName = val;
                    if (val == null && cell.FillPattern > 1)
                        cell.FillPattern = 0; // Verwijder legacy-patrooncode bij resetten
                }
                GridRefreshRequested?.Invoke();
            });

            RefreshHatchPatterns();

            LinkExcelCommand     = new RelayCommand(LinkExcel);
            RefreshLinkedCommand = new RelayCommand(RefreshLinked, () => _tableData.LinkedExcelPath != null);

            UndoCommand = new RelayCommand(Undo, () => _undoStack.Count > 0);
            RedoCommand = new RelayCommand(Redo, () => _redoStack.Count > 0);
            CopyCommand = new RelayCommand(CopySelection,   () => _selectedCells.Count > 0);
            PasteCommand= new RelayCommand(PasteClipboard,  () => _selectedRow >= 0 && _selectedCol >= 0);

            AlignTopCommand    = new RelayCommand(() => SetVAlignment(VerticalAlignment.Top),    () => _selectedCells.Count > 0);
            AlignMiddleCommand = new RelayCommand(() => SetVAlignment(VerticalAlignment.Middle), () => _selectedCells.Count > 0);
            AlignBottomCommand = new RelayCommand(() => SetVAlignment(VerticalAlignment.Bottom), () => _selectedCells.Count > 0);

            ToggleWordWrapCommand = new RelayCommand(ToggleWordWrap, () => _selectedCells.Count > 0);

            BorderNoneCommand    = new RelayCommand(() => ApplyBorders(false, false, false, false, clear: true), () => _selectedCells.Count > 0);
            BorderAllCommand     = new RelayCommand(() => ApplyBorders(true,  true,  true,  true),               () => _selectedCells.Count > 0);
            BorderOutsideCommand = new RelayCommand(ApplyBorderOutside,                                          () => _selectedCells.Count > 0);
            BorderTopCommand     = new RelayCommand(() => ToggleBorderSide(top: true),    () => _selectedCells.Count > 0);
            BorderBottomCommand  = new RelayCommand(() => ToggleBorderSide(bottom: true), () => _selectedCells.Count > 0);
            BorderLeftCommand    = new RelayCommand(() => ToggleBorderSide(left: true),   () => _selectedCells.Count > 0);
            BorderRightCommand   = new RelayCommand(() => ToggleBorderSide(right: true),  () => _selectedCells.Count > 0);
            SetBorderThicknessCommand = new RelayCommand<string?>(param =>
            {
                if (float.TryParse(param, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out float t))
                    _borderThickness = t;
            });
            SetBorderColorCommand = new RelayCommand<string?>(color =>
            {
                if (!string.IsNullOrEmpty(color))
                {
                    _borderColor = color;
                    AddRecentColor(color);
                }
            });

            ToggleHeaderRowCommand = new RelayCommand(ToggleHeaderRow, () => _selectedRow == 0);

            MoveRowUpCommand       = new RelayCommand(MoveRowUp,      () => _selectedRow > 0);
            MoveRowDownCommand     = new RelayCommand(MoveRowDown,     () => _selectedRow >= 0 && _selectedRow < _tableData.Rows.Count - 1);
            MoveColumnLeftCommand  = new RelayCommand(MoveColumnLeft,  () => _selectedCol > 0);
            MoveColumnRightCommand = new RelayCommand(MoveColumnRight, () => _selectedCol >= 0 && _selectedCol < _tableData.ColumnWidths.Count - 1);

            RebuildGridItems();
        }

        // ── Selection ─────────────────────────────────────────────────────────

        // Wordt aangeroepen door de view met alle geselecteerde cellen.
        public void SetSelectedCells(List<(int Row, int Col)> cells)
        {
            _selectedCellPositions = cells;
            _selectedCells = cells
                .Select(rc => GetCell(rc.Row, rc.Col))
                .Where(c => c != null)
                .Select(c => c!)
                .ToList();

            // Primaire cel (eerste in selectie) voor de eigenschappenstrip
            if (cells.Count > 0)
            {
                _selectedRow  = cells[0].Row;
                _selectedCol  = cells[0].Col;
                _selectedCell = GetCell(_selectedRow, _selectedCol);
            }
            else
            {
                _selectedRow  = -1;
                _selectedCol  = -1;
                _selectedCell = null;
            }

            UpdateStatus();
            Notify(nameof(SelectedColumnWidth));
            Notify(nameof(SelectedRowHeight));
            Notify(nameof(SelectedCellFont));
            Notify(nameof(SelectedCellFontSize));
            Notify(nameof(SelectedHatchScale));
            Notify(nameof(SelectedHatchRotation));
            Notify(nameof(IsCurrentRowHeader));
        }

        public void ClearSelectedCells(int row, int col)
        {
            var cell = GetCell(row, col);
            if (cell != null) cell.Text = string.Empty;
        }

        private TableCellData? GetCell(int r, int c) =>
            r >= 0 && r < _tableData.Rows.Count &&
            c >= 0 && c < _tableData.Rows[r].Cells.Count
                ? _tableData.Rows[r].Cells[c] : null;

        private void UpdateStatus()
        {
            if (_selectedCells.Count == 0) { StatusText = "No cell selected"; return; }
            if (_selectedCells.Count > 1)  { StatusText = $"{_selectedCells.Count} cells selected"; return; }
            string col = ColLetter(_selectedCol);
            double w = _selectedCol < _tableData.ColumnWidths.Count ? _tableData.ColumnWidths[_selectedCol] : 0;
            double h = _selectedRow < _tableData.RowHeights.Count   ? _tableData.RowHeights[_selectedRow]   : 0;
            StatusText = $"{col}{_selectedRow + 1}   width {w:F1} mm  ×  height {h:F1} mm";
        }

        // ── Sjabloon laden ────────────────────────────────────────────────────
        public void LoadTemplate(TableData templateData)
        {
            // Behoud de Rhino-blokverwijzing van de huidige tabel (voor TableEdit-flow)
            var sourceId = _tableData.SourceObjectId;
            _tableData = templateData;
            _tableData.SourceObjectId = sourceId;

            _undoStack.Clear();
            _redoStack.Clear();
            _selectedRow = -1;
            _selectedCol = -1;
            _selectedCell = null;
            _selectedCells.Clear();
            _selectedCellPositions.Clear();

            Notify(nameof(TableName));
            Notify(nameof(IsEditingExisting));
            Notify(nameof(PlaceButtonLabel));
            RebuildGridItems(syncFirst: false);
        }

        // ── Grid rebuild ──────────────────────────────────────────────────────
        // syncFirst=false bij een volledige vervanging van _tableData (import),
        // want anders overschrijft SyncObservableToModel de zojuist ingeladen
        // gegevens met de inhoud van het oude raster.
        public void RebuildGridItems(bool syncFirst = true)
        {
            if (syncFirst) SyncObservableToModel();
            GridItems.Clear();
            for (int r = 0; r < _tableData.Rows.Count; r++)
                GridItems.Add(new ObservableRow(_tableData.Rows[r], r));
            ColumnsChanged?.Invoke();
        }

        private void SyncObservableToModel()
        {
            for (int r = 0; r < GridItems.Count && r < _tableData.Rows.Count; r++)
            {
                var obs = GridItems[r];
                for (int c = 0; c < obs.Cells.Count && c < _tableData.Rows[r].Cells.Count; c++)
                    _tableData.Rows[r].Cells[c].Text = obs.Cells[c].Text;
            }
        }

        // Aangeroepen door de view vóór Place om gesleepte breedte/hoogte terug te schrijven.
        public void SyncColumnWidths(IReadOnlyList<double> widthsMm)
        {
            for (int i = 0; i < widthsMm.Count && i < _tableData.ColumnWidths.Count; i++)
                if (widthsMm[i] > 0) _tableData.ColumnWidths[i] = widthsMm[i];
        }

        public void SyncRowHeights(IReadOnlyList<double> heightsMm)
        {
            for (int i = 0; i < heightsMm.Count && i < _tableData.RowHeights.Count; i++)
                if (heightsMm[i] > 0) _tableData.RowHeights[i] = heightsMm[i];
        }

        // Aangeroepen door de view na kolom drag-and-drop; newOrder[i] = oude kolomindex op nieuwe positie i.
        public void ReorderColumns(List<int> newOrder)
        {
            if (newOrder.Count != _tableData.ColumnWidths.Count) return;
            PushUndoSnapshot();
            var newWidths = newOrder.Select(i => _tableData.ColumnWidths[i]).ToList();
            for (int i = 0; i < newWidths.Count; i++) _tableData.ColumnWidths[i] = newWidths[i];
            foreach (var row in _tableData.Rows)
            {
                var newCells = newOrder.Select(i => row.Cells[i]).ToList();
                row.Cells.Clear();
                row.Cells.AddRange(newCells);
            }
            RebuildGridItems();
        }

        // ── Place ─────────────────────────────────────────────────────────────
        private void PlaceTable()
        {
            GridSyncRequested?.Invoke(); // View syncs dragged column widths + row heights back
            SyncObservableToModel();

            Point3d origin;
            if (_tableData.SourceObjectId.HasValue)
            {
                var old = _doc.Objects.FindId(_tableData.SourceObjectId.Value) as InstanceObject;
                if (old != null)
                {
                    var xf = old.InstanceXform;
                    origin = new Point3d(xf.M03, xf.M13, xf.M23);
                    _doc.Objects.Delete(_tableData.SourceObjectId.Value, quiet: true);
                }
                else origin = Point3d.Origin;
                _tableData.SourceObjectId = null;
            }
            else
            {
                var gp = new GetPoint();
                gp.SetCommandPrompt("Click the insertion point of the table");
                if (gp.Get() != GetResult.Point) return;
                origin = gp.Point();
            }

            new RhinoTableDrawer().Draw(_doc, _tableData, origin);
            RequestClose?.Invoke();
        }

        // ── Import ────────────────────────────────────────────────────────────
        public event Action<string, int>? ImportStarted;   // (bestandsnaam, maxRijen)
        public event Action<int>?         ImportProgress;  // rijen geladen
        public event Action?              ImportFinished;

        private async void ImportCsv()
        {
            var dlg = new OpenFileDialog { Filter = "CSV (*.csv)|*.csv|Alle bestanden|*.*" };
            if (dlg.ShowDialog() != true) return;

            string path = dlg.FileName;
            ImportStarted?.Invoke(System.IO.Path.GetFileName(path), CsvImporter.MaxRows);

            var progress = new Progress<int>(n => ImportProgress?.Invoke(n));
            TableData result = await Task.Run(() => new CsvImporter().Import(path, progress));

            _tableData = result;
            RebuildGridItems(syncFirst: false);
            ImportFinished?.Invoke();

            int loaded = result.Rows.Count;
            StatusText = loaded >= CsvImporter.MaxRows
                ? $"CSV loaded — row limit of {CsvImporter.MaxRows} reached. File may be larger."
                : $"CSV loaded — {loaded} rows.";
        }

        private async void ImportExcel()
        {
            var dlg = new OpenFileDialog { Filter = "Excel (*.xlsx)|*.xlsx|Alle bestanden|*.*" };
            if (dlg.ShowDialog() != true) return;
            await DoImportExcel(dlg.FileName, storeLink: false);
        }

        // Importeert Excel én slaat het pad op als gekoppeld bestand
        private async void LinkExcel()
        {
            var dlg = new OpenFileDialog { Filter = "Excel (*.xlsx)|*.xlsx|Alle bestanden|*.*" };
            if (dlg.ShowDialog() != true) return;
            await DoImportExcel(dlg.FileName, storeLink: true);
        }

        // Herlaadt van het gekoppelde Excel-bestand (zonder bestandsdialoog)
        private async void RefreshLinked()
        {
            string? path = _tableData.LinkedExcelPath;
            if (path == null || !System.IO.File.Exists(path))
            {
                StatusText = "Linked file not found.";
                return;
            }
            // Gebruik het opgeslagen werkblad zodat er geen dialog verschijnt bij refresh
            await DoImportExcel(path, storeLink: true, knownSheet: _tableData.LinkedExcelSheet);
        }

        private async Task DoImportExcel(string path, bool storeLink, string? knownSheet = null)
        {
            // Werkbladnamen ophalen om te bepalen of er een keuze nodig is
            List<string> sheets = await Task.Run(() => ExcelImporter.GetSheetNames(path));

            string? sheetName = knownSheet;
            if (sheetName == null && sheets.Count > 1)
            {
                var dlg = new RhinoTable.UI.Views.SheetSelectionDialog(sheets);
                if (dlg.ShowDialog() != true) return;
                sheetName = dlg.SelectedSheet;
            }

            ImportStarted?.Invoke(System.IO.Path.GetFileName(path), ExcelImporter.MaxRows);

            var progress = new Progress<int>(n => ImportProgress?.Invoke(n));
            TableData result = await Task.Run(() => new ExcelImporter().Import(path, sheetName, progress));

            if (storeLink)
            {
                result.LinkedExcelPath  = path;
                result.LinkedExcelSheet = sheetName;
            }
            result.SourceObjectId = _tableData.SourceObjectId;

            _tableData = result;
            RebuildGridItems(syncFirst: false);
            ImportFinished?.Invoke();

            Notify(nameof(LinkedFileLabel));
            Notify(nameof(HasLinkedExcel));
            StatusText = storeLink
                ? $"Excel linked: {System.IO.Path.GetFileName(path)} — {result.Rows.Count} rows"
                : $"Excel loaded — {result.Rows.Count} rows, {result.ColumnWidths.Count} columns.";
        }

        // ── Auto-breedte & auto-nummer ────────────────────────────────────────
        private void ApplyAutoWidth()
        {
            SyncObservableToModel();
            new AutoWidthCalculator().Apply(_tableData);
            ColumnsChanged?.Invoke();
            Notify(nameof(SelectedColumnWidth));
        }

        private void AutoNumber()
        {
            SyncObservableToModel();
            int n = 1;
            for (int r = 0; r < _tableData.Rows.Count; r++)
            {
                if (_tableData.Rows[r].IsHeader) continue;
                if (_tableData.Rows[r].Cells.Count > 0)
                    _tableData.Rows[r].Cells[0].Text = n.ToString();
                n++;
            }
            RebuildGridItems();
        }

        // ── Rijen ─────────────────────────────────────────────────────────────
        private void AddRow()
        {
            PushUndoSnapshot();
            var row = MakeRow();
            _tableData.Rows.Add(row);
            _tableData.RowHeights.Add(8.0);
            GridItems.Add(new ObservableRow(row, _tableData.Rows.Count - 1));
        }

        private void InsertRowAbove()
        {
            if (_selectedRow < 0) return;
            PushUndoSnapshot();
            var row = MakeRow();
            _tableData.Rows.Insert(_selectedRow, row);
            _tableData.RowHeights.Insert(_selectedRow, 8.0);
            RebuildGridItems(syncFirst: false);
        }

        private void InsertRowBelow()
        {
            PushUndoSnapshot();
            int at = _selectedRow >= 0 ? _selectedRow + 1 : _tableData.Rows.Count;
            var row = MakeRow();
            _tableData.Rows.Insert(at, row);
            _tableData.RowHeights.Insert(Math.Min(at, _tableData.RowHeights.Count), 8.0);
            RebuildGridItems(syncFirst: false);
        }

        private void RemoveRow()
        {
            if (_selectedRow < 0 || _selectedRow >= _tableData.Rows.Count) return;
            PushUndoSnapshot();
            _tableData.Rows.RemoveAt(_selectedRow);
            if (_selectedRow < _tableData.RowHeights.Count)
                _tableData.RowHeights.RemoveAt(_selectedRow);
            _selectedRow = -1; _selectedCell = null;
            RebuildGridItems(syncFirst: false);
        }

        private TableRowData MakeRow()
        {
            var row = new TableRowData();
            for (int c = 0; c < _tableData.ColumnWidths.Count; c++)
                row.Cells.Add(new TableCellData());
            return row;
        }

        // ── Kolommen ──────────────────────────────────────────────────────────
        private void AddColumn()
        {
            PushUndoSnapshot();
            _tableData.ColumnWidths.Add(30.0);
            foreach (var r in _tableData.Rows) r.Cells.Add(new TableCellData());
            RebuildGridItems();
        }

        private void InsertColLeft()
        {
            PushUndoSnapshot();
            int at = _selectedCol >= 0 ? _selectedCol : 0;
            _tableData.ColumnWidths.Insert(at, 30.0);
            foreach (var r in _tableData.Rows) r.Cells.Insert(at, new TableCellData());
            RebuildGridItems(syncFirst: false);
        }

        private void InsertColRight()
        {
            PushUndoSnapshot();
            int at = _selectedCol >= 0 ? _selectedCol + 1 : _tableData.ColumnWidths.Count;
            _tableData.ColumnWidths.Insert(at, 30.0);
            foreach (var r in _tableData.Rows) r.Cells.Insert(at, new TableCellData());
            RebuildGridItems(syncFirst: false);
        }

        private void RemoveColumn()
        {
            if (_selectedCol < 0 || _selectedCol >= _tableData.ColumnWidths.Count) return;
            PushUndoSnapshot();
            _tableData.ColumnWidths.RemoveAt(_selectedCol);
            foreach (var r in _tableData.Rows)
                if (_selectedCol < r.Cells.Count) r.Cells.RemoveAt(_selectedCol);
            _selectedCol = -1; _selectedCell = null;
            RebuildGridItems();
        }

        // ── Opmaak ────────────────────────────────────────────────────────────
        private void MergeCells()
        {
            if (_selectedCells.Count == 0) return;
            PushUndoSnapshot();

            if (_selectedCells.Count == 1)
            {
                // Toggle: als al samengevoegd → ontkoppelen, anders één cel uitbreiden
                if (_selectedCell!.MergeRight > 0)
                {
                    var row = _tableData.Rows[_selectedRow];
                    for (int c = _selectedCol + 1; c <= _selectedCol + _selectedCell.MergeRight; c++)
                        if (c < row.Cells.Count) row.Cells[c].IsMergedHidden = false;
                    _selectedCell.MergeRight = 0;
                }
                else
                {
                    int nextCol = _selectedCol + 1;
                    if (nextCol < (_tableData.Rows.ElementAtOrDefault(_selectedRow)?.Cells.Count ?? 0))
                    {
                        _selectedCell.MergeRight = 1;
                        _tableData.Rows[_selectedRow].Cells[nextCol].IsMergedHidden = true;
                    }
                }
            }
            else
            {
                // Meerdere cellen geselecteerd: samenvoegen per rij
                var byRow = _selectedCellPositions.GroupBy(p => p.Row);
                foreach (var rowGroup in byRow)
                {
                    int row = rowGroup.Key;
                    if (row < 0 || row >= _tableData.Rows.Count) continue;
                    var rowData = _tableData.Rows[row];
                    var cols = rowGroup.Select(p => p.Col).OrderBy(c => c).ToList();
                    int minCol = cols.First();
                    int maxCol = cols.Last();

                    // Eerst bestaande samenvoegingen in het bereik ontkoppelen
                    for (int c = minCol; c <= maxCol; c++)
                    {
                        if (c >= rowData.Cells.Count) break;
                        rowData.Cells[c].IsMergedHidden = false;
                        rowData.Cells[c].MergeRight = 0;
                    }

                    // Dan nieuw samenvoegen
                    rowData.Cells[minCol].MergeRight = maxCol - minCol;
                    for (int c = minCol + 1; c <= maxCol; c++)
                        if (c < rowData.Cells.Count) rowData.Cells[c].IsMergedHidden = true;
                }
            }

            GridRefreshRequested?.Invoke();
        }

        private void ToggleHeaderRow()
        {
            if (_selectedRow != 0 || _tableData.Rows.Count == 0) return;
            PushUndoSnapshot();
            bool newValue = !_tableData.Rows[0].IsHeader;
            _tableData.Rows[0].IsHeader = newValue;
            if (newValue)
            {
                // Auto-apply bold + thick bottom border to header row
                foreach (var cell in _tableData.Rows[0].Cells)
                {
                    cell.Bold = true;
                    cell.BorderBottom = 0.5f;
                }
            }
            Notify(nameof(IsCurrentRowHeader));
            RebuildGridItems();
        }

        private void MergeDown()
        {
            if (_selectedCells.Count == 0) return;
            PushUndoSnapshot();

            if (_selectedCells.Count == 1 && _selectedCell != null)
            {
                if (_selectedCell.MergeDown > 0)
                {
                    for (int r = _selectedRow + 1; r <= _selectedRow + _selectedCell.MergeDown; r++)
                        if (r < _tableData.Rows.Count && _selectedCol < _tableData.Rows[r].Cells.Count)
                            _tableData.Rows[r].Cells[_selectedCol].IsMergedHidden = false;
                    _selectedCell.MergeDown = 0;
                }
                else
                {
                    int nextRow = _selectedRow + 1;
                    if (nextRow < _tableData.Rows.Count && _selectedCol < _tableData.Rows[nextRow].Cells.Count)
                    {
                        _selectedCell.MergeDown = 1;
                        _tableData.Rows[nextRow].Cells[_selectedCol].IsMergedHidden = true;
                    }
                }
            }
            else
            {
                foreach (var colGroup in _selectedCellPositions.GroupBy(p => p.Col))
                {
                    int col = colGroup.Key;
                    var rows = colGroup.Select(p => p.Row).OrderBy(r => r).ToList();
                    int minRow = rows.First(), maxRow = rows.Last();

                    for (int r = minRow; r <= maxRow; r++)
                    {
                        if (r >= _tableData.Rows.Count || col >= _tableData.Rows[r].Cells.Count) break;
                        _tableData.Rows[r].Cells[col].IsMergedHidden = false;
                        _tableData.Rows[r].Cells[col].MergeDown = 0;
                    }
                    _tableData.Rows[minRow].Cells[col].MergeDown = maxRow - minRow;
                    for (int r = minRow + 1; r <= maxRow; r++)
                        if (r < _tableData.Rows.Count && col < _tableData.Rows[r].Cells.Count)
                            _tableData.Rows[r].Cells[col].IsMergedHidden = true;
                }
            }
            GridRefreshRequested?.Invoke();
        }

        private void MoveRowUp()
        {
            if (_selectedRow <= 0) return;
            PushUndoSnapshot();
            (_tableData.RowHeights[_selectedRow], _tableData.RowHeights[_selectedRow - 1]) =
                (_tableData.RowHeights[_selectedRow - 1], _tableData.RowHeights[_selectedRow]);
            (_tableData.Rows[_selectedRow], _tableData.Rows[_selectedRow - 1]) =
                (_tableData.Rows[_selectedRow - 1], _tableData.Rows[_selectedRow]);
            _selectedRow--;
            RebuildGridItems(syncFirst: false);
        }

        private void MoveRowDown()
        {
            if (_selectedRow < 0 || _selectedRow >= _tableData.Rows.Count - 1) return;
            PushUndoSnapshot();
            (_tableData.RowHeights[_selectedRow], _tableData.RowHeights[_selectedRow + 1]) =
                (_tableData.RowHeights[_selectedRow + 1], _tableData.RowHeights[_selectedRow]);
            (_tableData.Rows[_selectedRow], _tableData.Rows[_selectedRow + 1]) =
                (_tableData.Rows[_selectedRow + 1], _tableData.Rows[_selectedRow]);
            _selectedRow++;
            RebuildGridItems(syncFirst: false);
        }

        private void MoveColumnLeft()
        {
            if (_selectedCol <= 0) return;
            PushUndoSnapshot();
            (_tableData.ColumnWidths[_selectedCol], _tableData.ColumnWidths[_selectedCol - 1]) =
                (_tableData.ColumnWidths[_selectedCol - 1], _tableData.ColumnWidths[_selectedCol]);
            foreach (var row in _tableData.Rows)
                (row.Cells[_selectedCol], row.Cells[_selectedCol - 1]) =
                    (row.Cells[_selectedCol - 1], row.Cells[_selectedCol]);
            _selectedCol--;
            RebuildGridItems(syncFirst: false);
        }

        private void MoveColumnRight()
        {
            if (_selectedCol < 0 || _selectedCol >= _tableData.ColumnWidths.Count - 1) return;
            PushUndoSnapshot();
            (_tableData.ColumnWidths[_selectedCol], _tableData.ColumnWidths[_selectedCol + 1]) =
                (_tableData.ColumnWidths[_selectedCol + 1], _tableData.ColumnWidths[_selectedCol]);
            foreach (var row in _tableData.Rows)
                (row.Cells[_selectedCol], row.Cells[_selectedCol + 1]) =
                    (row.Cells[_selectedCol + 1], row.Cells[_selectedCol]);
            _selectedCol++;
            RebuildGridItems(syncFirst: false);
        }

        private void ToggleBold()
        {
            if (_selectedCells.Count == 0) return;
            PushUndoSnapshot();
            bool allBold = _selectedCells.All(c => c.Bold);
            foreach (var cell in _selectedCells) cell.Bold = !allBold;
            GridRefreshRequested?.Invoke();
        }

        private void ToggleItalic()
        {
            if (_selectedCells.Count == 0) return;
            PushUndoSnapshot();
            bool allItalic = _selectedCells.All(c => c.Italic);
            foreach (var cell in _selectedCells) cell.Italic = !allItalic;
            GridRefreshRequested?.Invoke();
        }

        private void InsertSubscript()
        {
            if (_selectedCell == null) return;
            PushUndoSnapshot();
            _selectedCell.Text += "_{text}";
            RebuildGridItems();
        }

        private void InsertSuperscript()
        {
            if (_selectedCell == null) return;
            PushUndoSnapshot();
            _selectedCell.Text += "^{text}";
            RebuildGridItems();
        }

        private void SetAlignment(HorizontalAlignment a)
        {
            if (_selectedCells.Count == 0) return;
            PushUndoSnapshot();
            foreach (var cell in _selectedCells) cell.HorizontalAlignment = a;
            GridRefreshRequested?.Invoke();
        }

        // ── Undo / Redo ───────────────────────────────────────────────────────

        // Bevriest de huidige toestand vóór elke bewerking.
        private const int MaxUndoSteps = 100;

        internal void PushUndoSnapshot()
        {
            if (_suppressUndo) return;
            SyncObservableToModel();
            string snapshot = _tableData.Serialize();
            if (_undoStack.Count > 0 && _undoStack.Peek() == snapshot) return;
            _undoStack.Push(snapshot);
            if (_undoStack.Count > MaxUndoSteps)
            {
                // Trim the oldest entry (Stack has no Remove, so rebuild without the bottom)
                var entries = _undoStack.ToArray();
                _undoStack.Clear();
                for (int i = entries.Length - 2; i >= 0; i--)
                    _undoStack.Push(entries[i]);
            }
            _redoStack.Clear();
            Notify(nameof(CanUndo));
            Notify(nameof(CanRedo));
        }

        private void Undo()
        {
            if (_undoStack.Count == 0) return;
            _suppressUndo = true;
            _redoStack.Push(_tableData.Serialize());
            _tableData = TableData.Deserialize(_undoStack.Pop())!;
            RebuildGridItems(syncFirst: false);
            _suppressUndo = false;
            Notify(nameof(CanUndo));
            Notify(nameof(CanRedo));
            StatusText = "Undone";
        }

        private void Redo()
        {
            if (_redoStack.Count == 0) return;
            _suppressUndo = true;
            _undoStack.Push(_tableData.Serialize());
            _tableData = TableData.Deserialize(_redoStack.Pop())!;
            RebuildGridItems(syncFirst: false);
            _suppressUndo = false;
            Notify(nameof(CanUndo));
            Notify(nameof(CanRedo));
            StatusText = "Redone";
        }

        // ── Copy / Paste ──────────────────────────────────────────────────────

        private void CopySelection()
        {
            if (_selectedCellPositions.Count == 0) return;
            SyncObservableToModel();

            int minRow = _selectedCellPositions.Min(p => p.Row);
            int minCol = _selectedCellPositions.Min(p => p.Col);

            _clipboard = _selectedCellPositions
                .Select(p => new ClipCell(p.Row - minRow, p.Col - minCol,
                    CloneCell(GetCell(p.Row, p.Col) ?? new TableCellData())))
                .ToList();

            // Zet ook tabulatortekst op het klembord zodat plakken in Excel werkt
            int maxRow = _selectedCellPositions.Max(p => p.Row);
            int maxCol = _selectedCellPositions.Max(p => p.Col);
            var lines  = new System.Text.StringBuilder();
            for (int rr = minRow; rr <= maxRow; rr++)
            {
                bool firstCol = true;
                for (int cc = minCol; cc <= maxCol; cc++)
                {
                    if (!firstCol) lines.Append('\t');
                    firstCol = false;
                    lines.Append(GetCell(rr, cc)?.Text ?? string.Empty);
                }
                lines.Append('\n');
            }
            try { System.Windows.Clipboard.SetText(lines.ToString()); } catch { }

            StatusText = $"{_clipboard.Count} cell(s) copied";
        }

        private void PasteClipboard()
        {
            if (_selectedRow < 0 || _selectedCol < 0) return;
            PushUndoSnapshot();

            if (_clipboard != null)
            {
                foreach (var item in _clipboard)
                {
                    var target = GetCell(_selectedRow + item.RelRow, _selectedCol + item.RelCol);
                    if (target != null) CopyProperties(item.Data, target);
                }
                GridRefreshRequested?.Invoke();
            }
            else
            {
                // Probeer van systeem-klembord (tab-gescheiden tekst)
                try
                {
                    string text = System.Windows.Clipboard.GetText();
                    if (string.IsNullOrEmpty(text)) return;
                    var rows = text.Replace("\r\n", "\n").Replace("\r", "\n")
                                   .TrimEnd('\n').Split('\n');
                    for (int dr = 0; dr < rows.Length; dr++)
                    {
                        var cols = rows[dr].Split('\t');
                        for (int dc = 0; dc < cols.Length; dc++)
                        {
                            var cell = GetCell(_selectedRow + dr, _selectedCol + dc);
                            if (cell != null) cell.Text = cols[dc];
                        }
                    }
                    GridRefreshRequested?.Invoke();
                }
                catch { }
            }
        }

        private static TableCellData CloneCell(TableCellData src) => new()
        {
            Text                = src.Text,
            Bold                = src.Bold,
            Italic              = src.Italic,
            HorizontalAlignment = src.HorizontalAlignment,
            VerticalAlignment   = src.VerticalAlignment,
            TextColor           = src.TextColor,
            BackgroundColor     = src.BackgroundColor,
            HatchColor          = src.HatchColor,
            HatchPatternName    = src.HatchPatternName,
            HatchScale          = src.HatchScale,
            FillPattern         = src.FillPattern,
            FontName            = src.FontName,
            FontSize            = src.FontSize,
            WordWrap            = src.WordWrap,
            BorderTop           = src.BorderTop,
            BorderBottom        = src.BorderBottom,
            BorderLeft          = src.BorderLeft,
            BorderRight         = src.BorderRight,
            BorderColor         = src.BorderColor,
        };

        private static void CopyProperties(TableCellData src, TableCellData dst)
        {
            dst.Text = src.Text; dst.Bold = src.Bold; dst.Italic = src.Italic;
            dst.HorizontalAlignment = src.HorizontalAlignment;
            dst.VerticalAlignment   = src.VerticalAlignment;
            dst.TextColor           = src.TextColor;
            dst.BackgroundColor     = src.BackgroundColor;
            dst.HatchColor          = src.HatchColor;
            dst.HatchPatternName    = src.HatchPatternName;
            dst.HatchScale          = src.HatchScale;
            dst.FillPattern         = src.FillPattern;
            dst.FontName            = src.FontName;
            dst.FontSize            = src.FontSize;
            dst.WordWrap            = src.WordWrap;
            dst.BorderTop           = src.BorderTop;
            dst.BorderBottom        = src.BorderBottom;
            dst.BorderLeft          = src.BorderLeft;
            dst.BorderRight         = src.BorderRight;
            dst.BorderColor         = src.BorderColor;
        }

        // ── Verticale uitlijning ──────────────────────────────────────────────

        private void SetVAlignment(VerticalAlignment a)
        {
            if (_selectedCells.Count == 0) return;
            PushUndoSnapshot();
            foreach (var cell in _selectedCells) cell.VerticalAlignment = a;
            GridRefreshRequested?.Invoke();
        }

        // ── Woordterugloop ────────────────────────────────────────────────────

        private void ToggleWordWrap()
        {
            if (_selectedCells.Count == 0) return;
            PushUndoSnapshot();
            bool allWrap = _selectedCells.All(c => c.WordWrap);
            foreach (var cell in _selectedCells) cell.WordWrap = !allWrap;
            GridRefreshRequested?.Invoke();
        }

        // ── Randen ────────────────────────────────────────────────────────────

        private void ApplyBorders(bool top, bool bottom, bool left, bool right, bool clear = false)
        {
            if (_selectedCells.Count == 0) return;
            PushUndoSnapshot();
            if (clear)
            {
                foreach (var cell in _selectedCells)
                {
                    cell.BorderTop = cell.BorderBottom = cell.BorderLeft = cell.BorderRight = 0f;
                    cell.BorderColor = null;
                }
            }
            else
            {
                foreach (var cell in _selectedCells)
                {
                    if (top)    cell.BorderTop    = _borderThickness;
                    if (bottom) cell.BorderBottom = _borderThickness;
                    if (left)   cell.BorderLeft   = _borderThickness;
                    if (right)  cell.BorderRight  = _borderThickness;
                    cell.BorderColor = _borderColor;
                }
            }
            GridRefreshRequested?.Invoke();
        }

        private void ApplyBorderOutside()
        {
            if (_selectedCellPositions.Count == 0) return;
            PushUndoSnapshot();
            int minR = _selectedCellPositions.Min(p => p.Row);
            int maxR = _selectedCellPositions.Max(p => p.Row);
            int minC = _selectedCellPositions.Min(p => p.Col);
            int maxC = _selectedCellPositions.Max(p => p.Col);

            foreach (var (r, c) in _selectedCellPositions)
            {
                var cell = GetCell(r, c);
                if (cell == null) continue;
                cell.BorderColor = _borderColor;
                cell.BorderTop    = r == minR ? _borderThickness : 0f;
                cell.BorderBottom = r == maxR ? _borderThickness : 0f;
                cell.BorderLeft   = c == minC ? _borderThickness : 0f;
                cell.BorderRight  = c == maxC ? _borderThickness : 0f;
            }
            GridRefreshRequested?.Invoke();
        }

        private void ToggleBorderSide(bool top = false, bool bottom = false,
                                      bool left = false, bool right = false)
        {
            if (_selectedCells.Count == 0) return;
            PushUndoSnapshot();
            bool allSet = _selectedCells.All(cell =>
                (!top    || cell.BorderTop    > 0) &&
                (!bottom || cell.BorderBottom > 0) &&
                (!left   || cell.BorderLeft   > 0) &&
                (!right  || cell.BorderRight  > 0));
            float newVal = allSet ? 0f : _borderThickness;

            foreach (var cell in _selectedCells)
            {
                if (top)    { cell.BorderTop    = newVal; cell.BorderColor = _borderColor; }
                if (bottom) { cell.BorderBottom = newVal; cell.BorderColor = _borderColor; }
                if (left)   { cell.BorderLeft   = newVal; cell.BorderColor = _borderColor; }
                if (right)  { cell.BorderRight  = newVal; cell.BorderColor = _borderColor; }
            }
            GridRefreshRequested?.Invoke();
        }

        // ── Rhino arceerpatronen ──────────────────────────────────────────────

        public void RefreshHatchPatterns()
        {
            // Zorg dat de 4 basis RT-patronen altijd beschikbaar zijn in het document
            RhinoTableDrawer.EnsureBuiltinHatchPatterns(_doc);

            var list = new List<string> { "(no hatch pattern)" };
            for (int i = 0; i < _doc.HatchPatterns.Count; i++)
            {
                var p = _doc.HatchPatterns[i];
                if (p != null && !p.IsDeleted && !string.IsNullOrEmpty(p.Name))
                    list.Add(p.Name);
            }
            _availableHatchPatterns = list;
            Notify(nameof(AvailableHatchPatterns));
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private static string ColLetter(int index)
        {
            if (index < 0) return "?";
            string r = string.Empty; index++;
            while (index > 0) { index--; r = (char)('A' + index % 26) + r; index /= 26; }
            return r;
        }

        public void AddRecentColor(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return;
            var updated = RecentColorsManager.Add(hex, RecentColors.ToList());
            RecentColors.Clear();
            foreach (var c in updated) RecentColors.Add(c);
            RecentColorsManager.Save(RecentColors);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Notify([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
