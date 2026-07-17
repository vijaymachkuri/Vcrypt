using System;
using System.Diagnostics;
using System.IO;

namespace Vcrypt.Infrastructure.Hardware
{
    public class DriveMapper
    {
        public void MapDrive(string driveLetter, string url)
        {
            UnmapDrive(driveLetter);

            var uri = new Uri(url);
            var path = uri.AbsolutePath.Trim('/');
            if (string.IsNullOrEmpty(path)) path = "DavWWWRoot";
            var uncPath = $@"\\{uri.Host}@{uri.Port}\{path}";

            // Map in Elevated Session
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "net.exe",
                Arguments = $"use {driveLetter}: {uncPath} /persistent:no",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            process?.WaitForExit();

            // Map in Standard User Session via Explorer VBS injection
            try
            {
                string vbsPath = Path.Combine(Path.GetTempPath(), "VcryptMap.vbs");
                string vbsCode = $@"On Error Resume Next
Set objNetwork = CreateObject(""WScript.Network"")
objNetwork.RemoveNetworkDrive ""{driveLetter}:"", True, True
objNetwork.MapNetworkDrive ""{driveLetter}:"", ""{uncPath}"", False
WScript.Sleep 500
Set oShell = CreateObject(""Shell.Application"")
oShell.NameSpace(""{driveLetter}:\"").Self.Name = ""Vcrypt Vault""";
                File.WriteAllText(vbsPath, vbsCode);
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{vbsPath}\"",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
            }
            catch { }
        }

        public void UnmapDrive(string driveLetter)
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "net.exe",
                Arguments = $"use {driveLetter}: /delete /y",
                CreateNoWindow = true,
                UseShellExecute = false
            });
            process?.WaitForExit();

            try
            {
                string vbsPath = Path.Combine(Path.GetTempPath(), "VcryptUnmap.vbs");
                string vbsCode = $@"On Error Resume Next
Set objNetwork = CreateObject(""WScript.Network"")
objNetwork.RemoveNetworkDrive ""{driveLetter}:"", True, True";
                File.WriteAllText(vbsPath, vbsCode);
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{vbsPath}\"",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
            }
            catch { }
        }

        public void OpenInExplorer(string url)
        {
            try
            {
                var uri = new Uri(url);
                var path = uri.AbsolutePath.Trim('/');
                if (string.IsNullOrEmpty(path)) path = "DavWWWRoot";
                var uncPath = $@"\\{uri.Host}@{uri.Port}\{path}";

                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = uncPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to open explorer: {ex.Message}");
            }
        }
    }
}
