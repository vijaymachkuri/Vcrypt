using System.ComponentModel;
using System.Windows;

namespace Vcrypt.UI
{
    public partial class ProgressWindow : Wpf.Ui.Controls.FluentWindow
    {
        public ProgressWindow()
        {
            InitializeComponent();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            // Only allow closing if we are not actively transferring, or if the main window closes it
            // Ideally we just hide it instead of closing, or handle cancellation cleanly
            e.Cancel = true;
            this.Hide();
        }
    }
}
