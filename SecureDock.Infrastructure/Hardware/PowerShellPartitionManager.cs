using SecureDock.Core.Interfaces;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace SecureDock.Infrastructure.Hardware
{
    public class PowerShellPartitionManager : IPartitionManager
    {
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
            var diskScript = $@"
$p = Get-Partition -DriveLetter '{driveLetter.TrimEnd(':')}' -ErrorAction SilentlyContinue
if ($null -ne $p) {{
    Write-Output $p.DiskNumber
}} else {{
    $usb = Get-Disk | Where-Object BusType -eq 'USB'
    if ($usb -is [array]) {{
        if ($usb.Count -gt 1) {{ throw 'Multiple USB drives detected. Please unplug all other USB drives before formatting to prevent data loss.' }}
        Write-Output $usb[0].Number
    }} else {{
        if ($null -ne $usb) {{ Write-Output $usb.Number }} else {{ throw 'No USB drives detected to format.' }}
    }}
}}";
            var disk = await RunPowerShellAsync(diskScript);
            if (string.IsNullOrWhiteSpace(disk)) throw new Exception("Could not identify physical disk.");

            progressText?.Report($"Identified Disk {disk}");
            progressValue?.Report(10);
            
            // Disable Windows Automount and stop the Shell Hardware Detection service
            // This is the ULTIMATE fix to physically prevent Windows Explorer from showing
            // the "You need to format the disk" popup while we are wiping and formatting!
            await RunPowerShellAsync("mountvol /N; Stop-Service -Name ShellHWDetection -Force -ErrorAction SilentlyContinue");
            try
            {
                progressText?.Report("Cleaning and Initializing Disk...");
                progressValue?.Report(20);
            
            // Strip all drive letters first to prevent Windows from complaining when we wipe the disk!
            await RunPowerShellAsync($"Get-Partition -DiskNumber {disk.Trim()} -ErrorAction SilentlyContinue | Where-Object DriveLetter | ForEach-Object {{ Remove-PartitionAccessPath -InputObject $_ -AccessPath ($_.DriveLetter + ':\\') }}");
            
            await RunPowerShellAsync($"Clear-Disk -Number {disk.Trim()} -RemoveData -Confirm:$false");
            await RunPowerShellAsync($"Initialize-Disk -Number {disk.Trim()} -PartitionStyle MBR -ErrorAction SilentlyContinue");

            progressText?.Report($"Creating Public {fileSystem} Partition...");
            progressValue?.Report(40);
            await RunPowerShellAsync($"New-Partition -DiskNumber {disk.Trim()} -Size {publicMB}MB -AssignDriveLetter | Format-Volume -FileSystem {fileSystem} -NewFileSystemLabel 'Public' -Force");

            progressText?.Report("Creating SecureVault Partition...");
            progressValue?.Report(60);
            await RunPowerShellAsync($"New-Partition -DiskNumber {disk.Trim()} -UseMaximumSize -AssignDriveLetter | Format-Volume -FileSystem NTFS -NewFileSystemLabel 'SecureVault' -Force; Start-Sleep -Seconds 2; $dl = (Get-Partition -DiskNumber {disk.Trim()} -PartitionNumber 2).DriveLetter; if ($dl) {{ Remove-PartitionAccessPath -DiskNumber {disk.Trim()} -PartitionNumber 2 -AccessPath ($dl + ':\\') }}; Get-Partition -DiskNumber {disk.Trim()} -PartitionNumber 2 | Set-Partition -MbrType 23");
            
                progressText?.Report("Finalizing Mounts...");
                progressValue?.Report(85);
                
                await Task.Delay(2000); // Give Windows time to settle
            }
            finally
            {
                // ALWAYS re-enable Automount and restart the Shell Hardware Detection service!
                await RunPowerShellAsync("mountvol /E; Start-Service -Name ShellHWDetection -ErrorAction SilentlyContinue");
            }
            
            
            var getLetterScript = "(Get-Volume -FileSystemLabel 'Public' -ErrorAction SilentlyContinue | Select-Object -First 1).DriveLetter";
            var newLetter = await RunPowerShellAsync(getLetterScript);
            
            if (string.IsNullOrWhiteSpace(newLetter)) throw new Exception("Could not find the drive letter after formatting.");
            
            var sigPath = Path.Combine(newLetter.Trim() + ":\\", ".securedock");
            File.WriteAllText(sigPath, passwordHash);

            try
            {
                // Copy the portable app to the pendrive so they can use it on any PC!
                var currentExe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(currentExe) && currentExe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    var destExe = Path.Combine(newLetter.Trim() + ":\\", "SecureDock.exe");
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
            return newLetter.Trim() + ":";
        }

        public async Task<string?> MountVaultAsync()
        {
            try
            {
                var script = @"
$disk = Get-Disk | Where-Object BusType -eq 'USB' | Select-Object -First 1
if (-not $disk) { Write-Output ''; exit }

$partition = Get-Partition -DiskNumber $disk.Number | Where-Object PartitionNumber -eq 2
if (-not $partition) { Write-Output ''; exit }

# Unhide the partition (Change type to 0x07 NTFS)
Set-Partition -InputObject $partition -MbrType 7 | Out-Null
Start-Sleep -Seconds 2

$volume = Get-Volume -FileSystemLabel 'SecureVault' -ErrorAction SilentlyContinue
if (-not $volume) { Write-Output ''; exit }

if ($volume.DriveLetter) {
    Write-Output ($volume.DriveLetter + ':\')
    exit
}

# Find an available drive letter starting from Z down to M
$usedLetters = (Get-Volume).DriveLetter
$availableLetters = 90..77 | ForEach-Object { [char]$_ } | Where-Object { $usedLetters -notcontains $_ }
if ($availableLetters.Count -eq 0) { Write-Output ''; exit }

$newLetter = $availableLetters[0]
$volume | Get-Partition | Set-Partition -NewDriveLetter $newLetter | Out-Null
Write-Output ($newLetter + ':\')
";
                var drivePath = await RunPowerShellAsync(script);
                drivePath = drivePath?.Trim();
                
                if (string.IsNullOrEmpty(drivePath)) return null;
                return drivePath;
            }
            catch
            {
                return null;
            }
        }

        public async Task<bool> UnmountVaultAsync()
        {
            try
            {
                var script = @"
$volume = Get-Volume -FileSystemLabel 'SecureVault' -ErrorAction SilentlyContinue
if ($volume) {
    $partition = $volume | Get-Partition
    if ($volume.DriveLetter) {
        Remove-PartitionAccessPath -InputObject $partition -AccessPath ($volume.DriveLetter + ':\')
    }
    Set-Partition -InputObject $partition -MbrType 23 | Out-Null
} else {
    $disk = Get-Disk | Where-Object BusType -eq 'USB' | Select-Object -First 1
    if ($disk) {
        $partition = Get-Partition -DiskNumber $disk.Number | Where-Object PartitionNumber -eq 2
        if ($partition -and $partition.MbrType -ne 23) {
            Set-Partition -InputObject $partition -MbrType 23 | Out-Null
        }
    }
}
Write-Output 'OK'
";
                var result = await RunPowerShellAsync(script);
                return result?.Trim() == "OK";
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
            
            if (!string.IsNullOrWhiteSpace(error)) 
            {
                throw new Exception($"PowerShell Error: {error}");
            }
            
            return output;
        }
    }
}
