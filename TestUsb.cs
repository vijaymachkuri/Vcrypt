using System;

class Program 
{ 
    static void Main() 
    { 
        var process = new System.Diagnostics.Process(); 
        process.StartInfo.FileName = "powershell.exe"; 
        process.StartInfo.Arguments = "-NoProfile -NonInteractive -Command \"(Get-Partition -DriveLetter 'F' -ErrorAction SilentlyContinue | Get-Disk).BusType\""; 
        process.StartInfo.UseShellExecute = false; 
        process.StartInfo.RedirectStandardOutput = true; 
        process.StartInfo.CreateNoWindow = true; 
        process.Start(); 
        var output = process.StandardOutput.ReadToEnd().Trim(); 
        process.WaitForExit(3000); 
        Console.WriteLine($"Output: '{output}'"); 
        Console.WriteLine($"Is USB: {string.Equals(output, "USB", StringComparison.OrdinalIgnoreCase)}"); 
    } 
}
