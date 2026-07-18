using System.Windows;
using Vcrypt.UI.ViewModels;
using System.Windows.Input;
using Vcrypt.Core.Models;
using System.Runtime.InteropServices;

namespace Vcrypt.UI
{
    public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ChangeWindowMessageFilter(uint msg, uint flags);

        private const uint WM_DROPFILES = 0x0233;
        private const uint WM_COPYDATA = 0x004A;
        private const uint WM_COPYGLOBALDATA = 0x0049;
        private const uint MSGFLT_ADD = 1;


        public MainWindow()
        {
            InitializeComponent();
            
            // Allow drag and drop from Explorer when running as Administrator (bypass UIPI)
            ChangeWindowMessageFilter(WM_DROPFILES, MSGFLT_ADD);
            ChangeWindowMessageFilter(WM_COPYDATA, MSGFLT_ADD);
            ChangeWindowMessageFilter(WM_COPYGLOBALDATA, MSGFLT_ADD);

            var vm = new MainWindowViewModel();
            vm.PropertyChanged += Vm_PropertyChanged;
            DataContext = vm;
        }

        private void Vm_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // Progress window logic has been replaced by the in-app progress overlay in MainWindow.xaml
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (VaultNavigationView != null)
            {
                if (e.NewSize.Width < 1000)
                {
                    VaultNavigationView.IsPaneOpen = false;
                }
                else
                {
                    VaultNavigationView.IsPaneOpen = true;
                }
            }
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

        private bool _isClosing = false;
        protected override async void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_isClosing)
            {
                base.OnClosing(e);
                return;
            }

            if (DataContext is MainWindowViewModel vm && vm.CurrentView == "Vault")
            {
                e.Cancel = true; // Cancel immediate close
                _isClosing = true;

                // Hide the window immediately so the user thinks it closed
                this.Hide();

                // Force lock the vault in background
                if (vm.LockVaultCommand.CanExecute(null))
                {
                    await vm.LockVaultCommand.ExecuteAsync(null);
                }
                
                // Now safely close
                this.Close();
            }
            else
            {
                base.OnClosing(e);
            }
        }
    }
}