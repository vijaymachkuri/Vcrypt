
using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

class Program {
    static async Task Main() {
        string script = @"
$ProgressPreference = ""SilentlyContinue""
Write-Output ""F""
";
        var bytes = Encoding.Unicode.GetBytes(script);
        var encoded = Convert.ToBase64String(bytes);
        var p = new Process {
            StartInfo = new ProcessStartInfo {
                FileName = "powershell",
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
                RedirectStandardOutput = true,
                UseShellExecute = false
            }
        };
        p.Start();
        string outStr = await p.StandardOutput.ReadToEndAsync();
        Console.WriteLine($"Length: {outStr.Length}, Char: {(int)outStr[0]}, Out: {outStr}");
    }
}

