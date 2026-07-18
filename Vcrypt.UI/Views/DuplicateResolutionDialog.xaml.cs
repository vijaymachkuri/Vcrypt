using System.Windows;
using Vcrypt.Core.Models;

namespace Vcrypt.UI.Views
{
    public partial class DuplicateResolutionDialog : Window
    {
        public DuplicateResolution Result { get; private set; } = DuplicateResolution.Skip;

        public DuplicateResolutionDialog(string fileName, bool isIdentical)
        {
            InitializeComponent();
            FileNameRun.Text = fileName;
            
            if (isIdentical)
            {
                IdenticalWarningBorder.Visibility = Visibility.Visible;
            }
        }

        private void Replace_Click(object sender, RoutedEventArgs e)
        {
            Result = ApplyToAllCheckBox.IsChecked == true ? DuplicateResolution.ReplaceAll : DuplicateResolution.Replace;
            DialogResult = true;
            Close();
        }

        private void Skip_Click(object sender, RoutedEventArgs e)
        {
            Result = ApplyToAllCheckBox.IsChecked == true ? DuplicateResolution.SkipAll : DuplicateResolution.Skip;
            DialogResult = true;
            Close();
        }
    }
}
