using System.Windows;

namespace RhinoTable.UI.Views
{
    public partial class ImportProgressWindow : Window
    {
        public ImportProgressWindow()
        {
            InitializeComponent();
        }

        public void SetStatus(string message)
        {
            StatusText.Text = message;
        }

        public void SetRows(int rows)
        {
            RowText.Text = $"{rows} rows loaded…";
        }

        public void SetDeterminate(int max)
        {
            ProgressBar.IsIndeterminate = false;
            ProgressBar.Maximum = max;
        }

        public void SetProgress(int value)
        {
            ProgressBar.Value = value;
        }

        private void ProgressBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {

        }
    }
}
