using System.Diagnostics;
using System.Reflection;
using System.Windows;

namespace RhinoTable.UI.Views
{
    public partial class HelpWindow : Window
    {
        public HelpWindow()
        {
            InitializeComponent();
            var ver = Assembly.GetExecutingAssembly().GetName().Version;
            VersionLabel.Text = ver != null ? $"v{ver.ToString(3)}" : string.Empty;
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void GitHub_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => Process.Start(new ProcessStartInfo("https://github.com/Freekbaars/Freeks-table-plugin")
               { UseShellExecute = true });
    }
}
