using System;
using System.Diagnostics;
using System.Threading.Tasks;

class Program 
{ 
    static async Task Main() 
    { 
        var command = "Clear-Disk -Number 2 -RemoveData -Confirm:$false -WhatIf";
        var commandWithNoProgress = "$ProgressPreference = 'SilentlyContinue';\n" + command;
        var bytes = System.Text.Encoding.Unicode.GetBytes(commandWithNoProgress);
        var encoded = Convert.ToBase64String(bytes);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.Start();
        
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        
        await process.WaitForExitAsync();
        
        Console.WriteLine($"Output: {output}");
        Console.WriteLine($"Error: {error}");
    } 
}
