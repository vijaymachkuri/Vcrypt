using Vcrypt.Core.Interfaces;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Vcrypt.Infrastructure.Hardware
{
    public class PowerShellBitLockerManager : IBitLockerManager
    {
        public async Task<bool> EnableBitLockerAsync(string driveLetter, string password, IProgress<string>? progressText = null, IProgress<int>? progressValue = null)
        {
            progressText?.Report("Securing partition with BitLocker...");
            progressValue?.Report(75);

            var script = $@"
$pwd = ConvertTo-SecureString '{password.Replace("'", "''")}' -AsPlainText -Force
Enable-BitLocker -MountPoint '{driveLetter.TrimEnd(':')}:' -EncryptionMethod XtsAes256 -PasswordProtector -Password $pwd -UsedSpaceOnly -SkipHardwareTest
";
            try
            {
                var result = await RunPowerShellAsync(script);
                
                // Wait for encryption to complete
                progressText?.Report("Waiting for encryption to complete...");
                progressValue?.Report(80);
                
                var waitScript = $@"
$timeout = 30
$count = 0
while ((Get-BitLockerVolume -MountPoint '{driveLetter.TrimEnd(':')}:').VolumeStatus -ne 'FullyEncrypted') {{
    Start-Sleep -Seconds 2
    $count += 2
    if ($count -ge $timeout) {{
        Write-Error 'BitLocker encryption timed out. Your Windows edition (e.g. Windows Home) may not support creating BitLocker drives.'
        exit 1
    }}
}}
Write-Output 'Done'
";
                var waitResult = await RunPowerShellAsync(waitScript);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"BitLocker Enable Error: {ex.Message}");
                throw new Exception("BitLocker failed to initialize. Your Windows edition likely does not support creating encrypted drives: " + ex.Message);
            }
        }

        public async Task<bool> UnlockBitLockerAsync(string driveLetter, string password)
        {
            var script = $@"
$pwd = ConvertTo-SecureString '{password.Replace("'", "''")}' -AsPlainText -Force
Unlock-BitLocker -MountPoint '{driveLetter.TrimEnd(':')}:' -Password $pwd
";
            try
            {
                var result = await RunPowerShellAsync(script);
                return !result.Contains("failed") && !result.Contains("Error");
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> LockBitLockerAsync(string driveLetter)
        {
            var script = $@"
Lock-BitLocker -MountPoint '{driveLetter.TrimEnd(':')}:' -ForceDismount
";
            try
            {
                var result = await RunPowerShellAsync(script);
                return !result.Contains("failed") && !result.Contains("Error");
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> IsBitLockerEnabledAsync(string driveLetter)
        {
            var script = $@"
$vol = Get-BitLockerVolume -MountPoint '{driveLetter.TrimEnd(':')}:' -ErrorAction SilentlyContinue
if ($vol -and $vol.VolumeStatus -ne 'Decrypted') {{ Write-Output 'Yes' }} else {{ Write-Output 'No' }}
";
            try
            {
                var result = await RunPowerShellAsync(script);
                return result?.Trim() == "Yes";
            }
            catch
            {
                return false;
            }
        }

        private async Task<string> RunPowerShellAsync(string command)
        {
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
            
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            
            await process.WaitForExitAsync();
            
            var output = await outputTask;
            var error = await errorTask;
            
            if (!string.IsNullOrWhiteSpace(error) && !error.Contains("Get-Win32EncryptableVolumeInternal")) 
            {
                throw new Exception($"PowerShell Error: {error}");
            }
            
            return output;
        }
    }
}
