using Vcrypt.UI.ViewModels;
using System.Windows;
using Wpf.Ui.Controls;
using Wpf.Ui.Appearance;

namespace Vcrypt.UI
{
    public partial class MainWindow : FluentWindow
    {
        private MainWindowViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainWindowViewModel();
            DataContext = _viewModel;
            
            // Set the modern system theme
            SystemThemeWatcher.Watch(this);
        }
    }
}