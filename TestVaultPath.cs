using System;
using System.Diagnostics;
using System.Threading.Tasks;

class Program
{
    static void Main()
    {
        var volPathScript = @"(Get-Partition -DiskNumber 2 | Where-Object { -not $_.DriveLetter } | Get-Volume | Where-Object FileSystemLabel -eq 'SecureVault').Path";
        Console.WriteLine("Script: " + volPathScript);
        
        var bytes = System.Text.Encoding.Unicode.GetBytes(volPathScript);
        var encoded = Convert.ToBase64String(bytes);

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        
        Console.WriteLine("OUT: " + output);
        Console.WriteLine("ERR: " + error);
    }
}
