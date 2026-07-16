using System.Configuration;
using System.Data;
using System.Windows;

namespace Vcrypt.UI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        RestoreSystemSettings();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        RestoreSystemSettings();
        base.OnExit(e);
    }

    private void RestoreSystemSettings()
    {
        try
        {
            var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = "powershell.exe";
            process.StartInfo.Arguments = "-NoProfile -NonInteractive -WindowStyle Hidden -Command \"mountvol /E; Start-Service -Name ShellHWDetection -ErrorAction SilentlyContinue\"";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.Start();
            process.WaitForExit(3000);
        }
        catch { }
    }
}

