using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vcrypt.Core.Interfaces;
using Vcrypt.Core.Models;
using Vcrypt.Core.Services;
using Vcrypt.Infrastructure.Hardware;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Collections.Generic;

namespace Vcrypt.UI.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject
    {
        private readonly IUsbMonitorService _usbMonitor;
        private readonly IPartitionManager _partitionManager;
        private IEncryptionProvider? _vault;

        [ObservableProperty]
        private string _currentView = "Waiting";

        [ObservableProperty]
        private string _statusMessage = "Waiting for Drive...";
        
        [ObservableProperty]
        private bool _isProcessing = false;

        [ObservableProperty]
        private string _detectedDriveLetter = "";

        [ObservableProperty]
        private int _progressValue = 0;

        // Setup Form Bindings
        [ObservableProperty]
        private int _setupPublicMB = 100;
        
        [ObservableProperty]
        private string _setupPassword = "";

        [ObservableProperty]
        private string _selectedFileSystem = "exFAT";

        [ObservableProperty]
        private string _selectedAlgorithm = "AES-256 (Native Streaming)";

        public List<string> AvailableFileSystems { get; } = new List<string> { "exFAT", "NTFS" };
        public List<string> AvailableAlgorithms { get; } = new List<string> { "AES-256 (Native Streaming)" };

        // Unlock Form
        [ObservableProperty]
        private string _unlockPassword = "";

        public ObservableCollection<EncryptedFileModel> VaultFiles { get; } = new();

        public MainWindowViewModel()
        {
            _usbMonitor = new WmiUsbMonitorService();
            _partitionManager = new PowerShellPartitionManager();

            _usbMonitor.DriveInserted += UsbMonitor_DriveInserted;
            _usbMonitor.DriveRemoved += UsbMonitor_DriveRemoved;
            _usbMonitor.StartMonitoring();

            Application.Current.Exit += (s, e) =>
            {
                try
                {
                    var process = new System.Diagnostics.Process();
                    process.StartInfo.FileName = "powershell.exe";
                    process.StartInfo.Arguments = "-WindowStyle Hidden -Command \"(Get-Volume -FileSystemLabel 'SecureVault' -ErrorAction SilentlyContinue | Get-Partition | Remove-PartitionAccessPath -AccessPath ((Get-Volume -FileSystemLabel 'SecureVault').DriveLetter + ':\\'))\"";
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.CreateNoWindow = true;
                    process.Start();
                    process.WaitForExit(3000);
                }
                catch { }
            };
        }

        private void UsbMonitor_DriveInserted(object? sender, UsbDriveEventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (IsProcessing) return;

                DetectedDriveLetter = e.DriveLetter;
                if (e.IsVcrypt)
                {
                    StatusMessage = $"Locked Vault Detected ({e.DriveLetter})";
                    CurrentView = "Unlock";
                }
                else
                {
                    StatusMessage = $"New Drive Detected ({e.DriveLetter})";
                    CurrentView = "Setup";
                }
            });
        }

        private void UsbMonitor_DriveRemoved(object? sender, UsbDriveEventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (IsProcessing) return;

                if (e.DriveLetter == DetectedDriveLetter)
                {
                    DetectedDriveLetter = "";
                    VaultFiles.Clear();
                    StatusMessage = "Waiting for Drive...";
                    CurrentView = "Waiting";
                }
            });
        }

        private string ComputeSha256Hash(string rawData)
        {
            using (System.Security.Cryptography.SHA256 sha256Hash = System.Security.Cryptography.SHA256.Create())
            {
                byte[] bytes = sha256Hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(rawData));
                System.Text.StringBuilder builder = new System.Text.StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }

        [RelayCommand]
        private async Task FormatAndLockAsync()
        {
            if (string.IsNullOrEmpty(SetupPassword))
            {
                MessageBox.Show("Please enter a password.");
                return;
            }
            
            if (SetupPublicMB < 250)
            {
                MessageBox.Show("The public area must be at least 250 MB so the Vcrypt application has sufficient space to reside there.", "Constraint Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                SetupPublicMB = 250;
                return;
            }
            
            IsProcessing = true;
            ProgressValue = 0;
            
            var progressText = new Progress<string>(s => StatusMessage = s);
            var progressVal = new Progress<int>(v => ProgressValue = v);
            
            try
            {
                string hash = ComputeSha256Hash(SetupPassword);
                var newLetter = await _partitionManager.FormatAndLockAsync(DetectedDriveLetter, SetupPublicMB, SelectedFileSystem, hash, progressText, progressVal);
                if (!string.IsNullOrEmpty(newLetter))
                {
                    DetectedDriveLetter = newLetter;
                    SetupPassword = "";
                    StatusMessage = "Drive Secured!";
                    CurrentView = "Unlock";
                }
                else
                {
                    MessageBox.Show("Failed to format drive.");
                    StatusMessage = "Initialization Failed.";
                }
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Failed to format drive: {ex.Message}");
                StatusMessage = "Initialization Failed.";
                ProgressValue = 0;
            }
            IsProcessing = false;
        }

        [RelayCommand]
        private void ResetDrive()
        {
            var result = MessageBox.Show("Are you sure you want to completely erase and re-format this drive? All existing data will be permanently lost.", "Reset Drive", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                CurrentView = "Setup";
                StatusMessage = $"New Drive Detected ({DetectedDriveLetter})";
            }
        }

        [RelayCommand]
        private async Task UnlockAsync()
        {
            if (string.IsNullOrEmpty(UnlockPassword)) return;
            
            IsProcessing = true;
            StatusMessage = "Unlocking...";

            try
            {
                var sigPath = System.IO.Path.Combine(DetectedDriveLetter + "\\", ".Vcrypt");
                if (System.IO.File.Exists(sigPath))
                {
                    var storedHash = System.IO.File.ReadAllText(sigPath).Trim();
                    var attemptHash = ComputeSha256Hash(UnlockPassword);

                    if (storedHash == attemptHash)
                    {
                        var drivePath = await _partitionManager.MountVaultAsync();
                        if (drivePath != null)
                        {
                            CurrentView = "Vault";
                            UnlockPassword = "";
                            
                            // Open Windows Explorer to the new drive!
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
                            {
                                FileName = drivePath,
                                UseShellExecute = true
                            });
                        }
                        else
                        {
                            MessageBox.Show("Failed to mount the secure partition. It may be missing.");
                        }
                    }
                    else
                    {
                        MessageBox.Show("Incorrect Password.");
                    }
                }
                else
                {
                    MessageBox.Show("Signature file missing. Is this a Vcrypt drive?");
                }
            }
            catch (System.Exception ex)
            {
                MessageBox.Show("Error unlocking: " + ex.Message);
            }
            
            IsProcessing = false;
        }

        [RelayCommand]
        private async Task LockVaultAsync()
        {
            IsProcessing = true;
            StatusMessage = "Locking Vault...";
            
            await _partitionManager.UnmountVaultAsync();
            
            CurrentView = "Unlock";
            StatusMessage = $"Locked Vault Detected ({DetectedDriveLetter})";
            IsProcessing = false;
        }

        [RelayCommand]
        private void Support()
        {
            try
            {
                System.Diagnostics.Process.Start("explorer", "https://vijay-portfolio-three.vercel.app/");
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Could not open browser: {ex.Message}");
            }
        }
    }
}
