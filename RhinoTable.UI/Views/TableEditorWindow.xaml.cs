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

namespace RhinoTable.UI.Views
{
    public partial class TableEditorWindow : Window
    {
        private readonly TableEditorViewModel _vm;
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

        public TableEditorWindow(RhinoDoc doc, TableData tableData)
        {
            InitializeComponent();

            _vm = new TableEditorViewModel(doc, tableData);
            DataContext = _vm;

            _vm.ColumnsChanged       += RebuildColumns;
            _vm.RequestClose         += () => Close();
            _vm.GridRefreshRequested += RefreshGridAfterFormat;
            _vm.GridSyncRequested    += SyncSizesFromDataGrid;
            WireImportProgress();

            // Rijnummers in de rijkopjes — header rijen tonen "H", overige tellen vanaf 1
            TableGrid.LoadingRow += (s, e) =>
            {
                if (e.Row.DataContext is ObservableRow obsRow && obsRow.IsHeader)
                {
                    e.Row.Header = "H";
                    return;
                }
                // Tel alleen niet-header rijen
                int nonHeaderIndex = _vm.GridItems
                    .Take(e.Row.GetIndex())
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
        }

        private DataGridTemplateColumn MakeColumn(int ci, double widthPx)
        {
            // ── CellTemplate ──────────────────────────────────────────────────
            var cellTemplate = new DataTemplate();
            var root = new FrameworkElementFactory(typeof(Grid));

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
                if (s is TextBox box) { box.Focus(); box.SelectAll(); ApplyTextColor(box, ci); }
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

        private void OpenTemplates_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new TemplateManagerWindow(_vm.TableData) { Owner = this };
            if (dlg.ShowDialog() == true && dlg.LoadedTemplate != null)
                _vm.LoadTemplate(dlg.LoadedTemplate);
        }

        private static string ColLetter(int index)
        {
            string r = string.Empty; index++;
            while (index > 0) { index--; r = (char)('A' + index % 26) + r; index /= 26; }
            return r;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {

        }
    }
}
