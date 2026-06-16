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
        private List<TableTemplate> _templates = new();

        // Gevuld als de gebruiker op "Laden" klikt
        public TableData? LoadedTemplate { get; private set; }

        public TemplateManagerWindow(TableData currentTableData)
        {
            InitializeComponent();
            _currentTableData = currentTableData;
            RefreshList();
        }

        private void RefreshList()
        {
            _templates = TemplateManager.LoadAll();
            TemplateList.ItemsSource = null;
            TemplateList.ItemsSource = _templates;
        }

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

            TemplateNameBox.Text   = t.Name;
            TemplateDescBox.Text   = t.Description;
            TemplateNameBox.IsReadOnly = t.IsBuiltIn;
            TemplateDescBox.IsReadOnly = t.IsBuiltIn;

            LoadButton.IsEnabled   = true;
            DeleteButton.IsEnabled = !t.IsBuiltIn;

            if (t.IsBuiltIn)
            {
                TypeChip.Background   = new SolidColorBrush(Color.FromRgb(0x21, 0x5D, 0x9E));
                TypeLabel.Text        = "INGEBOUWD";
                TypeLabel.Foreground  = Brushes.White;
            }
            else
            {
                TypeChip.Background   = new SolidColorBrush(Color.FromRgb(0x27, 0xAE, 0x60));
                TypeLabel.Text        = "EIGEN SJABLOON";
                TypeLabel.Foreground  = Brushes.White;
            }
        }

        private void Load_Click(object sender, RoutedEventArgs e)
        {
            if (TemplateList.SelectedItem is not TableTemplate t) return;
            LoadedTemplate = TemplateManager.CloneData(t.TableData);
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
            RefreshList();

            // Selecteer het zojuist opgeslagen sjabloon
            TemplateList.SelectedItem = _templates.FirstOrDefault(x => x.Name == template.Name);
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (TemplateList.SelectedItem is not TableTemplate t || t.IsBuiltIn) return;

            var result = MessageBox.Show(
                $"Sjabloon '{t.Name}' verwijderen?",
                "Verwijderen bevestigen",
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
