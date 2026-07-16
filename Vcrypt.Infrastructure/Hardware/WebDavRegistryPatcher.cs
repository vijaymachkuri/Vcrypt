using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace Vcrypt.Infrastructure.Hardware
{
    public static class WebDavRegistryPatcher
    {
        public static void EnsureMaxFileSizeLimit()
        {
            try
            {
                const string keyName = @"SYSTEM\CurrentControlSet\Services\WebClient\Parameters";
                const string valueName = "FileSizeLimitInBytes";
                const uint maxLimit = 0xFFFFFFFF; // 4 GB (Max allowed by WebClient)

                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(keyName, false))
                {
                    if (key != null)
                    {
                        object currentValue = key.GetValue(valueName);
                        if (currentValue == null || Convert.ToUInt32(currentValue) != maxLimit)
                        {
                            // We need to write to HKLM and restart service, must be elevated
                            var script = $@"
Set-ItemProperty -Path 'HKLM:\{keyName}' -Name '{valueName}' -Value {maxLimit} -Type DWord -ErrorAction SilentlyContinue
Stop-Service WebClient -Force -ErrorAction SilentlyContinue
Start-Service WebClient -ErrorAction SilentlyContinue
";
                            RunElevated(script);
                        }
                        else
                        {
                            // Registry is already correct, just ensure service is running
                            EnsureWebClientStarted();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to read/patch WebDAV registry limit: {ex.Message}");
            }
        }

        private static void EnsureWebClientStarted()
        {
            var script = "Start-Service WebClient -ErrorAction SilentlyContinue";
            RunElevated(script);
        }

        private static void RunElevated(string script)
        {
            try
            {
                var bytes = System.Text.Encoding.Unicode.GetBytes(script);
                var encoded = Convert.ToBase64String(bytes);

                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
                    UseShellExecute = true,
                    Verb = "runAs",
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
                process?.WaitForExit();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to run elevated script: {ex.Message}");
            }
        }
    }
}
