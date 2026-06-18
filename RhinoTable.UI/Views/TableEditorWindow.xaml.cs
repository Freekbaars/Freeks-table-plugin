using Rhino;
using RhinoTable.Core.Models;
using RhinoTable.UI.Converters;
using RhinoTable.UI.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using WinHA = System.Windows.HorizontalAlignment;
using WinVA = System.Windows.VerticalAlignment;
using HA = RhinoTable.Core.Models.HorizontalAlignment;
using VA = RhinoTable.Core.Models.VerticalAlignment;

namespace RhinoTable.UI.Views
{
    public partial class TableEditorWindow : Window
    {
        private readonly TableEditorViewModel _vm;
        private readonly RhinoDoc _doc;
        private const double PxPerMm = 3.7795275591;

        // Converters — één instantie per venster
        private static readonly BoolToFontWeightConverter        _boldConv        = new();
        private static readonly BoolToFontStyleConverter         _italicConv      = new();
        private static readonly HAlignToTextAlignConverter       _alignConv       = new();
        private static readonly VAlignToVerticalAlignConverter   _vAlignConv      = new();
        private static readonly BoolToTextWrappingConverter      _wrapConv        = new();
        private static readonly MergedHiddenToBrushConverter     _mergedBg        = new();
        private static readonly BoolToInverseVisibilityConverter _hideWhenMerged  = new();
        private static readonly IntPositiveToVisibilityConverter _showWhenMerged  = new();
    
        private List<double> _rowHeights = new();
        // Blokkeert BeginEdit() tijdens programmatische commit/rebuild om re-entrancy te voorkomen.
        private bool _suppressBeginEdit = false;
        private bool _suppressSelectionChanged = false;

        // Bijzondere tekens — bijhouden welke edit-TextBox als laatste focus had
        private TextBox? _lastCellTextBox;
        private int      _lastCaretPos;
        private int      _lastCellRow = -1;
        private int      _lastCellCol = -1;
        private bool     _specialCharPanelBuilt;
        private bool     _fieldsPanelBuilt;

        public TableEditorWindow(RhinoDoc doc, TableData tableData)
        {
            InitializeComponent();

            _doc = doc;
            _vm  = new TableEditorViewModel(doc, tableData);
            DataContext = _vm;

            _vm.ColumnsChanged       += RebuildColumns;
            _vm.RequestClose         += () => Close();
            _vm.GridRefreshRequested += RefreshGridAfterFormat;
            _vm.GridSyncRequested    += SyncSizesFromDataGrid;
            _vm.RowHeightChanged     += () => Dispatcher.BeginInvoke(
                ApplyRowHeights, System.Windows.Threading.DispatcherPriority.Loaded);
            WireImportProgress();

            // Rijnummers en rijhoogtes uit het model toepassen
            TableGrid.LoadingRow += (s, e) =>
            {
                int idx = e.Row.GetIndex();

                // Hoogte uit model zetten bij elke (her)laad van een rij
                if (idx >= 0 && idx < _rowHeights.Count)
                    e.Row.Height = _rowHeights[idx] * PxPerMm;

                if (e.Row.DataContext is ObservableRow obsRow && obsRow.IsHeader)
                {
                    e.Row.Header = "H";
                    return;
                }
                // Tel alleen niet-header rijen
                int nonHeaderIndex = _vm.GridItems
                    .Take(idx)
                    .Count(r => !r.IsHeader) + 1;
                e.Row.Header = nonHeaderIndex.ToString();
            };

            // Single-click bewerken: zodra een cel focus krijgt, start editing.
            // Bewust geblokkeerd tijdens RefreshGridAfterFormat om re-entrancy te voorkomen:
            // CommitEdit(exitEditingMode:true) geeft focus terug aan de DataGridCell, wat
            // GotFocus triggert → BeginEdit() → DataGrid is onmiddellijk weer in edit-mode →
            // Columns.Clear() knalt of doet niets.
            TableGrid.GotFocus += (s, e) =>
            {
                if (!_suppressBeginEdit
                    && e.OriginalSource is DataGridCell cell
                    && !cell.IsEditing
                    && !cell.IsReadOnly)
                    TableGrid.BeginEdit();
            };

            RebuildColumns();
        }

        // ── Import voortgangsvenster ──────────────────────────────────────────

        private ImportProgressWindow? _importProgress;

        private void WireImportProgress()
        {
            _vm.ImportStarted += (fileName, maxRows) =>
            {
                _importProgress = new ImportProgressWindow { Owner = this };
                _importProgress.SetStatus($"Loading file: {fileName}");
                _importProgress.SetDeterminate(maxRows);
                _importProgress.Show();
            };

            _vm.ImportProgress += rows =>
            {
                _importProgress?.SetRows(rows);
                _importProgress?.SetProgress(rows);
            };

            _vm.ImportFinished += () =>
            {
                _importProgress?.Close();
                _importProgress = null;
            };
        }

        // ── Opmaak vernieuwen ─────────────────────────────────────────────────

        private void RefreshGridAfterFormat()
        {
            // Gesleepte rij/kolomgroottes opslaan vóór de rebuild, anders gaan ze verloren.
            SyncSizesFromDataGrid();

            // Sla de volledige selectie op vóór de rebuild; daarna herstellen we die.
            var savedSelection = _vm.SelectedCellPositions.ToList();

            _suppressBeginEdit = true;
            try
            {
                try { TableGrid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true); } catch { }
                try { TableGrid.CommitEdit(DataGridEditingUnit.Row,  exitEditingMode: true); } catch { }
                _vm.RebuildGridItems();
            }
            finally
            {
                _suppressBeginEdit = false;
            }

            // Herstel de volledige celselectie na rebuild.
            _suppressSelectionChanged = true;
            try
            {
                TableGrid.SelectedCells.Clear();
                foreach (var (row, col) in savedSelection)
                {
                    if (row >= 0 && col >= 0
                        && row < TableGrid.Items.Count
                        && col < TableGrid.Columns.Count)
                        TableGrid.SelectedCells.Add(
                            new DataGridCellInfo(TableGrid.Items[row], TableGrid.Columns[col]));
                }
                if (savedSelection.Count > 0)
                {
                    var (r0, c0) = savedSelection[0];
                    if (r0 >= 0 && c0 >= 0
                        && r0 < TableGrid.Items.Count
                        && c0 < TableGrid.Columns.Count)
                        TableGrid.CurrentCell =
                            new DataGridCellInfo(TableGrid.Items[r0], TableGrid.Columns[c0]);
                }
            }
            finally
            {
                _suppressSelectionChanged = false;
            }

            // Zeker stellen dat de model-hoogtes ook zichtbaar zijn na de rebuild.
            Dispatcher.BeginInvoke(ApplyRowHeights, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // ── Grootte-sync (gesleepte breedte/hoogte → model) ──────────────────

        private void SyncSizesFromDataGrid()
        {
            var colWidths = TableGrid.Columns
                .OrderBy(c => c.DisplayIndex)
                .Select(c => c.ActualWidth / PxPerMm)
                .ToList();
            _vm.SyncColumnWidths(colWidths);

            var rowHeightsMm = new List<double>();
            for (int i = 0; i < TableGrid.Items.Count; i++)
            {
                double h = (TableGrid.ItemContainerGenerator.ContainerFromIndex(i) as DataGridRow)
                           ?.ActualHeight / PxPerMm ?? 0;
                rowHeightsMm.Add(h);
            }
            _vm.SyncRowHeights(rowHeightsMm);
        }

        // ── Kolom drag-and-drop herordening ───────────────────────────────────

        private bool _suppressColumnReorder;
        private readonly Dictionary<DataGridColumn, int> _columnOriginalIndex = new();

        private void TableGrid_ColumnReordered(object sender, DataGridColumnEventArgs e)
        {
            if (_suppressColumnReorder) return;
            _suppressColumnReorder = true;
            try
            {
                var newOrder = TableGrid.Columns
                    .OrderBy(c => c.DisplayIndex)
                    .Select(c => _columnOriginalIndex.TryGetValue(c, out int idx) ? idx : c.DisplayIndex)
                    .ToList();
                _vm.ReorderColumns(newOrder);
            }
            finally
            {
                _suppressColumnReorder = false;
            }
        }

        // ── Cel-navigatie helper ──────────────────────────────────────────────

        private void NavigateToCell(int row, int col)
        {
            if (row < 0 || row >= TableGrid.Items.Count) return;
            if (col < 0 || col >= TableGrid.Columns.Count) return;
            var item = TableGrid.Items[row];
            var column = TableGrid.Columns[col];
            TableGrid.CurrentCell = new DataGridCellInfo(item, column);
            _suppressSelectionChanged = true;
            TableGrid.SelectedCells.Clear();
            TableGrid.SelectedCells.Add(new DataGridCellInfo(item, column));
            _suppressSelectionChanged = false;
            TableGrid.ScrollIntoView(item, column);
            _vm.SetSelectedCells(new List<(int, int)> { (row, col) });
        }

        // ── Kolommen aanmaken ─────────────────────────────────────────────────

        private void RebuildColumns()
        {
            TableGrid.Columns.Clear();
            _columnOriginalIndex.Clear();

            var table = _vm.TableData;
            for (int c = 0; c < table.ColumnWidths.Count; c++)
                TableGrid.Columns.Add(MakeColumn(c, table.ColumnWidths[c] * PxPerMm));

            _rowHeights = table.RowHeights;
            Dispatcher.BeginInvoke(ApplyRowHeights, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void ApplyRowHeights()
        {
            for (int i = 0; i < TableGrid.Items.Count; i++)
            {
                if (TableGrid.ItemContainerGenerator.ContainerFromIndex(i) is DataGridRow row)
                {
                    double h = i < _rowHeights.Count ? _rowHeights[i] : 8.0;
                    row.Height = h * PxPerMm;
                }
            }
        }

        private DataGridTemplateColumn MakeColumn(int ci, double widthPx)
        {
            // ── CellTemplate ──────────────────────────────────────────────────
            var cellTemplate = new DataTemplate();
            var root = new FrameworkElementFactory(typeof(Grid));
            root.SetValue(UIElement.ClipToBoundsProperty, true);

            // 1. Arceerpatroon / vulkleur — Loaded event i.p.v. MultiBinding (MultiBinding
            // werkt niet betrouwbaar met FrameworkElementFactory).
            var fillBg = new FrameworkElementFactory(typeof(Border));
            fillBg.AddHandler(FrameworkElement.LoadedEvent, new RoutedEventHandler((s, _) =>
            {
                if (s is Border b) ApplyFillPattern(b, ci);
            }));
            root.AppendChild(fillBg);

            // 2. Grijze overlay voor samengevoegen-verborgen cellen
            var mergedOverlay = new FrameworkElementFactory(typeof(Border));
            mergedOverlay.SetBinding(Border.BackgroundProperty, Bind($"Cells[{ci}].IsMergedHidden", _mergedBg));
            root.AppendChild(mergedOverlay);

            // 3. Tekst
            var tb = new FrameworkElementFactory(typeof(TextBlock));
            tb.SetBinding(TextBlock.TextProperty,          Bind($"Cells[{ci}].Text"));
            tb.SetBinding(TextBlock.FontWeightProperty,    Bind($"Cells[{ci}].Bold",                _boldConv));
            tb.SetBinding(TextBlock.FontStyleProperty,     Bind($"Cells[{ci}].Italic",              _italicConv));
            tb.SetBinding(TextBlock.TextAlignmentProperty, Bind($"Cells[{ci}].HorizontalAlignment", _alignConv));
            tb.SetBinding(TextBlock.VerticalAlignmentProperty, Bind($"Cells[{ci}].VerticalAlignment", _vAlignConv));
            tb.SetBinding(TextBlock.TextWrappingProperty,  Bind($"Cells[{ci}].WordWrap",            _wrapConv));
            tb.SetBinding(TextBlock.VisibilityProperty,    Bind($"Cells[{ci}].IsMergedHidden",      _hideWhenMerged));
            tb.SetValue(TextBlock.PaddingProperty,            new Thickness(5, 2, 5, 2));
            // Tekstkleur: via Loaded zodat de waarde als LocalValue (prioriteit 3) wordt
            // gezet — dit wint zeker van geërfde waarden uit de DataGridCell-stijl.
            tb.AddHandler(FrameworkElement.LoadedEvent, new RoutedEventHandler((s, _) =>
            {
                if (s is TextBlock t) ApplyTextColor(t, ci);
            }));
            root.AppendChild(tb);

            // 4. ⊞ badge — cel voegt rechts samen
            var badge = new FrameworkElementFactory(typeof(TextBlock));
            badge.SetValue(TextBlock.TextProperty,                "⊞");
            badge.SetValue(TextBlock.FontSizeProperty,            9.0);
            badge.SetValue(TextBlock.ForegroundProperty,          new SolidColorBrush(Color.FromRgb(0x26, 0x82, 0xC6)));
            badge.SetValue(TextBlock.HorizontalAlignmentProperty, WinHA.Right);
            badge.SetValue(TextBlock.VerticalAlignmentProperty,   WinVA.Top);
            badge.SetValue(TextBlock.MarginProperty,              new Thickness(0, 2, 3, 0));
            badge.SetValue(TextBlock.ToolTipProperty,             "Samengevoegde cel");
            badge.SetBinding(TextBlock.VisibilityProperty,        Bind($"Cells[{ci}].MergeRight", _showWhenMerged));
            root.AppendChild(badge);

            // 5. "←" pijl in samengevoegen-verborgen cel
            var arrow = new FrameworkElementFactory(typeof(TextBlock));
            arrow.SetValue(TextBlock.TextProperty,                "←");
            arrow.SetValue(TextBlock.FontSizeProperty,            10.0);
            arrow.SetValue(TextBlock.ForegroundProperty,          new SolidColorBrush(Color.FromRgb(0x99, 0xA8, 0xBB)));
            arrow.SetValue(TextBlock.HorizontalAlignmentProperty, WinHA.Center);
            arrow.SetValue(TextBlock.VerticalAlignmentProperty,   WinVA.Center);
            arrow.SetValue(TextBlock.ToolTipProperty,             "Opgeslokt door samengevoegde cel");
            arrow.SetBinding(TextBlock.VisibilityProperty,        Bind($"Cells[{ci}].IsMergedHidden",
                                                                       new BooleanToVisibilityConverter()));
            root.AppendChild(arrow);

            // 6. Celrand overlay — zelfde patroon als ApplyTextColor: imperatief via Loaded
            // want FrameworkElementFactory ondersteunt MultiBinding niet betrouwbaar.
            var cellBorder = new FrameworkElementFactory(typeof(Border));
            cellBorder.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            cellBorder.SetValue(UIElement.IsHitTestVisibleProperty, false);
            cellBorder.AddHandler(FrameworkElement.LoadedEvent, new RoutedEventHandler((s, _) =>
            {
                if (s is Border b) ApplyCellBorder(b, ci);
            }));
            root.AppendChild(cellBorder);

            cellTemplate.VisualTree = root;

            // ── CellEditingTemplate: TextBox ──────────────────────────────────
            var editTemplate = new DataTemplate();
            var tbx = new FrameworkElementFactory(typeof(TextBox));
            tbx.SetBinding(TextBox.TextProperty, new Binding($"Cells[{ci}].Text")
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.LostFocus
            });
            tbx.SetBinding(TextBox.FontWeightProperty, Bind($"Cells[{ci}].Bold",   _boldConv));
            tbx.SetBinding(TextBox.FontStyleProperty,  Bind($"Cells[{ci}].Italic", _italicConv));
            tbx.SetValue(TextBox.BorderThicknessProperty,         new Thickness(0));
            tbx.SetValue(TextBox.PaddingProperty,                 new Thickness(4, 0, 4, 0));
            tbx.SetValue(TextBox.VerticalContentAlignmentProperty, WinVA.Center);
            tbx.SetValue(TextBox.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0xEB, 0xF4, 0xFF)));
            tbx.AddHandler(FrameworkElement.LoadedEvent, new RoutedEventHandler((s, _) =>
            {
                if (s is TextBox box)
                {
                    // Handlers VÓÓR Focus() registreren — anders mist de eerste GotFocus
                    box.GotFocus += (sb, _2) =>
                    {
                        _lastCellTextBox = (TextBox)sb;
                        _lastCellRow     = _vm.CurrentRow;
                        _lastCellCol     = _vm.CurrentCol;
                    };
                    box.PreviewLostKeyboardFocus += (sb, _2) =>
                    {
                        if (sb is TextBox t) _lastCaretPos = t.CaretIndex;
                    };
                    box.Focus(); box.SelectAll(); ApplyTextColor(box, ci);
                }
            }));
            editTemplate.VisualTree = tbx;

            var col = new DataGridTemplateColumn
            {
                Header              = ColLetter(ci),
                CellTemplate        = cellTemplate,
                CellEditingTemplate = editTemplate,
                Width               = new DataGridLength(widthPx),
            };
            _columnOriginalIndex[col] = ci;
            return col;
        }

        private static void ApplyFillPattern(Border fillBg, int ci)
        {
            if (fillBg.DataContext is not ObservableRow row || ci >= row.Cells.Count) return;
            var cell = row.Cells[ci];
            fillBg.Background = FillPatternBrushConverter.BuildBrush(
                cell.FillPattern, cell.BackgroundColor, cell.HatchColor, cell.HatchPatternName);
        }

        private static void ApplyCellBorder(Border border, int ci)
        {
            if (border.DataContext is not ObservableRow row || ci >= row.Cells.Count) return;
            var cell = row.Cells[ci];
            border.BorderThickness = new Thickness(
                cell.BorderLeft   * PxPerMm,
                cell.BorderTop    * PxPerMm,
                cell.BorderRight  * PxPerMm,
                cell.BorderBottom * PxPerMm);
            var color = ColorStringToBrushConverter.ParseHex(cell.BorderColor);
            border.BorderBrush = color.HasValue ? new SolidColorBrush(color.Value) : Brushes.Black;
        }

        // Zet tekstkleur als echte LocalValue (prioriteit 3) op het element,
        // zodat het zeker wint van geërfde Foreground-waarden uit de DataGridCell-stijl.
        private static void ApplyTextColor(FrameworkElement element, int ci)
        {
            if (element.DataContext is not ObservableRow row || ci >= row.Cells.Count) return;
            string? color = row.Cells[ci].TextColor;
            var parsed = string.IsNullOrEmpty(color) ? null : ColorStringToBrushConverter.ParseHex(color);
            if (parsed.HasValue)
                element.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(parsed.Value));
            else
                element.ClearValue(TextBlock.ForegroundProperty);
        }

        private static Binding Bind(string path, IValueConverter? conv = null)
        {
            var b = new Binding(path);
            if (conv != null) b.Converter = conv;
            return b;
        }

        // ── DataGrid events ───────────────────────────────────────────────────

        private void TableGrid_SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
        {
            if (_suppressSelectionChanged) return;

            var cells = TableGrid.SelectedCells
                .Select(info => (Row: TableGrid.Items.IndexOf(info.Item), Col: info.Column.DisplayIndex))
                .Where(rc => rc.Row >= 0 && rc.Col >= 0)
                .ToList();

            // Wanneer de selectie leeg lijkt maar het DataGrid geen toetsenbordfocus heeft,
            // is dit een bijwerking van een toolbar-klik (WPF DataGrid wist tijdelijk SelectedCells
            // bij focus-verlies). De bestaande celselectie in de ViewModel bewaren.
            if (cells.Count == 0 && !TableGrid.IsKeyboardFocusWithin)
                return;

            _vm.SetSelectedCells(cells);
        }

        private void TableGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            // Push snapshot BEFORE the LostFocus binding commits the new text to the model,
            // so each committed text edit becomes a distinct undo step.
            if (e.EditAction == DataGridEditAction.Commit)
                _vm.PushUndoSnapshot();
        }

        private void TableGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            bool ctrl  = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

            int totalRows = _vm.TableData.Rows.Count;
            int totalCols = _vm.TableData.ColumnWidths.Count;
            int curRow    = _vm.CurrentRow;
            int curCol    = _vm.CurrentCol;

            // ── Delete ────────────────────────────────────────────────────────
            if (e.Key == Key.Delete && Keyboard.FocusedElement is not TextBox
                && TableGrid.CurrentCell.IsValid)
            {
                _vm.ClearSelectedCells(curRow, curCol);
                e.Handled = true;
                return;
            }

            // ── Ctrl shortcuts ────────────────────────────────────────────────
            if (ctrl)
            {
                switch (e.Key)
                {
                    case Key.Z:
                        try { TableGrid.CancelEdit(DataGridEditingUnit.Cell); } catch { }
                        try { TableGrid.CancelEdit(DataGridEditingUnit.Row);  } catch { }
                        _vm.UndoCommand.Execute(null);
                        e.Handled = true; return;
                    case Key.Y:
                        try { TableGrid.CancelEdit(DataGridEditingUnit.Cell); } catch { }
                        try { TableGrid.CancelEdit(DataGridEditingUnit.Row);  } catch { }
                        _vm.RedoCommand.Execute(null);
                        e.Handled = true; return;
                    case Key.C: _vm.CopyCommand.Execute(null);  e.Handled = true; return;
                    case Key.V: _vm.PasteCommand.Execute(null); e.Handled = true; return;
                }
                return;
            }

            // ── Tab — commit en ga naar volgende/vorige cel ───────────────────
            if (e.Key == Key.Tab && curRow >= 0 && curCol >= 0)
            {
                if (Keyboard.FocusedElement is TextBox tabBox)
                    tabBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                try { TableGrid.CommitEdit(DataGridEditingUnit.Cell, true); } catch { }
                try { TableGrid.CommitEdit(DataGridEditingUnit.Row,  true); } catch { }

                int r = curRow, c = curCol;
                if (shift) { c--; if (c < 0) { c = totalCols - 1; r--; } }
                else       { c++; if (c >= totalCols) { c = 0; r++; } }

                if (r >= 0 && r < totalRows)
                    NavigateToCell(r, Math.Max(0, Math.Min(c, totalCols - 1)));
                e.Handled = true;
                return;
            }

            // ── Pijltoetsen — commit huidige cel en navigeer ─────────────────
            // Opmerking: !isEditing check is hier bewust weggelaten — cellen zijn altijd
            // in edit-mode door GotFocus→BeginEdit(), dus die check blokkeerde altijd navigatie.
            if (!ctrl && curRow >= 0 && curCol >= 0)
            {
                int r = curRow, c = curCol;
                switch (e.Key)
                {
                    case Key.Up:    r--; break;
                    case Key.Down:  r++; break;
                    case Key.Left:  c--; break;
                    case Key.Right: c++; break;
                    default: return;
                }
                if (Keyboard.FocusedElement is TextBox arrowBox)
                    arrowBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                try { TableGrid.CommitEdit(DataGridEditingUnit.Cell, true); } catch { }
                try { TableGrid.CommitEdit(DataGridEditingUnit.Row,  true); } catch { }
                r = Math.Max(0, Math.Min(r, totalRows - 1));
                c = Math.Max(0, Math.Min(c, totalCols - 1));
                NavigateToCell(r, c);
                e.Handled = true;
            }
        }

        // ── Hex kleur invoer ──────────────────────────────────────────────────

        private void ApplyHexTextColor_Click(object sender, RoutedEventArgs e)
        {
            string hex = HexFromSibling(sender);
            if (IsValidHex(hex)) _vm.SetTextColorCommand.Execute(hex);
            TextColorToggle.IsChecked = false;
        }

        private void ApplyHexFillColor_Click(object sender, RoutedEventArgs e)
        {
            string hex = HexFromSibling(sender);
            if (IsValidHex(hex)) _vm.SetFillColorCommand.Execute(hex);
            FillColorToggle.IsChecked = false;
        }

        private void ApplyHexBorderColor_Click(object sender, RoutedEventArgs e)
        {
            string hex = HexFromSibling(sender);
            if (IsValidHex(hex)) _vm.SetBorderColorCommand.Execute(hex);
            BorderColorToggle.IsChecked = false;
        }

        private void ApplyHexHatchColor_Click(object sender, RoutedEventArgs e)
        {
            string hex = HexFromSibling(sender);
            if (IsValidHex(hex)) _vm.SetHatchColorCommand.Execute(hex);
            HatchColorToggle.IsChecked = false;
        }

        private void HatchPatternListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ListBox lb && lb.SelectedItem is string name)
            {
                _vm.SetHatchPatternNameCommand.Execute(name);
                HatchPatternToggle.IsChecked = false;
                lb.SelectedIndex = -1;
            }
        }

        private void HatchPatternPopup_Opened(object sender, EventArgs e)
            => _vm.RefreshHatchPatterns();

        private void HatchScaleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox cb && cb.SelectedItem is ComboBoxItem item)
                _vm.SelectedHatchScale = item.Content?.ToString() ?? "";
        }

        private void HatchRotationCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox cb && cb.SelectedItem is ComboBoxItem item)
                _vm.SelectedHatchRotation = item.Content?.ToString() ?? "";
        }

        private void RefreshHatchPatterns_Click(object sender, RoutedEventArgs e)
            => _vm.RefreshHatchPatterns();

        // Leest de TextBox die naast de knop staat (zelfde StackPanel).
        private static string HexFromSibling(object sender)
        {
            if (sender is not Button btn || btn.Parent is not StackPanel sp) return "";
            string raw = sp.Children.OfType<TextBox>().FirstOrDefault()?.Text ?? "";
            raw = raw.Trim();
            if (!raw.StartsWith("#")) raw = "#" + raw;
            return raw.ToUpperInvariant();
        }

        private static bool IsValidHex(string hex)
            => hex.Length == 7 && hex[0] == '#'
               && hex.Skip(1).All(c => (c >= '0' && c <= '9')
                                    || (c >= 'A' && c <= 'F')
                                    || (c >= 'a' && c <= 'f'));

        // Sluit de kleur-popup wanneer een kleurvlak wordt aangeklikt.
        // De ToggleButton staat opgeslagen in de Tag van de kleurknop.
        private void ColorPopupButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement el && el.Tag is ToggleButton toggle)
                toggle.IsChecked = false;
        }

        // Sluit de popup voor recente kleuren — de Tag bevat de naam van de ToggleButton.
        private void RecentColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement el && el.Tag is string name
                && FindName(name) is ToggleButton toggle)
                toggle.IsChecked = false;
        }

        private void OpenHelp_Click(object sender, RoutedEventArgs e)
            => new HelpWindow { Owner = this }.ShowDialog();

        // ── Sjablonen ────────────────────────────────────────────────────────

        // Scant alle blokken in het document en bouwt een dynamische stuklijst.
        private TableData? BuildBomTable()
        {
            // Verzamel alle niet-verwijderde blokdefinities met minstens 1 instantie
            var allInstances = _doc.Objects
                .OfType<Rhino.DocObjects.InstanceObject>()
                .Where(o => !o.IsDeleted)
                .ToList();

            if (allInstances.Count == 0)
            {
                MessageBox.Show("Er zijn geen blokinstanties gevonden in het document.",
                                "BOM lijst", MessageBoxButton.OK, MessageBoxImage.Information);
                return null;
            }

            // Groepeer per blokdefinitie, gesorteerd op naam
            var groups = allInstances
                .GroupBy(o => o.InstanceDefinition.Id)
                .Select(g => (Def: g.First().InstanceDefinition, Instances: g.ToList()))
                .OrderBy(g => g.Def.Name)
                .ToList();

            // Bepaal extra kolommen: omschrijving en user-text sleutels
            bool hasDesc = groups.Any(g => !string.IsNullOrEmpty(g.Def.Description));

            var allKeys = groups
                .SelectMany(g => g.Instances
                    .SelectMany(o => o.Attributes.GetUserStrings()?.AllKeys
                                     ?? Array.Empty<string>()))
                .Where(k => !string.IsNullOrEmpty(k))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(k => k)
                .ToList();

            if (MessageBox.Show(
                    $"Gevonden: {groups.Count} bloktype(s){(allKeys.Count > 0 ? $" met {allKeys.Count} user-text sleutel(s)" : "")}.\n" +
                    "Huidige tabelinhoud vervangen door een BOM lijst?",
                    "BOM lijst", MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes) return null;

            // Kolombreedtes: nr, naam, aantal, omschrijving?, user-text...
            var table = new TableData { DefaultFontSize = 3.5, DefaultFontName = "Arial" };
            table.ColumnWidths.Add(12.0); // nr.
            table.ColumnWidths.Add(50.0); // blok naam
            table.ColumnWidths.Add(22.0); // aantal
            if (hasDesc) table.ColumnWidths.Add(60.0);
            foreach (var _ in allKeys) table.ColumnWidths.Add(35.0);

            // ── Kopregel (zelfde stijl als statische BOM: lichtblauw) ──
            var header = new TableRowData { IsHeader = true };
            header.Cells.Add(MakeCell("Nr.",          bold: true, bg: "D6EAF8", ha: HA.Center));
            header.Cells.Add(MakeCell("Blok naam",    bold: true, bg: "D6EAF8", ha: HA.Left));
            header.Cells.Add(MakeCell("Aantal",       bold: true, bg: "D6EAF8", ha: HA.Center));
            if (hasDesc)
                header.Cells.Add(MakeCell("Omschrijving", bold: true, bg: "D6EAF8"));
            foreach (var key in allKeys)
                header.Cells.Add(MakeCell(key ?? "", bold: true, bg: "D6EAF8"));
            table.Rows.Add(header);
            table.RowHeights.Add(8.0);

            // ── Datarijen ──
            bool odd = false;
            int rowNr = 1;
            foreach (var (def, instances) in groups)
            {
                string? stripe = odd ? "F0F4F8" : null;
                var row = new TableRowData();
                row.Cells.Add(MakeCell((rowNr++).ToString(), bg: stripe, ha: HA.Center));
                row.Cells.Add(MakeCell(def.Name, bg: stripe));
                // Dynamisch veld: update automatisch als blokken worden toegevoegd/verwijderd
                row.Cells.Add(MakeCell($"%<BlockInstanceCount(\"{def.Name}\")>%",
                                       bg: stripe, ha: HA.Center));
                if (hasDesc)
                    row.Cells.Add(MakeCell(def.Description ?? "", bg: stripe));

                // User-text: unieke waarden samenvoegen per sleutel
                foreach (var key in allKeys)
                {
                    var vals = instances
                        .Select(o => o.Attributes.GetUserStrings()?.Get(key) ?? "")
                        .Where(v => !string.IsNullOrEmpty(v))
                        .Distinct()
                        .ToList();
                    row.Cells.Add(MakeCell(string.Join(", ", vals), bg: stripe));
                }

                table.Rows.Add(row);
                table.RowHeights.Add(8.0);
                odd = !odd;
            }

            return table;
        }

        // BOM per instantie — één rij per blokinstantie, attribuutvelden zijn
        // dynamische %<UserText("ID","Key")>% velden die in de viewport updaten.
        private TableData? BuildBomPerInstanceTable()
        {
            var instances = _doc.Objects
                .OfType<Rhino.DocObjects.InstanceObject>()
                .Where(o => !o.IsDeleted)
                .OrderBy(o => o.InstanceDefinition.Name)
                .ToList();

            if (instances.Count == 0)
            {
                MessageBox.Show("Er zijn geen blokinstanties gevonden in het document.",
                                "BOM per instantie", MessageBoxButton.OK, MessageBoxImage.Information);
                return null;
            }

            // Alle user-text sleutels die op minstens één instantie voorkomen
            var allKeys = instances
                .SelectMany(o => o.Attributes.GetUserStrings()?.AllKeys
                                 ?? Array.Empty<string>())
                .Where(k => !string.IsNullOrEmpty(k))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(k => k)
                .ToList();

            bool hasName = instances.Any(o => !string.IsNullOrEmpty(o.Name));

            if (MessageBox.Show(
                    $"Gevonden: {instances.Count} instantie(s)" +
                    (allKeys.Count > 0 ? $" met {allKeys.Count} user-text sleutel(s)" : " (geen user-text)") +
                    ".\nHuidige tabelinhoud vervangen door een dynamische BOM per instantie?",
                    "BOM per instantie", MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes) return null;

            var table = new TableData { DefaultFontSize = 3.5, DefaultFontName = "Arial" };
            table.ColumnWidths.Add(12.0); // nr.
            table.ColumnWidths.Add(50.0); // blok naam
            if (hasName) table.ColumnWidths.Add(40.0); // object naam
            foreach (var _ in allKeys) table.ColumnWidths.Add(35.0);

            // ── Kopregel (lichtblauw) ──
            var header = new TableRowData { IsHeader = true };
            header.Cells.Add(MakeCell("Nr.",         bold: true, bg: "D6EAF8", ha: HA.Center));
            header.Cells.Add(MakeCell("Blok naam",   bold: true, bg: "D6EAF8"));
            if (hasName)
                header.Cells.Add(MakeCell("Naam",    bold: true, bg: "D6EAF8"));
            foreach (var key in allKeys)
                header.Cells.Add(MakeCell(key ?? "", bold: true, bg: "D6EAF8"));
            table.Rows.Add(header);
            table.RowHeights.Add(8.0);

            // ── Datarijen — één per instantie ──
            bool odd = false;
            int rowNr = 1;
            foreach (var inst in instances)
            {
                string? stripe = odd ? "F0F4F8" : null;
                var id = inst.Id.ToString();
                var row = new TableRowData();

                row.Cells.Add(MakeCell((rowNr++).ToString(), bg: stripe, ha: HA.Center));
                // Blok naam via dynamisch veld — update als de definitie hernoemd wordt
                row.Cells.Add(MakeCell($"%<BlockName(\"{id}\")>%", bg: stripe));

                if (hasName)
                    row.Cells.Add(MakeCell($"%<ObjectName(\"{id}\")>%", bg: stripe));

                // Attribuut-velden: dynamisch — update wanneer user-text wordt gewijzigd
                foreach (var key in allKeys)
                    row.Cells.Add(MakeCell($"%<UserText(\"{id}\",\"{key}\")>%", bg: stripe));

                table.Rows.Add(row);
                table.RowHeights.Add(8.0);
                odd = !odd;
            }

            return table;
        }

        // Bouwt een standaard titel blok met Rhino tekstvelden.
        private static TableData BuildTitleBlockTable()
        {
            var rows = new (string Label, string Value)[]
            {
                ("Project",         "%<DocumentText(\"Project\")>%"),
                ("Titel",           "%<DocumentText(\"Title\")>%"),
                ("Opdrachtgever",   "%<DocumentText(\"Client\")>%"),
                ("Getekend door",   "%<DocumentText(\"DrawnBy\")>%"),
                ("Gecontroleerd",   "%<DocumentText(\"CheckedBy\")>%"),
                ("Datum",           "%<Date(\"dd-MM-yyyy\")>%"),
                ("Revisie",         "%<DocumentText(\"Revision\")>%"),
                ("Schaal",          "%<DocumentText(\"Scale\")>%"),
                ("Bestand",         "%<FileName(\"1\")>%"),
                ("Pagina",          "%<PageNumber()>% / %<NumPages()>%"),
            };

            var table = new TableData { DefaultFontSize = 3.5, DefaultFontName = "Arial" };
            table.ColumnWidths.Add(35.0);  // label kolom
            table.ColumnWidths.Add(75.0);  // waarde kolom

            foreach (var (label, value) in rows)
            {
                var row = new TableRowData();
                row.Cells.Add(MakeCell(label, bold: true, bg: "163F60", fg: "FFFFFF", ha: HA.Left));
                row.Cells.Add(MakeCell(value, ha: HA.Left));
                table.Rows.Add(row);
                table.RowHeights.Add(8.0);
            }

            return table;
        }

        // Hulpfunctie: maakt een cel met veelgebruikte opties.
        private static TableCellData MakeCell(
            string text, bool bold = false,
            string? bg = null, string? fg = null,
            HA ha = HA.Left)
        {
            var c = new TableCellData
            {
                Text                = text,
                Bold                = bold,
                HorizontalAlignment = ha,
                VerticalAlignment   = VA.Middle,
            };
            if (bg != null) { c.BackgroundColor = bg; c.FillPattern = 1; }
            if (fg != null) c.TextColor = fg;
            return c;
        }


        private void OpenTemplates_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new TemplateManagerWindow(_vm.TableData) { Owner = this };
            if (dlg.ShowDialog() != true) return;

            if (dlg.LoadedGeneratorId != null)
            {
                TableData? generated = dlg.LoadedGeneratorId switch
                {
                    "BOM_TYPE"     => BuildBomTable(),
                    "BOM_INSTANCE" => BuildBomPerInstanceTable(),
                    _              => null
                };
                if (generated != null) _vm.ReplaceTableData(generated);
            }
            else if (dlg.LoadedTemplate != null)
            {
                _vm.ReplaceTableData(dlg.LoadedTemplate);
            }
        }

        private static string ColLetter(int index)
        {
            string r = string.Empty; index++;
            while (index > 0) { index--; r = (char)('A' + index % 26) + r; index /= 26; }
            return r;
        }

        // ── Bijzondere tekens ─────────────────────────────────────────────────

        private static readonly (string Title, string[] Chars)[] SpecialCharGroups =
        {
            ("Common",              new[] { "°","±","≈","≠","≤","≥","×","÷","∞","‰","′","″" }),
            ("Geometry",            new[] { "Ø","⌀","□","■","△","▲","○","●","⊙","∠","⊥","∥" }),
            ("Math",                new[] { "√","∑","∫","∂","Δ","∇","∏","∝","∈","∉","⊂","⊃" }),
            ("Arrows",              new[] { "→","←","↑","↓","↔","⇒","⇐","⇑","⇓","⇔","↗","↘","↕" }),
            ("GD&T — Form",         new[] { "⏤","⏥","○","⌭","⌒","⌓" }),
            ("GD&T — Location",     new[] { "⌖","◎","⌯","⊕","⌮","⏦" }),
            ("GD&T — Runout",       new[] { "↻","⌰" }),
            ("GD&T — Modifiers",    new[] { "Ⓜ","Ⓛ","Ⓢ","Ⓟ","Ⓕ","Ⓣ" }),
            ("Currency",            new[] { "€","£","$","¥","¢","₩" }),
            ("Greek (upper)",       new[] { "Α","Β","Γ","Δ","Ε","Ζ","Η","Θ","Λ","Μ","Ν","Ξ","Π","Ρ","Σ","Τ","Υ","Φ","Χ","Ψ","Ω" }),
            ("Greek (lower)",       new[] { "α","β","γ","δ","ε","ζ","η","θ","λ","μ","ν","ξ","π","ρ","σ","τ","υ","φ","χ","ψ","ω" }),
            ("Fractions",           new[] { "½","¼","¾","⅓","⅔","⅛","⅜","⅝","⅞" }),
            ("Misc",                new[] { "©","®","™","§","¶" }),
        };

        private static readonly Dictionary<string, string> CharTooltips = new()
        {
            ["°"]="Degree", ["±"]="Plus-minus", ["≈"]="Approximately equal",
            ["≠"]="Not equal", ["≤"]="Less than or equal", ["≥"]="Greater than or equal",
            ["×"]="Multiplication", ["÷"]="Division", ["∞"]="Infinity",
            ["‰"]="Per mille", ["′"]="Prime — feet / arc-minutes",
            ["″"]="Double prime — inches / arc-seconds",
            ["Ø"]="Diameter Ø", ["⌀"]="Diameter symbol", ["⊙"]="Circle with centre dot",
            ["∠"]="Angle", ["⊥"]="Perpendicular", ["∥"]="Parallel",
            ["√"]="Square root", ["∑"]="Summation", ["∫"]="Integral",
            ["∂"]="Partial derivative", ["Δ"]="Delta — change / difference",
            ["∇"]="Nabla — gradient", ["∏"]="Product", ["∝"]="Proportional to",
            ["∈"]="Element of", ["∉"]="Not element of",
            ["→"]="Right arrow", ["←"]="Left arrow",
            ["↑"]="Up arrow", ["↓"]="Down arrow", ["↔"]="Bidirectional arrow",
            ["⇒"]="Implies", ["⇐"]="Follows from", ["⇑"]="Double up arrow",
            ["⇓"]="Double down arrow", ["⇔"]="If and only if",
            ["↗"]="Up-right arrow", ["↘"]="Down-right arrow", ["↕"]="Up-down arrow",
            ["€"]="Euro", ["£"]="Pound sterling", ["¥"]="Yen", ["¢"]="Cent", ["₩"]="Won",
            ["©"]="Copyright", ["®"]="Registered trademark", ["™"]="Trademark",
            ["§"]="Section", ["¶"]="Paragraph",
            ["½"]="One half", ["¼"]="One quarter", ["¾"]="Three quarters",
            ["⅓"]="One third", ["⅔"]="Two thirds",
            ["⅛"]="One eighth", ["⅜"]="Three eighths", ["⅝"]="Five eighths", ["⅞"]="Seven eighths",
            // GD&T — Form & Profile (ISO 1101 / ASME Y14.5)
            ["⏤"]="Straightness",
            ["⏥"]="Flatness",
            ["⌭"]="Cylindricity",
            ["⌒"]="Profile of a line",
            ["⌓"]="Profile of a surface",
            // GD&T — Location & Other
            ["⌖"]="True position",
            ["◎"]="Concentricity / Coaxiality",
            ["⌯"]="Symmetry",
            ["⊕"]="Position (circled plus)",
            ["⌮"]="All-around profile",
            ["⏦"]="Between",
            // GD&T — Runout
            ["↻"]="Circular runout",
            ["⌰"]="Total runout",
            // GD&T — Material condition modifiers
            ["Ⓜ"]="Maximum Material Condition (MMC)",
            ["Ⓛ"]="Least Material Condition (LMC)",
            ["Ⓢ"]="Regardless of Feature Size (RFS)",
            ["Ⓟ"]="Projected tolerance zone",
            ["Ⓕ"]="Free state",
            ["Ⓣ"]="Tangent plane",
        };

        private void SpecialCharPopup_Opened(object sender, EventArgs e)
        {
            if (_specialCharPanelBuilt) return;
            _specialCharPanelBuilt = true;

            var charFont   = new FontFamily("Segoe UI Symbol");
            var headerBrush = new SolidColorBrush(Color.FromRgb(0xA8, 0xC8, 0xE8));
            var sepBrush    = new SolidColorBrush(Color.FromRgb(0x3A, 0x7A, 0xB0));

            bool first = true;
            foreach (var (title, chars) in SpecialCharGroups)
            {
                if (!first)
                {
                    SpecialCharPanel.Children.Add(new System.Windows.Shapes.Rectangle
                    {
                        Height = 1, Fill = sepBrush, Margin = new Thickness(0, 4, 0, 0)
                    });
                }
                first = false;

                SpecialCharPanel.Children.Add(new TextBlock
                {
                    Text       = title,
                    Foreground = headerBrush,
                    FontSize   = 9,
                    FontWeight = FontWeights.Bold,
                    Margin     = new Thickness(2, 4, 2, 2)
                });

                var wrap = new WrapPanel { Width = 264 };
                foreach (var ch in chars)
                {
                    CharTooltips.TryGetValue(ch, out string? tip);
                    var btn = new Button
                    {
                        Content     = ch,
                        FontFamily  = charFont,
                        ToolTip     = tip ?? ch,
                        Style       = (Style)FindResource("SpecialCharBtn"),
                        Focusable   = false,
                    };
                    btn.Click += SpecialChar_Click;
                    wrap.Children.Add(btn);
                }
                SpecialCharPanel.Children.Add(wrap);
            }
        }

        private void SpecialChar_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Content is not string ch) return;
            _vm.PushUndoSnapshot();
            InsertSpecialChar(ch);
            SpecialCharToggle.IsChecked = false;
        }

        private void InsertSpecialChar(string ch)
        {
            if (_lastCellRow < 0 || _lastCellCol < 0) return;
            var rows = _vm.TableData.Rows;
            if (_lastCellRow >= rows.Count) return;
            var cells = rows[_lastCellRow].Cells;
            if (_lastCellCol >= cells.Count) return;

            var box   = _lastCellTextBox;
            bool live = box is { IsVisible: true };

            // Cursor en selectie vastleggen vóórdat UpdateSource de TextBox mogelijk reset
            int savedCaret  = live ? box!.CaretIndex      : _lastCaretPos;
            int savedSelLen = live ? box!.SelectionLength : 0;

            if (live)
            {
                // Getypte tekst (uncommitted, LostFocus-binding) naar model flushen
                box!.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            }

            // Model lezen — na UpdateSource heeft het de actuele waarde
            string current  = cells[_lastCellCol].Text ?? string.Empty;

            // SelectionLength > 0 = SelectAll-toestand → achteraan invoegen
            // SelectionLength = 0 = cursor handmatig geplaatst → op cursorpositie invoegen
            int insertPos   = savedSelLen > 0 ? current.Length : Math.Min(savedCaret, current.Length);
            string newText  = current.Insert(insertPos, ch);
            int targetCaret = insertPos + ch.Length;

            cells[_lastCellCol].Text = newText;  // model bijwerken

            if (live)
            {
                // Binding garandeert geen instantane update van de TextBox via PropertyChanged,
                // dus TextBox ook direct zetten zodat de gebruiker de wijziging meteen ziet
                // én LostFocus later niet de oude waarde overschrijft.
                box!.Text       = newText;
                box!.CaretIndex = targetCaret;
            }
            else
            {
                NavigateToCell(_lastCellRow, _lastCellCol);
                TableGrid.BeginEdit();
                Dispatcher.BeginInvoke(() =>
                {
                    if (_lastCellTextBox is { IsVisible: true } b)
                        b.CaretIndex = Math.Min(targetCaret, b.Text?.Length ?? 0);
                }, System.Windows.Threading.DispatcherPriority.Input);
            }
        }

        // ── Rhino text fields ─────────────────────────────────────────────────

        // Sub-menu opties voor datum, paginanummer en eenheden.
        private static readonly (string Label, string Value)[] DateFormats =
        {
            ("Default",                "%<Date()>%"),
            ("dd-MM-yyyy",            "%<Date(\"dd-MM-yyyy\")>%"),
            ("dd/MM/yyyy",            "%<Date(\"dd/MM/yyyy\")>%"),
            ("MM/dd/yyyy",            "%<Date(\"MM/dd/yyyy\")>%"),
            ("yyyy-MM-dd",            "%<Date(\"yyyy-MM-dd\")>%"),
            ("d MMMM yyyy",           "%<Date(\"d MMMM yyyy\")>%"),
            ("dd-MM-yyyy HH:mm",      "%<Date(\"dd-MM-yyyy HH:mm\")>%"),
            ("HH:mm  (tijd only)",    "%<Date(\"HH:mm\")>%"),
        };

        private static readonly (string Label, string Value)[] DateModFormats =
        {
            ("Default",                "%<DateModified()>%"),
            ("dd-MM-yyyy",            "%<DateModified(\"dd-MM-yyyy\")>%"),
            ("dd/MM/yyyy",            "%<DateModified(\"dd/MM/yyyy\")>%"),
            ("MM/dd/yyyy",            "%<DateModified(\"MM/dd/yyyy\")>%"),
            ("yyyy-MM-dd",            "%<DateModified(\"yyyy-MM-dd\")>%"),
            ("d MMMM yyyy",           "%<DateModified(\"d MMMM yyyy\")>%"),
            ("dd-MM-yyyy HH:mm",      "%<DateModified(\"dd-MM-yyyy HH:mm\")>%"),
            ("HH:mm  (tijd only)",    "%<DateModified(\"HH:mm\")>%"),
        };

        private static readonly (string Label, string Value)[] PageNumberOptions =
        {
            ("Huidig paginanummer",    "%<PageNumber()>%"),
            ("Pagina + 1",             "%<PageNumber() + 1>%"),
            ("Pagina + 2",             "%<PageNumber() + 2>%"),
            ("Pagina - 1",             "%<PageNumber() - 1>%"),
        };

        // Prefix "PICK_BLOCK:" → block-name picker dialog
        // Prefix "PICK_OBJ:"  → pick object from viewport, {ID} replaced by GUID
        private static readonly (string Title, (string Label, string Field, string Tip)[] Items)[] FieldGroups =
        {
            ("Document", new[]
            {
                ("File name",          "%<FileName(\"1\")>%",  "Short file name with extension"),
                ("File name (no ext)", "%<FileName(\"3\")>%",  "Short file name without extension"),
                ("Full path",          "%<FileName(\"0\")>%",  "Full path including extension"),
                ("Date…",              "SUBMENU_DATE",         "Current date — choose format"),
                ("Date modified…",     "SUBMENU_DATE_MOD",     "Last saved date — choose format"),
                ("Units",              "%<ModelUnits()>%",     "Current model unit system (mm, m, …)"),
                ("Notes",              "%<Notes()>%",          "Document notes (File > Notes)"),
            }),
            ("Document User Text", new[]
            {
                ("Project",      "%<DocumentText(\"Project\")>%",    "DocumentText key \"Project\""),
                ("Title",        "%<DocumentText(\"Title\")>%",      "DocumentText key \"Title\""),
                ("Drawn by",     "%<DocumentText(\"DrawnBy\")>%",    "DocumentText key \"DrawnBy\""),
                ("Checked by",   "%<DocumentText(\"CheckedBy\")>%",  "DocumentText key \"CheckedBy\""),
                ("Revision",     "%<DocumentText(\"Revision\")>%",   "DocumentText key \"Revision\""),
                ("Scale",        "%<DocumentText(\"Scale\")>%",      "DocumentText key \"Scale\""),
                ("Client",       "%<DocumentText(\"Client\")>%",     "DocumentText key \"Client\""),
                ("Custom…",      "%<DocumentText(\"key\")>%",        "Replace \"key\" with your own key name"),
            }),
            ("Layout", new[]
            {
                ("Page number…",  "SUBMENU_PAGE_NUM",   "Current layout page number — choose offset"),
                ("Page count",    "%<NumPages()>%",     "Total number of layout pages"),
                ("Page name",     "%<PageName()>%",     "Current layout page name"),
                ("Page width",    "%<PageWidth()>%",    "Width of the current layout page"),
                ("Page height",   "%<PageHeight()>%",   "Height of the current layout page"),
                ("Paper name",    "%<PaperName()>%",    "Selected paper size name"),
                ("Layout user text…", "%<LayoutUserText(\"key\")>%", "Replace \"key\" with your own layout user text key"),
            }),
            ("Object — pick from viewport", new[]
            {
                ("Block count…",        "PICK_BLOCK:%<BlockInstanceCount(\"{NAME}\")>%",
                                        "Pick a block definition → inserts instance count"),
                ("Block name…",         "PICK_OBJ:%<BlockName(\"{ID}\")>%",
                                        "Pick a block instance → inserts its definition name"),
                ("Block description…",  "PICK_OBJ:%<BlockDescription(\"{ID}\")>%",
                                        "Pick a block instance → inserts its description"),
                ("Object name…",        "PICK_OBJ:%<ObjectName(\"{ID}\")>%",
                                        "Pick any object → inserts its name"),
                ("Object layer…",       "PICK_OBJ:%<ObjectLayer(\"{ID}\")>%",
                                        "Pick any object → inserts its layer name"),
                ("Curve length…",       "PICK_CURVE_LEN",
                                        "Pick a curve → choose unit → inserts its length"),
                ("Area…",               "PICK_AREA",
                                        "Pick a closed curve, hatch or surface → choose unit → inserts its area"),
                ("Volume…",             "PICK_VOL",
                                        "Pick a closed polysurface or mesh → choose unit / open objects → inserts its volume"),
                ("Object page name…",   "PICK_OBJ:%<ObjectPageName(\"{ID}\")>%",
                                        "Pick any object → inserts the layout page it lives on"),
                ("Object page number…", "PICK_OBJ:%<ObjectPageNumber(\"{ID}\")>%",
                                        "Pick any object → inserts the layout page number it lives on"),
                ("Object user text…",   "PICK_OBJUTEXT",
                                        "Pick an object → choose a user-text key → inserts the value"),
            }),
        };

        private void FieldsPopup_Opened(object sender, EventArgs e)
        {
            if (_fieldsPanelBuilt) return;
            _fieldsPanelBuilt = true;

            var headerBrush = new SolidColorBrush(Color.FromRgb(0xA8, 0xC8, 0xE8));
            var sepBrush    = new SolidColorBrush(Color.FromRgb(0x3A, 0x7A, 0xB0));
            bool first = true;

            foreach (var (title, items) in FieldGroups)
            {
                if (!first)
                    FieldsPanel.Children.Add(new System.Windows.Shapes.Rectangle
                        { Height = 1, Fill = sepBrush, Margin = new Thickness(0, 6, 0, 0) });
                first = false;

                FieldsPanel.Children.Add(new TextBlock
                {
                    Text       = title,
                    Foreground = headerBrush,
                    FontSize   = 9,
                    FontWeight = FontWeights.Bold,
                    Margin     = new Thickness(2, 4, 2, 2),
                });

                foreach (var (label, field, tip) in items)
                {
                    // Knop toont: "Label  %<Field()>%"
                    var panel = new StackPanel { Orientation = Orientation.Horizontal };
                    panel.Children.Add(new TextBlock
                    {
                        Text = label,
                        FontWeight = FontWeights.SemiBold,
                        VerticalAlignment = WinVA.Center,
                    });
                    panel.Children.Add(new TextBlock
                    {
                        Text = $"  {field}",
                        FontSize = 9,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0xBC, 0xDC)),
                        VerticalAlignment = WinVA.Center,
                    });

                    var btn = new Button
                    {
                        Content  = panel,
                        Tag      = field,
                        ToolTip  = tip,
                        Style    = (Style)FindResource("FieldItemBtn"),
                        Focusable = false,
                        Width    = 264,
                    };
                    btn.Click += Field_Click;
                    FieldsPanel.Children.Add(btn);
                }
            }
        }

        private void Field_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string tag) return;
            FieldsToggle.IsChecked = false;

            // Statische submenus (datum, paginanummer)
            if (tag == "SUBMENU_DATE")     { ShowSubMenuAndInsert("Datum formaat", DateFormats);    return; }
            if (tag == "SUBMENU_DATE_MOD") { ShowSubMenuAndInsert("Datum formaat", DateModFormats); return; }
            if (tag == "SUBMENU_PAGE_NUM") { ShowSubMenuAndInsert("Paginanummer",  PageNumberOptions); return; }

            // Viewport-pick met eenheidkeuze
            if (tag == "PICK_CURVE_LEN")
            {
                Dispatcher.BeginInvoke(() => PickCurveLength(),
                    System.Windows.Threading.DispatcherPriority.Background);
                return;
            }
            if (tag == "PICK_AREA")
            {
                Dispatcher.BeginInvoke(() => PickArea(),
                    System.Windows.Threading.DispatcherPriority.Background);
                return;
            }
            if (tag == "PICK_VOL")
            {
                Dispatcher.BeginInvoke(() => PickVolume(),
                    System.Windows.Threading.DispatcherPriority.Background);
                return;
            }

            if (tag.StartsWith("PICK_BLOCK:"))
            {
                Dispatcher.BeginInvoke(
                    () => PickBlockNameAndInsert(tag["PICK_BLOCK:".Length..]),
                    System.Windows.Threading.DispatcherPriority.Background);
                return;
            }
            if (tag.StartsWith("PICK_OBJ:"))
            {
                string template = tag["PICK_OBJ:".Length..];
                Dispatcher.BeginInvoke(
                    () => PickObjectAndInsert(template),
                    System.Windows.Threading.DispatcherPriority.Background);
                return;
            }
            if (tag == "PICK_OBJUTEXT")
            {
                Dispatcher.BeginInvoke(
                    () => PickObjectUserTextField(),
                    System.Windows.Threading.DispatcherPriority.Background);
                return;
            }

            // Gewoon veld — direct invoegen
            _vm.PushUndoSnapshot();
            InsertSpecialChar(tag);
        }

        // Toont een picker met format-opties en voegt de gekozen waarde in.
        private void ShowSubMenuAndInsert(string title, (string Label, string Value)[] options)
        {
            var labels = options.Select(o => o.Label).ToList();
            var picked = ShowItemPickerDialog(title, "Kies optie:", labels);
            if (picked == null) return;
            var value = options.First(o => o.Label == picked).Value;
            _vm.PushUndoSnapshot();
            InsertSpecialChar(value);
        }

        // Pick curve → toon eenheidkeuze → voeg CurveLength-veld in.
        private void PickCurveLength()
        {
            if (!DoViewportPick("Select curve for length field", out Guid id)) return;
            var opts = new (string L, string? U)[]
            {
                ("Annotation style (default)", null),
                ("mm",   "Millimeters"),  ("cm",  "Centimeters"),  ("m",   "Meters"),
                ("km",   "kilometers"),  ("inch","Inches"), ("ft",  "Feet"),
            };
            var label = ShowItemPickerDialog("Lengte eenheid", "Eenheid:", opts.Select(o => o.L).ToList());
            if (label == null) return;
            var unit  = opts.First(o => o.L == label).U;
            var field = unit is null
                ? $"%<CurveLength(\"{id}\")>%"
                : $"%<CurveLength(\"{id}\",\"{unit}\")>%";
            _vm.PushUndoSnapshot();
            InsertSpecialChar(field);
        }

        // Pick object → toon eenheidkeuze → voeg Area-veld in.
        private void PickArea()
        {
            if (!DoViewportPick("Select object for area field", out Guid id)) return;
            var opts = new (string L, string? U)[]
            {
                ("Annotation style (default)", null),
                ("mm²",   "Millimeters"), ("cm²",   "Centimeters"), ("m²",    "Meters"),
                ("km²",   "kilometers"), ("inch²",  "Inches"), ("ft²",   "Feet"),
            };
            var label = ShowItemPickerDialog("Oppervlakte eenheid", "Eenheid:", opts.Select(o => o.L).ToList());
            if (label == null) return;
            var unit  = opts.First(o => o.L == label).U;
            var field = unit is null
                ? $"%<Area(\"{id}\")>%"
                : $"%<Area(\"{id}\",\"{unit}\")>%";
            _vm.PushUndoSnapshot();
            InsertSpecialChar(field);
        }

        // Pick object → toon eenheid + open-optie → voeg Volume-veld in.
        private void PickVolume()
        {
            if (!DoViewportPick("Select object for volume field", out Guid id)) return;
            var opts = new (string L, string Field)[]
            {
                ("Standard unit  —  Closed",  $"%<Volume(\"{id}\")>%"),
                ("Standard unit  —  Open",  $"%<Volume(\"{id}\",\"True\")>%"),
                ("mm³  —  Closed",                $"%<Volume(\"{id}\",\"Millimeters\")>%"),
                ("mm³  —  Open",                $"%<Volume(\"{id}\",\"Millimeters\",\"True\")>%"),
                ("cm³  —  Closed",                $"%<Volume(\"{id}\",\"Centimeters\")>%"),
                ("cm³  —  Open",                $"%<Volume(\"{id}\",\"Centimeters\",\"True\")>%"),
                ("m³   —  Closed",                $"%<Volume(\"{id}\",\"Meters\")>%"),
                ("m³   —  Open",                $"%<Volume(\"{id}\",\"Meters\",\"True\")>%"),
            };
            var label = ShowItemPickerDialog("Volume optie", "Eenheid / open objecten:", opts.Select(o => o.L).ToList());
            if (label == null) return;
            var field = opts.First(o => o.L == label).Field;
            _vm.PushUndoSnapshot();
            InsertSpecialChar(field);
        }

        // Toont een lijst van blokdefinities; vervangt {NAME} in het template.
        private void PickBlockNameAndInsert(string fieldTemplate)
        {
            var names = _doc.InstanceDefinitions
                .Where(d => !d.IsDeleted)
                .OrderBy(d => d.Name)
                .Select(d => d.Name)
                .ToList();

            if (names.Count == 0)
            {
                MessageBox.Show("No blocks found in the document.", "No Blocks",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string? picked = ShowItemPickerDialog("Select Block", "Block definition:", names);
            if (picked == null) return;

            _vm.PushUndoSnapshot();
            InsertSpecialChar(fieldTemplate.Replace("{NAME}", picked));
        }

        // ── Viewport pick — gedeelde helper ──────────────────────────────────
        // Minimaliseert het venster, toont een floating instructie-overlay,
        // laat de gebruiker een object kiezen en herstelt het venster daarna.
        private bool DoViewportPick(string prompt, out Guid pickedId)
        {
            pickedId    = Guid.Empty;
            WindowState = WindowState.Minimized;

            var go = new Rhino.Input.Custom.GetObject();
            go.SetCommandPrompt(prompt);
            // Exclude our own RhinoTable block instances from the pick
            go.SetCustomGeometryFilter((rhinoObj, _, _) =>
                rhinoObj is not Rhino.DocObjects.InstanceObject inst ||
                !inst.InstanceDefinition.Name.StartsWith("RhinoTable_", StringComparison.Ordinal));

            var overlay = BuildPickOverlay();
            overlay.Show();

            go.Get();

            overlay.Close();
            WindowState = WindowState.Normal;
            Activate();

            if (go.CommandResult() != Rhino.Commands.Result.Success) return false;
            pickedId = go.Object(0).ObjectId;
            return true;
        }

        // P/Invoke — stuurt een bericht rechtstreeks naar een venster-HWND.
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        // Klein altijd-zichtbaar kaartje dat de gebruiker begeleidt tijdens het pieken.
        // Annuleren stuurt WM_KEYDOWN Escape naar Rhino's hoofdvenster.
        private static Window BuildPickOverlay()
        {
            var overlay = new Window
            {
                WindowStyle           = WindowStyle.None,
                AllowsTransparency    = true,
                Background            = Brushes.Transparent,
                Topmost               = true,
                ShowInTaskbar         = false,
                Width                 = 248,
                Height                = 72,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode            = ResizeMode.NoResize,
            };

            var border = new Border
            {
                Background      = new SolidColorBrush(Color.FromRgb(0x16, 0x3F, 0x60)),
                BorderBrush     = new SolidColorBrush(Color.FromRgb(0x3A, 0x7A, 0xB0)),
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(6),
            };

            var icon = new TextBlock
            {
                Text              = "⊕",
                FontSize          = 22,
                Foreground        = new SolidColorBrush(Color.FromRgb(0x5B, 0xA3, 0xD9)),
                VerticalAlignment = WinVA.Center,
                Margin            = new Thickness(12, 0, 8, 0),
            };

            var msg = new TextBlock
            {
                Text              = "Click an object\nin the Rhino viewport",
                FontSize          = 11,
                Foreground        = Brushes.White,
                VerticalAlignment = WinVA.Center,
            };

            var cancelBtn = new Button
            {
                Content         = "✕",
                Width           = 24,
                Height          = 24,
                FontSize        = 12,
                Foreground      = Brushes.White,
                Background      = new SolidColorBrush(Color.FromRgb(0x8B, 0x22, 0x22)),
                BorderThickness = new Thickness(0),
                Margin          = new Thickness(10, 0, 10, 0),
                VerticalAlignment = WinVA.Center,
                ToolTip         = "Cancel (or press Esc in the viewport)",
            };
            cancelBtn.Click += (_, _2) =>
            {
                // Post Escape to Rhino's main window to abort the GetObject pick
                const uint WM_KEYDOWN = 0x0100;
                const uint WM_KEYUP   = 0x0101;
                var hwnd = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                PostMessage(hwnd, WM_KEYDOWN, new IntPtr(0x1B), IntPtr.Zero);
                PostMessage(hwnd, WM_KEYUP,   new IntPtr(0x1B), IntPtr.Zero);
            };

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(icon);
            row.Children.Add(msg);
            row.Children.Add(cancelBtn);

            border.Child   = row;
            overlay.Content = border;
            return overlay;
        }

        private void PickObjectAndInsert(string fieldTemplate)
        {
            if (!DoViewportPick("Select object for text field", out Guid id)) return;
            _vm.PushUndoSnapshot();
            InsertSpecialChar(fieldTemplate.Replace("{ID}", id.ToString()));
        }

        // ObjectUserText: pik object → toon beschikbare sleutels → voeg veld in.
        private void PickObjectUserTextField()
        {
            if (!DoViewportPick("Select object for UserText field", out Guid id)) return;

            var obj  = _doc.Objects.FindId(id);
            var keys = obj?.Attributes.GetUserStrings()?.AllKeys
                          .Where(k => !string.IsNullOrEmpty(k))
                          .Select(k => k!)
                          .OrderBy(k => k)
                          .ToList()
                       ?? new List<string>();

            string? key = keys.Count > 0
                ? ShowItemPickerDialog("Select User Text Key",
                      $"Keys on \"{obj?.Name ?? id.ToString()}\":", keys)
                : ShowTextInputDialog("Enter User Text Key",
                      $"No existing keys on \"{obj?.Name ?? id.ToString()}\".\nEnter key name:");

            if (string.IsNullOrEmpty(key)) return;
            _vm.PushUndoSnapshot();
            InsertSpecialChar($"%<UserText(\"{id}\",\"{key}\")>%");
        }

        // Lichte WPF-keuzelijst (hergebruikbaar voor blokken en sleutels).
        private string? ShowItemPickerDialog(string title, string prompt, List<string> items)
        {
            var dlg = new Window
            {
                Title  = title,
                Width  = 300,
                Height = 380,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner        = this,
                ResizeMode   = ResizeMode.NoResize,
                Background   = new SolidColorBrush(Color.FromRgb(0x2B, 0x4A, 0x6B)),
                Foreground   = Brushes.White,
                ShowInTaskbar = false,
            };

            string? result = null;

            var lb = new ListBox
            {
                ItemsSource  = items,
                Margin       = new Thickness(8, 0, 8, 4),
                FontSize     = 12,
                Background   = new SolidColorBrush(Color.FromRgb(0x1A, 0x3A, 0x55)),
                Foreground   = Brushes.White,
                BorderBrush  = new SolidColorBrush(Color.FromRgb(0x3A, 0x7A, 0xB0)),
            };

            var ok  = new Button { Content = "OK",     Width = 80, Margin = new Thickness(4) };
            var can = new Button { Content = "Cancel", Width = 80, Margin = new Thickness(4) };

            ok.Click  += (_, _2) => { result = lb.SelectedItem as string; dlg.DialogResult = result != null; };
            can.Click += (_, _2) => dlg.DialogResult = false;
            lb.MouseDoubleClick += (_, _2) =>
            {
                if (lb.SelectedItem is string s) { result = s; dlg.DialogResult = true; }
            };

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal,
                HorizontalAlignment = WinHA.Right, Margin = new Thickness(8) };
            btnRow.Children.Add(ok);
            btnRow.Children.Add(can);

            var root = new StackPanel();
            root.Children.Add(new TextBlock
            {
                Text = prompt, Margin = new Thickness(8, 8, 8, 4),
                FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0xA8, 0xC8, 0xE8)),
            });
            root.Children.Add(lb);
            root.Children.Add(btnRow);
            dlg.Content = root;

            dlg.ShowDialog();
            return result;
        }

        // Licht WPF-invoervenster voor een vrije tekst-sleutel.
        private string? ShowTextInputDialog(string title, string prompt)
        {
            var dlg = new Window
            {
                Title  = title,
                Width  = 300,
                Height = 170,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner        = this,
                ResizeMode   = ResizeMode.NoResize,
                Background   = new SolidColorBrush(Color.FromRgb(0x2B, 0x4A, 0x6B)),
                ShowInTaskbar = false,
            };

            string? result = null;
            var tb  = new TextBox { Margin = new Thickness(8, 4, 8, 4), FontSize = 12, Height = 26 };
            var ok  = new Button  { Content = "OK",     Width = 80, Margin = new Thickness(4) };
            var can = new Button  { Content = "Cancel", Width = 80, Margin = new Thickness(4) };

            ok.Click  += (_, _2) =>
            {
                result = string.IsNullOrWhiteSpace(tb.Text) ? null : tb.Text.Trim();
                dlg.DialogResult = result != null;
            };
            can.Click += (_, _2) => dlg.DialogResult = false;
            tb.KeyDown  += (_, e) => { if (e.Key == Key.Return) ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); };

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal,
                HorizontalAlignment = WinHA.Right, Margin = new Thickness(8) };
            btnRow.Children.Add(ok);
            btnRow.Children.Add(can);

            var root = new StackPanel();
            root.Children.Add(new TextBlock
            {
                Text = prompt, Margin = new Thickness(8, 8, 8, 2),
                FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0xA8, 0xC8, 0xE8)),
                TextWrapping = TextWrapping.Wrap,
            });
            root.Children.Add(tb);
            root.Children.Add(btnRow);
            dlg.Content = root;

            dlg.ShowDialog();
            return result;
        }

        private void Button_Click(object sender, RoutedEventArgs e) { }
        private void Button_Click_1(object sender, RoutedEventArgs e) { }
        private void Button_Click_2(object sender, RoutedEventArgs e) { }
        private void Button_Click_3(object sender, RoutedEventArgs e) { }
    }
}
