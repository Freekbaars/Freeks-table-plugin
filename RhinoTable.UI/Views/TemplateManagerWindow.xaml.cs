using RhinoTable.Core.Models;
using RhinoTable.Core.Settings;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RhinoTable.UI.Views
{
    public partial class TemplateManagerWindow : Window
    {
        private readonly TableData _currentTableData;
        private List<TableTemplate> _allTemplates = new();

        // Gevuld als de gebruiker op "Laden" klikt
        public TableData? LoadedTemplate    { get; private set; }
        // Gevuld als het gekozen sjabloon een generator is (BOM_TYPE / BOM_INSTANCE)
        public string?    LoadedGeneratorId { get; private set; }

        public TemplateManagerWindow(TableData currentTableData)
        {
            InitializeComponent();
            _currentTableData = currentTableData;
            RefreshList();
        }

        private void RefreshList()
        {
            _allTemplates = TemplateManager.LoadAll();
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            if (FilterBuiltIn == null) return; // controls not yet initialized

            IEnumerable<TableTemplate> filtered = _allTemplates;

            if (FilterBuiltIn.IsChecked == true)
                filtered = _allTemplates.Where(t => t.HasBuiltIn);
            else if (FilterDynamic.IsChecked == true)
                filtered = _allTemplates.Where(t => t.HasDynamic);
            else if (FilterCustom.IsChecked == true)
                filtered = _allTemplates.Where(t => t.HasCustom);

            TemplateList.ItemsSource = null;
            TemplateList.ItemsSource = filtered.ToList();
        }

        private void Filter_Checked(object sender, RoutedEventArgs e)
            => ApplyFilter();

        private void TemplateList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TemplateList.SelectedItem is not TableTemplate t)
            {
                LoadButton.IsEnabled   = false;
                DeleteButton.IsEnabled = false;
                TemplateNameBox.Text   = string.Empty;
                TemplateDescBox.Text   = string.Empty;
                return;
            }

            TemplateNameBox.Text       = t.Name;
            TemplateDescBox.Text       = t.Description;
            TemplateNameBox.IsReadOnly = !t.CanDelete;
            TemplateDescBox.IsReadOnly = !t.CanDelete;

            LoadButton.IsEnabled   = true;
            DeleteButton.IsEnabled = t.CanDelete;

            var parts = new List<string>();
            if (t.HasBuiltIn) parts.Add("Built-in");
            if (t.HasDynamic) parts.Add(t.GeneratorId != null ? "Dynamic (generator)" : "Dynamic");
            if (t.HasCustom)  parts.Add("Custom");
            TypeLabel.Text       = string.Join("  /  ", parts);
            TypeLabel.Foreground = Brushes.White;
            TypeChip.Background  = t.HasBuiltIn
                ? new SolidColorBrush(Color.FromRgb(0x2A, 0x6A, 0x9E))
                : t.HasDynamic
                    ? new SolidColorBrush(Color.FromRgb(0x8E, 0x44, 0xAD))
                    : new SolidColorBrush(Color.FromRgb(0x27, 0xAE, 0x60));
        }

        private void Load_Click(object sender, RoutedEventArgs e)
        {
            if (TemplateList.SelectedItem is not TableTemplate t) return;

            if (t.GeneratorId != null)
            {
                LoadedGeneratorId = t.GeneratorId;
            }
            else
            {
                LoadedTemplate = TemplateManager.CloneData(t.TableData);
            }
            DialogResult = true;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveTemplateDialog { Owner = this };
            if (dlg.ShowDialog() != true) return;

            var template = new TableTemplate
            {
                Name        = dlg.TemplateName,
                Description = dlg.TemplateDescription,
                TableData   = TemplateManager.CloneData(_currentTableData)
            };
            TemplateManager.Save(template);
            FilterAll.IsChecked = true;
            RefreshList();

            // Select the newly saved template
            if (TemplateList.ItemsSource is List<TableTemplate> list)
                TemplateList.SelectedItem = list.FirstOrDefault(x => x.Name == template.Name);
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (TemplateList.SelectedItem is not TableTemplate t || !t.CanDelete) return;

            var result = MessageBox.Show(
                $"Delete template '{t.Name}'?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            TemplateManager.Delete(t);
            RefreshList();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
