using System.Windows;

namespace RhinoTable.UI.Views
{
    public partial class SaveTemplateDialog : Window
    {
        public string TemplateName        { get; private set; } = string.Empty;
        public string TemplateDescription { get; private set; } = string.Empty;

        public SaveTemplateDialog()
        {
            InitializeComponent();
            Loaded += (_, _) => NameBox.Focus();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NameBox.Text))
            {
                MessageBox.Show("Voer een naam in voor het sjabloon.",
                    "Naam verplicht", MessageBoxButton.OK, MessageBoxImage.Warning);
                NameBox.Focus();
                return;
            }
            TemplateName        = NameBox.Text.Trim();
            TemplateDescription = DescBox.Text.Trim();
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
