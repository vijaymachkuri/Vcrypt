using System.Windows;
using Vcrypt.UI.ViewModels;
using System.Windows.Input;
using Vcrypt.Core.Models;

namespace Vcrypt.UI
{
    public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainWindowViewModel();
        }

        private async void Vault_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (DataContext is MainWindowViewModel vm)
                {
                    await vm.ProcessDroppedFiles(files);
                }
            }
        }

        private void ListViewItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is System.Windows.Controls.ListViewItem item && item.DataContext is EncryptedItemModel model)
            {
                if (DataContext is MainWindowViewModel vm)
                {
                    if (model.IsFolder)
                    {
                        vm.OpenFolderCommand.Execute(model);
                    }
                    else
                    {
                        vm.ExtractItemCommand.Execute(model);
                    }
                }
            }
        }
    }
}