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
            
            if (process?.ExitCode != 0)
            {
                string error = process?.StandardError.ReadToEnd();
                Debug.WriteLine($"Failed to map drive: {error}");
            }
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
        }

        public void OpenInExplorer(string driveLetter)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"{driveLetter}:\\",
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
