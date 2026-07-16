using Vcrypt.Core.Interfaces;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Vcrypt.Infrastructure.Hardware
{
    public class PowerShellPartitionManager : IPartitionManager
    {
        public PowerShellPartitionManager()
        {
        }
        private async Task RunDiskPartScriptAsync(string scriptContent)
        {
            var tempScriptPath = Path.GetTempFileName();
            File.WriteAllText(tempScriptPath, scriptContent);

            try 
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "diskpart.exe",
                        Arguments = $"/s \"{tempScriptPath}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                await process.WaitForExitAsync();
                
                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                var exitCode = process.ExitCode;
                
                if (exitCode != 0 && !output.Contains("no letter or mount point to remove")) 
                {
                    throw new Exception($"DiskPart failed:\n{output} \n{error}");
                }
            }
            finally 
            {
                if (File.Exists(tempScriptPath)) File.Delete(tempScriptPath);
            }
        }

        public async Task<string?> FormatAndLockAsync(string driveLetter, int publicMB, string fileSystem, string passwordHash, IProgress<string>? progressText = null, IProgress<int>? progressValue = null)
        {
            var formatScript = $@"
$ErrorActionPreference = 'Stop'
$p = Get-Partition -DriveLetter '{driveLetter.TrimEnd(':')}' -ErrorAction SilentlyContinue
if ($null -eq $p) {{ throw 'Could not identify the disk.' }}
$disk = $p.DiskNumber

mountvol /N | Out-Null
Stop-Service -Name ShellHWDetection -Force -ErrorAction SilentlyContinue

try {{
    Get-Partition -DiskNumber $disk -ErrorAction SilentlyContinue | Where-Object DriveLetter | ForEach-Object {{ Remove-PartitionAccessPath -InputObject $_ -AccessPath ($_.DriveLetter + ':\') }}
    Clear-Disk -Number $disk -RemoveData -Confirm:$false
    Initialize-Disk -Number $disk -PartitionStyle MBR -ErrorAction SilentlyContinue
    New-Partition -DiskNumber $disk -Size {publicMB}MB -AssignDriveLetter | Format-Volume -FileSystem {fileSystem} -NewFileSystemLabel 'Public' -Force | Out-Null
    
    New-Partition -DiskNumber $disk -UseMaximumSize -AssignDriveLetter | Format-Volume -FileSystem NTFS -NewFileSystemLabel 'SecureVault' -Force | Out-Null
    Start-Sleep -Seconds 2
    $dl = (Get-Partition -DiskNumber $disk -PartitionNumber 2).DriveLetter
    if ($dl) {{ Remove-PartitionAccessPath -DiskNumber $disk -PartitionNumber 2 -AccessPath ($dl + ':\') }}
    Get-Partition -DiskNumber $disk -PartitionNumber 2 | Set-Partition -MbrType 23 | Out-Null
    Start-Sleep -Seconds 2
}} finally {{
    mountvol /E | Out-Null
    Start-Service -Name ShellHWDetection -ErrorAction SilentlyContinue
}}

$newLetter = (Get-Partition -DiskNumber $disk -PartitionNumber 1).DriveLetter
if (-not $newLetter) {{ throw 'Could not find the drive letter after formatting.' }}

$sigPath = $newLetter + ':\.Vcrypt'
Set-Content -Path $sigPath -Value '{passwordHash}' -Force
Write-Output $newLetter
";
            progressText?.Report($"Formatting Disk (this may take a few moments)...");
            progressValue?.Report(50);
            
            var newLetterFull = await RunPowerShellAsync(formatScript);
            if (string.IsNullOrWhiteSpace(newLetterFull)) throw new Exception("Could not find the drive letter after formatting.");
            
            var lines = newLetterFull.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var actualLetter = lines[lines.Length - 1].Trim();
            if (string.IsNullOrWhiteSpace(actualLetter)) throw new Exception("Could not find the drive letter after formatting.");

            try
            {
                // Copy the portable app to the pendrive so they can use it on any PC!
                var currentExe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(currentExe) && currentExe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    var destExe = Path.Combine(actualLetter + ":\\", "Vcrypt.exe");
                    File.Copy(currentExe, destExe, overwrite: true);
                }
            }
            catch (Exception ex)
            {
                // Ignore copy errors, it's a bonus feature
                System.Diagnostics.Debug.WriteLine($"Failed to copy portable exe: {ex.Message}");
            }

            progressText?.Report("Format Complete!");
            progressValue?.Report(95);
            return actualLetter + ":";
        }

        public async Task<string?> MountVaultAsync(string publicDriveLetter)
        {
            try
            {
                var script = $@"
$p = Get-Partition -DriveLetter '{publicDriveLetter.TrimEnd(':')}' -ErrorAction SilentlyContinue
if (-not $p) {{ throw 'Disk not found.' }}
$diskNumber = $p.DiskNumber

$partition = Get-Partition -DiskNumber $diskNumber | Where-Object PartitionNumber -eq 2
if (-not $partition) {{ Write-Output ''; exit }}

# Unhide the partition (Change type to 0x07 NTFS)
Set-Partition -InputObject $partition -MbrType 7 | Out-Null
Start-Sleep -Seconds 2

$mountPath = 'C:\ProgramData\Vcrypt\Mounts\' + $diskNumber
if (-not (Test-Path $mountPath)) {{
    New-Item -ItemType Directory -Force -Path $mountPath | Out-Null
}}

Add-PartitionAccessPath -DiskNumber $diskNumber -PartitionNumber 2 -AccessPath $mountPath
Write-Output $mountPath
";
                var drivePath = await RunPowerShellAsync(script);
                if (string.IsNullOrWhiteSpace(drivePath)) return null;
                var lines = drivePath.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                return lines[lines.Length - 1].Trim();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex.Message);
                return null;
            }
        }

        public async Task<bool> UnmountVaultAsync(string publicDriveLetter)
        {
            try
            {
                var script = $@"
$p = Get-Partition -DriveLetter '{publicDriveLetter.TrimEnd(':')}' -ErrorAction SilentlyContinue
if (-not $p) {{ throw 'Disk not found.' }}
$diskNumber = $p.DiskNumber

$partition = Get-Partition -DiskNumber $diskNumber | Where-Object PartitionNumber -eq 2
if ($partition) {{
    $mountPath = 'C:\ProgramData\Vcrypt\Mounts\' + $diskNumber
    if (Test-Path $mountPath) {{
        Remove-PartitionAccessPath -DiskNumber $diskNumber -PartitionNumber 2 -AccessPath $mountPath -ErrorAction SilentlyContinue
    }}
    Set-Partition -InputObject $partition -MbrType 23 | Out-Null
}}
Write-Output 'OK'
";
                var result = await RunPowerShellAsync(script);
                if (string.IsNullOrWhiteSpace(result)) return false;
                var lines = result.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                return lines[lines.Length - 1].Trim() == "OK";
            }
            catch
            {
                return false;
            }
        }

        public async Task<VaultCapacityInfo?> GetVaultCapacityAsync(string publicDriveLetter)
        {
            try
            {
                var script = $@"
$p = Get-Partition -DriveLetter '{publicDriveLetter.TrimEnd(':')}' -ErrorAction SilentlyContinue
if (-not $p) {{ exit }}
$diskNumber = $p.DiskNumber
$mountPath = 'C:\ProgramData\Vcrypt\Mounts\' + $diskNumber
$vol = $null
for ($i=0; $i -lt 10; $i++) {{
    $vol = Get-Partition -DiskNumber $diskNumber -PartitionNumber 2 | Get-Volume -ErrorAction SilentlyContinue
    if ($vol) {{ break }}
    Start-Sleep -Milliseconds 500
}}
if ($vol) {{
    Write-Output $vol.Size
    Write-Output $vol.SizeRemaining
}}
";
                var output = await RunPowerShellAsync(script);
                var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length >= 2 && long.TryParse(lines[0], out long total) && long.TryParse(lines[1], out long remaining))
                {
                    return new VaultCapacityInfo { TotalBytes = total, FreeBytes = remaining };
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        private async Task<string> RunPowerShellAsync(string command, bool requireAdmin = true)
        {
            var outputFile = Path.GetTempFileName();
            var errorFile = Path.GetTempFileName();
            
            var commandWithNoProgress = $"$ProgressPreference = 'SilentlyContinue'; {command}";
            // Wrapper script to run the command and redirect output to files
            var wrapperScript = $@"
try {{
    & {{
        {commandWithNoProgress}
    }} | Out-File -FilePath '{outputFile}' -Encoding UTF8
}} catch {{
    $_.Exception.Message | Out-File -FilePath '{errorFile}' -Encoding UTF8
}}
";
            var bytes = System.Text.Encoding.Unicode.GetBytes(wrapperScript);
            var encoded = Convert.ToBase64String(bytes);

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
                    UseShellExecute = false,
                    Verb = "",
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                }
            };

            process.Start();
            await process.WaitForExitAsync();
            
            string output = "";
            string error = "";
            
            if (File.Exists(outputFile))
            {
                output = await File.ReadAllTextAsync(outputFile);
                File.Delete(outputFile);
            }
            if (File.Exists(errorFile))
            {
                error = await File.ReadAllTextAsync(errorFile);
                File.Delete(errorFile);
            }
            
            if (!string.IsNullOrWhiteSpace(error)) 
            {
                throw new Exception($"PowerShell Error: {error}");
            }
            
            return output;
        }
    }
}
