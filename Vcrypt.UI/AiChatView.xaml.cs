using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Vcrypt.UI.ViewModels;

namespace Vcrypt.UI
{
    public partial class AiChatView : UserControl
    {
        public AiChatView()
        {
            InitializeComponent();
        }

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is AiChatViewModel vm)
            {
                vm.Messages.CollectionChanged += (s, ev) =>
                {
                    if (vm.Messages.Count > 0)
                    {
                        var lastItem = vm.Messages[vm.Messages.Count - 1];
                        var listView = this.FindName("ChatListView") as System.Windows.Controls.ListView;
                        listView?.ScrollIntoView(lastItem);
                    }
                };
                await vm.InitializeAsync();
            }
        }

        private void TextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (DataContext is AiChatViewModel vm)
                {
                    if (vm.SendMessageCommand.CanExecute(null))
                    {
                        vm.SendMessageCommand.Execute(null);
                    }
                }
            }
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is AiChatViewModel vm)
            {
                vm.Cleanup();
            }
        }
    }
}
