using System;
using System.Diagnostics;

class Program {
    static void Main() {
        var p = new Process {
            StartInfo = new ProcessStartInfo {
                FileName = "powershell.exe",
                Arguments = "-Command Start-Sleep 3; Write-Host 'Done'",
                UseShellExecute = true,
                Verb = "runAs",
                CreateNoWindow = true
            }
        };
        p.Start();
        Console.WriteLine("Started. Waiting...");
        p.WaitForExit();
        Console.WriteLine("Finished wait.");
    }
}
