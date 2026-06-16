using Rhino;
using System.Windows;

namespace RhinoTable.UI.Views
{
    public partial class UpdateNotificationWindow : Window
    {
        public UpdateNotificationWindow(string currentVersion, string latestVersion)
        {
            InitializeComponent();
            MessageBlock.Text =
                $"Huidige versie: v{currentVersion}   →   Nieuwe versie: v{latestVersion}";
        }

        private void Later_Click(object sender, RoutedEventArgs e) => Close();

        private void Update_Click(object sender, RoutedEventArgs e)
        {
            Close();
            RhinoApp.InvokeOnUiThread(() =>
                RhinoApp.RunScript("_PackageManager", false));
        }
    }
}
