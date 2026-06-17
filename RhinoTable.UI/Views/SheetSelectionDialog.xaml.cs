using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RhinoTable.UI.Views
{
    public partial class SheetSelectionDialog : Window
    {
        public string? SelectedSheet { get; private set; }

        public SheetSelectionDialog(IEnumerable<string> sheetNames)
        {
            InitializeComponent();
            foreach (var name in sheetNames)
                SheetList.Items.Add(name);
            Loaded += (_, _) =>
            {
                SheetList.SelectedIndex = 0;
                SheetList.Focus();
            };
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (SheetList.SelectedItem == null)
            {
                MessageBox.Show("Select a worksheet first.",
                    "No selection", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            SelectedSheet = SheetList.SelectedItem.ToString();
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void SheetList_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (SheetList.SelectedItem != null)
                Ok_Click(sender, e);
        }
    }
}
