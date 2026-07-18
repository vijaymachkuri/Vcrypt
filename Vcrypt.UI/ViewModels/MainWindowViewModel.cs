using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vcrypt.Core.Interfaces;
using Vcrypt.Core.Models;
using Vcrypt.Core.Services;
using Vcrypt.Core.WebDav;
using Vcrypt.Infrastructure.Hardware;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Vcrypt.UI.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject
    {
        private readonly IUsbMonitorService _usbMonitor;
        private readonly IPartitionManager _partitionManager;
        private IEncryptionProvider? _vault;
        private readonly WebDavServerManager _webDavManager;
        private readonly DriveMapper _driveMapper;
        private const string MOUNT_DRIVE = "Z";
        private DispatcherTimer _capacityTimer;
        private string _mountPath = "";

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetDiskFreeSpaceEx(string lpDirectoryName,
            out ulong lpFreeBytesAvailable,
            out ulong lpTotalNumberOfBytes,
            out ulong lpTotalNumberOfFreeBytes);

        [ObservableProperty]
        private string _currentView = "Waiting";

        [ObservableProperty]
        private string _statusMessage = "Waiting for Drive...";
        
        [ObservableProperty]
        private bool _isProcessing = false;

        [ObservableProperty]
        private bool _isFileTransferActive = false;

        [ObservableProperty]
        private string _detectedDriveLetter = "";

        [ObservableProperty]
        private int _progressValue = 0;

        [ObservableProperty]
        private string _currentFileName = "";

        [ObservableProperty]
        private string _timeRemainingString = "";

        [ObservableProperty]
        private string _itemsRemainingString = "";

        [ObservableProperty]
        private string _speedString = "";

        // Setup Form Bindings
        [ObservableProperty]
        private int _setupPublicMB = 250;
        
        [ObservableProperty]
        private string _setupPassword = "";

        [ObservableProperty]
        private string _selectedFileSystem = "exFAT";

        [ObservableProperty]
        private string _selectedAlgorithm = "AES-256 (Native Streaming)";

        [ObservableProperty]
        private bool _isFormatAndHide = true;
        
        public string SetupButtonText => IsFormatAndHide ? "Encrypt & Format Drive" : "Encrypt Existing Drive (No Format)";

        partial void OnIsFormatAndHideChanged(bool value)
        {
            OnPropertyChanged(nameof(SetupButtonText));
        }

        public List<string> AvailableFileSystems { get; } = new List<string> { "exFAT", "NTFS" };
        public List<string> AvailableAlgorithms { get; } = new List<string> { "AES-256 (Native Streaming)" };

        // Unlock Form
        [ObservableProperty]
        private string _unlockPassword = "";

        // File Explorer Bindings
        [ObservableProperty]
        private string _currentPath = "";

        [ObservableProperty]
        private string _setupLogs = "";

        [ObservableProperty]
        private bool _isVaultCapacityVisible = false;

        [ObservableProperty]
        private string _vaultCapacityString = "";

        [ObservableProperty]
        private string _vaultFreeString = "";

        [ObservableProperty]
        private double _vaultUsedPercentage = 0;

        [ObservableProperty]
        private double _imagesShare = 0;

        [ObservableProperty]
        private double _videosShare = 0;

        [ObservableProperty]
        private double _audioShare = 0;

        [ObservableProperty]
        private double _documentsShare = 0;

        [ObservableProperty]
        private double _appsShare = 0;

        [ObservableProperty]
        private double _otherShare = 0;

        [ObservableProperty]
        private double _freeShare = 1;

        [ObservableProperty]
        private string _imagesSizeString = "";

        [ObservableProperty]
        private string _videosSizeString = "";

        [ObservableProperty]
        private string _audioSizeString = "";

        [ObservableProperty]
        private string _documentsSizeString = "";

        [ObservableProperty]
        private string _appsSizeString = "";

        [ObservableProperty]
        private string _otherSizeString = "";

        public ObservableCollection<EncryptedItemModel> VaultFiles { get; } = new();

        public MainWindowViewModel()
        {
            _usbMonitor = new WmiUsbMonitorService();
            _partitionManager = new PowerShellPartitionManager();
            _webDavManager = new WebDavServerManager();
            _driveMapper = new DriveMapper();

            _usbMonitor.DriveInserted += UsbMonitor_DriveInserted;
            _usbMonitor.DriveRemoved += UsbMonitor_DriveRemoved;
            _usbMonitor.StartMonitoring();

            _capacityTimer = new DispatcherTimer();
            _capacityTimer.Interval = System.TimeSpan.FromSeconds(2);
            _capacityTimer.Tick += CapacityTimer_Tick;
        }

        private void CapacityTimer_Tick(object? sender, System.EventArgs e)
        {
            if (string.IsNullOrEmpty(_mountPath)) return;
            
            string pathWithSlash = _mountPath;
            if (!pathWithSlash.EndsWith("\\")) pathWithSlash += "\\";

            if (GetDiskFreeSpaceEx(pathWithSlash, out ulong freeAvail, out ulong totalBytes, out ulong totalFree))
            {
                IsVaultCapacityVisible = true;
                long total = (long)totalBytes;
                long free = (long)totalFree;
                long physicalUsed = total - free;
                
                VaultUsedPercentage = total > 0 ? (double)physicalUsed / total * 100 : 0;
                VaultCapacityString = FormatBytes(total);
                VaultFreeString = $"{FormatBytes(free)} ({(free / 1024 / 1024):N0} MB)";

                // Calculate logical file sizes
                long images = 0, videos = 0, audio = 0, docs = 0, apps = 0, other = 0;
                if (_vault?.CurrentIndex?.Root != null)
                {
                    CalculateCategorySizes(_vault.CurrentIndex.Root, ref images, ref videos, ref audio, ref docs, ref apps, ref other);
                }

                // The sum of logical files will be slightly less than physical used (due to padding/filesystem overhead).
                // We add the unaccounted physical overhead to "Other" so the bar fills up exactly to the physical used space.
                long logicalTotal = images + videos + audio + docs + apps + other;
                long overhead = System.Math.Max(0, physicalUsed - logicalTotal);
                other += overhead;

                ImagesShare = total > 0 ? (double)images / total : 0;
                VideosShare = total > 0 ? (double)videos / total : 0;
                AudioShare = total > 0 ? (double)audio / total : 0;
                DocumentsShare = total > 0 ? (double)docs / total : 0;
                AppsShare = total > 0 ? (double)apps / total : 0;
                OtherShare = total > 0 ? (double)other / total : 0;
                FreeShare = total > 0 ? (double)free / total : 1;

                ImagesSizeString = images > 0 ? FormatBytes(images) : "";
                VideosSizeString = videos > 0 ? FormatBytes(videos) : "";
                AudioSizeString = audio > 0 ? FormatBytes(audio) : "";
                DocumentsSizeString = docs > 0 ? FormatBytes(docs) : "";
                AppsSizeString = apps > 0 ? FormatBytes(apps) : "";
                OtherSizeString = other > 0 ? FormatBytes(other) : "";
            }
        }

        private void CalculateCategorySizes(EncryptedItemModel current, ref long images, ref long videos, ref long audio, ref long docs, ref long apps, ref long other)
        {
            if (current == null) return;

            if (!current.IsFolder)
            {
                string ext = System.IO.Path.GetExtension(current.Name).ToLowerInvariant();
                switch (ext)
                {
                    case ".jpg": case ".jpeg": case ".png": case ".gif": case ".bmp": case ".webp": case ".heic": case ".svg":
                        images += current.Size;
                        break;
                    case ".mp4": case ".mkv": case ".avi": case ".mov": case ".wmv": case ".flv": case ".webm":
                        videos += current.Size;
                        break;
                    case ".mp3": case ".wav": case ".flac": case ".aac": case ".ogg": case ".m4a":
                        audio += current.Size;
                        break;
                    case ".pdf": case ".doc": case ".docx": case ".txt": case ".xlsx": case ".xls": case ".pptx": case ".ppt": case ".md": case ".csv":
                        docs += current.Size;
                        break;
                    case ".exe": case ".msi": case ".dll": case ".apk": case ".appx": case ".iso":
                        apps += current.Size;
                        break;
                    default:
                        other += current.Size;
                        break;
                }
            }
            else
            {
                foreach (var child in current.Children)
                {
                    CalculateCategorySizes(child, ref images, ref videos, ref audio, ref docs, ref apps, ref other);
                }
            }
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
                    _capacityTimer.Stop();
                    _mountPath = "";
                    _driveMapper.UnmapDrive(MOUNT_DRIVE);
                    _webDavManager.Stop();
                    DetectedDriveLetter = "";
                    VaultFiles.Clear();
                    _vault = null;
                    CurrentPath = "";
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
            
            if (IsFormatAndHide && SetupPublicMB < 250)
            {
                MessageBox.Show("The public area must be at least 250 MB.", "Constraint Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                SetupPublicMB = 250;
                return;
            }
            
            IsProcessing = true;
            IsFileTransferActive = false;
            ProgressValue = 0;
            SetupLogs = "Initializing Setup...\n";
            
            var progressText = new System.Progress<string>(s => 
            {
                StatusMessage = s;
                SetupLogs += s + "\n";
            });
            var progressVal = new System.Progress<int>(v => ProgressValue = v);
            
            try
            {
                string hash = ComputeSha256Hash(SetupPassword);
                
                if (IsFormatAndHide)
                {
                    var newLetter = await _partitionManager.FormatAndLockAsync(DetectedDriveLetter, SetupPublicMB, SelectedFileSystem, hash, progressText, progressVal);
                    if (!string.IsNullOrEmpty(newLetter))
                    {
                        ((System.IProgress<int>)progressVal).Report(100);
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
                else
                {
                    // No Format Mode!
                    ((System.IProgress<string>)progressText).Report("Setting up Secure Vault without formatting...");
                    ((System.IProgress<int>)progressVal).Report(0);
                    
                    // Create signature file
                    var driveRoot = DetectedDriveLetter + "\\";
                    var sigPath = System.IO.Path.Combine(driveRoot, ".Vcrypt");
                    if (System.IO.File.Exists(sigPath))
                    {
                        System.IO.File.SetAttributes(sigPath, System.IO.FileAttributes.Normal);
                    }
                    System.IO.File.WriteAllText(sigPath, hash);
                    System.IO.File.SetAttributes(sigPath, System.IO.FileAttributes.Hidden | System.IO.FileAttributes.System);
                    
                    // Create hidden vault directory
                    var vaultDir = System.IO.Path.Combine(driveRoot, ".VcryptVault");
                    if (!System.IO.Directory.Exists(vaultDir))
                    {
                        var di = System.IO.Directory.CreateDirectory(vaultDir);
                        di.Attributes = System.IO.FileAttributes.Directory | System.IO.FileAttributes.Hidden | System.IO.FileAttributes.System;
                    }
                    
                    // Try to copy portable exe
                    try
                    {
                        var currentExe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                        if (!string.IsNullOrEmpty(currentExe) && currentExe.EndsWith(".exe", System.StringComparison.OrdinalIgnoreCase))
                        {
                            var destExe = System.IO.Path.Combine(driveRoot, "Vcrypt.exe");
                            System.IO.File.Copy(currentExe, destExe, overwrite: true);
                        }
                    }
                    catch { }

                    // Migrate existing files
                    ((System.IProgress<string>)progressText).Report("Preparing to secure existing files...");
                    
                    var tempVault = new Vcrypt.Core.Services.AesEncryptionProvider();
                    tempVault.Initialize(vaultDir);
                    await tempVault.InitializeNewVaultAsync(SetupPassword);
                    
                    try
                    {
                        var filesToMigrate = new System.Collections.Generic.List<string>();
                        var dirsToMigrate = new System.Collections.Generic.List<string>();
                        
                        void ScanDirectory(string path, string relativePath)
                        {
                            var di = new System.IO.DirectoryInfo(path);
                            foreach (var dir in di.GetDirectories())
                            {
                                if (dir.Name.Equals(".VcryptVault", System.StringComparison.OrdinalIgnoreCase) ||
                                    dir.Name.Equals("System Volume Information", System.StringComparison.OrdinalIgnoreCase) ||
                                    dir.Name.Equals("$RECYCLE.BIN", System.StringComparison.OrdinalIgnoreCase))
                                {
                                    continue;
                                }
                                dirsToMigrate.Add(dir.FullName);
                                ScanDirectory(dir.FullName, string.IsNullOrEmpty(relativePath) ? dir.Name : relativePath + "/" + dir.Name);
                            }
                            
                            foreach (var file in di.GetFiles())
                            {
                                if (file.Name.Equals(".Vcrypt", System.StringComparison.OrdinalIgnoreCase) ||
                                    file.Name.Equals("Vcrypt.exe", System.StringComparison.OrdinalIgnoreCase))
                                {
                                    continue;
                                }
                                filesToMigrate.Add(file.FullName);
                            }
                        }
                        
                        ScanDirectory(driveRoot, "");

                        long totalFiles = filesToMigrate.Count;
                        long currentFile = 0;
                        
                        foreach (var file in filesToMigrate)
                        {
                            currentFile++;
                            ((System.IProgress<string>)progressText).Report($"Securing {currentFile}/{totalFiles}: {System.IO.Path.GetFileName(file)}");
                            if (totalFiles > 0)
                            {
                                ((System.IProgress<int>)progressVal).Report((int)((currentFile * 100) / totalFiles));
                            }
                            
                            // Calculate vault target folder based on relative path
                            string relPath = file.Substring(driveRoot.Length).TrimStart('\\', '/');
                            string targetFolder = "";
                            if (relPath.Contains("\\") || relPath.Contains("/"))
                            {
                                targetFolder = System.IO.Path.GetDirectoryName(relPath)?.Replace("\\", "/") ?? "";
                                if (!string.IsNullOrEmpty(targetFolder))
                                {
                                    await tempVault.CreateFolderAsync(System.IO.Path.GetFileName(targetFolder), System.IO.Path.GetDirectoryName(targetFolder)?.Replace("\\", "/") ?? "");
                                }
                            }
                            
                            // In a real implementation we should create all intermediate folders properly,
                            // but for simplicity let's just make sure the file is encrypted with the correct target Parent Path.
                            // To properly handle deep folders, we can split targetFolder and create each part:
                            if (!string.IsNullOrEmpty(targetFolder))
                            {
                                string currentPath = "";
                                foreach (var part in targetFolder.Split(new[] { '/' }, System.StringSplitOptions.RemoveEmptyEntries))
                                {
                                    await tempVault.CreateFolderAsync(part, currentPath);
                                    currentPath = string.IsNullOrEmpty(currentPath) ? part : currentPath + "/" + part;
                                }
                            }

                            await tempVault.EncryptFileAsync(file, targetFolder);
                            System.IO.File.Delete(file);
                        }
                        
                        // Clean up empty directories
                        // Reverse sort so we delete deepest directories first
                        dirsToMigrate.Sort((a, b) => b.Length.CompareTo(a.Length));
                        foreach (var dir in dirsToMigrate)
                        {
                            try
                            {
                                if (System.IO.Directory.GetFileSystemEntries(dir).Length == 0)
                                {
                                    System.IO.Directory.Delete(dir);
                                }
                            }
                            catch { }
                        }
                    }
                    finally
                    {
                        tempVault.Lock();
                    }
                    
                    ((System.IProgress<string>)progressText).Report("Setup Complete!");
                    ((System.IProgress<int>)progressVal).Report(100);
                    
                    SetupPassword = "";
                    StatusMessage = "Drive Secured!";
                    CurrentView = "Unlock";
                }
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Failed to setup drive: {ex.Message}");
                StatusMessage = "Initialization Failed.";
            }
            finally
            {
                IsProcessing = false;
                ProgressValue = 0;
            }
        }

        [RelayCommand]
        private async Task ResetDriveAsync()
        {
            var result = MessageBox.Show("Are you sure you want to completely erase and re-format this drive?", "Reset Drive", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                // We MUST lock the vault first to stop the WebDAV server and unmap Z:,
                // otherwise the Z: drive is left dangling and throws network errors!
                await LockVaultAsync();
                
                CurrentView = "Setup";
                StatusMessage = $"New Drive Detected ({DetectedDriveLetter})";
            }
        }

        [RelayCommand]
        private async Task UnlockAsync()
        {
            if (string.IsNullOrEmpty(UnlockPassword)) return;
            
            IsProcessing = true;
            IsFileTransferActive = false;
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
                        var mountPath = await _partitionManager.MountVaultAsync(DetectedDriveLetter);
                        bool isFolderVault = false;
                        
                        if (string.IsNullOrEmpty(mountPath))
                        {
                            isFolderVault = true;
                            mountPath = System.IO.Path.Combine(DetectedDriveLetter + "\\", ".VcryptVault");
                            if (!System.IO.Directory.Exists(mountPath))
                            {
                                var di = System.IO.Directory.CreateDirectory(mountPath);
                                di.Attributes = System.IO.FileAttributes.Directory | System.IO.FileAttributes.Hidden | System.IO.FileAttributes.System;
                            }
                        }

                        if (!string.IsNullOrEmpty(mountPath))
                        {
                            _vault = new AesEncryptionProvider();
                            _vault.Initialize(mountPath);
                            
                            if (!await _vault.UnlockAsync(UnlockPassword))
                            {
                                await _vault.InitializeNewVaultAsync(UnlockPassword);
                            }

                            // Patch registry for big files
                            WebDavRegistryPatcher.EnsureMaxFileSizeLimit();

                            // Start WebDAV Server and Map Drive
                            _webDavManager.Start((AesEncryptionProvider)_vault);
                            _driveMapper.MapDrive(MOUNT_DRIVE, _webDavManager.BaseUrl);

                            string pathWithSlash = mountPath;
                            if (!pathWithSlash.EndsWith("\\")) pathWithSlash += "\\";

                            if (GetDiskFreeSpaceEx(pathWithSlash, out ulong freeAvail, out ulong totalBytes, out ulong totalFree))
                            {
                                long used = (long)totalBytes - (long)totalFree;
                                VaultUsedPercentage = totalBytes > 0 ? (double)used / totalBytes * 100 : 0;
                                VaultCapacityString = FormatBytes((long)totalBytes);
                                VaultFreeString = FormatBytes((long)totalFree);
                                IsVaultCapacityVisible = true;
                                
                                _mountPath = mountPath;
                                _capacityTimer.Start();
                            }
                            else
                            {
                                IsVaultCapacityVisible = false;
                            }

                            CurrentPath = "";
                            StatusMessage = "Vault mounted as Z: drive";
                            CurrentView = "Vault";
                            UnlockPassword = "";
                        }
                        else
                        {
                            MessageBox.Show("Failed to mount the secure partition.");
                        }
                    }
                    else
                    {
                        MessageBox.Show("Incorrect Password.");
                    }
                }
                else
                {
                    string files = "";
                    try { files = string.Join(", ", System.IO.Directory.GetFiles(DetectedDriveLetter + "\\")); } catch { files = "Error reading dir"; }
                    MessageBox.Show($"Signature file missing.\nPath checked: {sigPath}\nFiles found: {files}\nIs this a Vcrypt drive?", "Missing Signature");
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
            IsFileTransferActive = false;
            StatusMessage = "Locking Vault...";
            
            _capacityTimer.Stop();
            _mountPath = "";
            _driveMapper.UnmapDrive(MOUNT_DRIVE);
            _webDavManager.Stop();
            _vault?.Lock();
            VaultFiles.Clear();
            _vault = null;

            await _partitionManager.UnmountVaultAsync(DetectedDriveLetter);
            
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
            catch { }
        }

        private string FormatBytes(long bytes)
        {
            string[] suf = { "B", "KB", "MB", "GB", "TB", "PB", "EB" }; 
            if (bytes == 0) return "0 B";
            long bytesCopy = System.Math.Abs(bytes);
            int place = System.Convert.ToInt32(System.Math.Floor(System.Math.Log(bytesCopy, 1024)));
            double num = System.Math.Round(bytesCopy / System.Math.Pow(1024, place), 1);
            return (System.Math.Sign(bytes) * num).ToString() + " " + suf[place];
        }

        // --- File Explorer Commands ---

        [RelayCommand]
        private void NavigateBack()
        {
            if (string.IsNullOrEmpty(CurrentPath)) return;
            var parts = CurrentPath.Split('/', System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length <= 1) CurrentPath = "";
            else CurrentPath = string.Join("/", parts.Take(parts.Length - 1));
        }

        [RelayCommand]
        private void OpenFolder(EncryptedItemModel folder)
        {
            if (!folder.IsFolder) return;
            if (string.IsNullOrEmpty(CurrentPath)) CurrentPath = folder.Name;
            else CurrentPath += "/" + folder.Name;
        }

        [RelayCommand]
        private void OpenExplorer()
        {
            if (_vault != null)
            {
                _driveMapper.OpenInExplorer(_webDavManager.BaseUrl);
            }
        }

        private void RefreshVaultView()
        {
            VaultFiles.Clear();
            if (_vault?.CurrentIndex == null) return;

            var current = FindFolder(_vault.CurrentIndex.Root, CurrentPath);
            if (current != null)
            {
                foreach (var child in current.Children.OrderByDescending(c => c.IsFolder).ThenBy(c => c.Name))
                {
                    VaultFiles.Add(child);
                }
            }
        }

        private EncryptedItemModel? FindFolder(EncryptedItemModel current, string targetPath)
        {
            if (string.IsNullOrEmpty(targetPath)) return current;
            var parts = targetPath.Split('/', System.StringSplitOptions.RemoveEmptyEntries);
            var ptr = current;
            foreach (var part in parts)
            {
                var next = ptr.Children.FirstOrDefault(c => c.IsFolder && c.Name == part);
                if (next == null) return null;
                ptr = next;
            }
            return ptr;
        }

        private (int totalItems, long totalBytes) ScanItemsToCopy(IEnumerable<string> paths)
        {
            int totalItems = 0;
            long totalBytes = 0;
            foreach (var path in paths)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        totalItems++;
                        totalBytes += new FileInfo(path).Length;
                    }
                    else if (Directory.Exists(path))
                    {
                        string folderName = Path.GetFileName(path);
                        if (folderName.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase) || 
                            folderName.Equals("$RECYCLE.BIN", StringComparison.OrdinalIgnoreCase)) continue;

                        var (subItems, subBytes) = ScanItemsToCopy(Directory.GetFileSystemEntries(path));
                        totalItems += subItems;
                        totalBytes += subBytes;
                    }
                }
                catch { }
            }
            return (totalItems, totalBytes);
        }

        public async Task ProcessDroppedFiles(string[] items)
        {
            if (_vault == null) return;
            IsProcessing = true;
            IsFileTransferActive = true;
            StatusMessage = "Encrypting items...";
            ProgressValue = 0;

            try
            {
                var (totalItems, totalBytes) = ScanItemsToCopy(items);

                var progressReport = new CopyProgressReport
                {
                    TotalItems = totalItems,
                    ItemsRemaining = totalItems,
                    TotalBytes = totalBytes,
                    BytesTransferred = 0
                };

                var progress = new Progress<CopyProgressReport>(report =>
                {
                    CurrentFileName = report.CurrentFileName;
                    if (report.TotalBytes > 0)
                    {
                        ProgressValue = (int)((double)report.BytesTransferred / report.TotalBytes * 100);
                    }
                    else
                    {
                        ProgressValue = 100;
                    }

                    if (report.SpeedBytesPerSecond > 0)
                    {
                        SpeedString = $"{(report.SpeedBytesPerSecond / 1024 / 1024):0.00} MB/s";
                        
                        if (report.TimeRemaining.TotalMinutes > 60)
                            TimeRemainingString = $"About {(int)report.TimeRemaining.TotalHours} hours remaining";
                        else if (report.TimeRemaining.TotalMinutes > 1)
                            TimeRemainingString = $"About {(int)report.TimeRemaining.TotalMinutes} minutes remaining";
                        else
                            TimeRemainingString = $"About {(int)report.TimeRemaining.TotalSeconds} seconds remaining";
                    }
                    else
                    {
                        SpeedString = "Calculating...";
                        TimeRemainingString = "Calculating...";
                    }

                    double itemsGb = Math.Max(0, (double)(report.TotalBytes - report.BytesTransferred) / 1024 / 1024 / 1024);
                    ItemsRemainingString = $"{report.ItemsRemaining} ({itemsGb:0.0} GB)";
                });

                Vcrypt.Core.Models.DuplicateResolution? bulkResolution = null;

                Func<string, bool, Task<Vcrypt.Core.Models.DuplicateResolution>> onDuplicate = async (fileName, isIdentical) =>
                {
                    if (bulkResolution.HasValue) return bulkResolution.Value;

                    Vcrypt.Core.Models.DuplicateResolution result = Vcrypt.Core.Models.DuplicateResolution.Skip;
                    
                    await App.Current.Dispatcher.InvokeAsync(() =>
                    {
                        var dialog = new Vcrypt.UI.Views.DuplicateResolutionDialog(fileName, isIdentical);
                        if (App.Current.MainWindow != null)
                        {
                            dialog.Owner = App.Current.MainWindow;
                        }
                        dialog.ShowDialog();
                        result = dialog.Result;
                    });

                    if (result == Vcrypt.Core.Models.DuplicateResolution.SkipAll || result == Vcrypt.Core.Models.DuplicateResolution.ReplaceAll)
                    {
                        bulkResolution = result;
                    }

                    return result;
                };

                foreach (var item in items)
                {
                    await ProcessItemRecursive(item, CurrentPath, progress, progressReport, onDuplicate);
                }
                RefreshVaultView();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show("Error adding items: " + ex.Message);
            }
            IsProcessing = false;
        }

        private async Task ProcessItemRecursive(string localPath, string vaultTargetFolder, IProgress<CopyProgressReport> progress, CopyProgressReport state, Func<string, bool, Task<Vcrypt.Core.Models.DuplicateResolution>> onDuplicate)
        {
            try
            {
                if (File.Exists(localPath))
                {
                    await _vault.EncryptFileAsync(localPath, vaultTargetFolder, progress, state, onDuplicate);
                }
                else if (Directory.Exists(localPath))
                {
                    string folderName = Path.GetFileName(localPath);
                    if (string.IsNullOrEmpty(folderName)) folderName = Path.GetPathRoot(localPath)?.TrimEnd('\\', ':') ?? "Folder";

                    // Skip system volume info and recycle bin
                    if (folderName.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase) || 
                        folderName.Equals("$RECYCLE.BIN", StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    await _vault.CreateFolderAsync(folderName, vaultTargetFolder);
                    
                    string newTargetFolder = string.IsNullOrEmpty(vaultTargetFolder) ? folderName : $"{vaultTargetFolder}/{folderName}";
                    foreach (var subItem in Directory.GetFileSystemEntries(localPath))
                    {
                        await ProcessItemRecursive(subItem, newTargetFolder, progress, state, onDuplicate);
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip files/folders we don't have access to
            }
            catch (System.Exception ex)
            {
                // Log or ignore other exceptions so it doesn't stop the whole process
                System.Diagnostics.Debug.WriteLine($"Failed to process {localPath}: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task CreateFolder()
        {
            if (_vault == null) return;
            
            // Simple prompt placeholder
            string folderName = "New Folder"; // We will add a dialog logic here or let user rename
            await _vault.CreateFolderAsync(folderName, CurrentPath);
            RefreshVaultView();
        }

        [RelayCommand]
        private async Task AddFiles()
        {
            if (_vault == null) return;

            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Multiselect = true,
                Title = "Select files to encrypt"
            };

            if (dialog.ShowDialog() == true)
            {
                await ProcessDroppedFiles(dialog.FileNames);
            }
        }

        [RelayCommand]
        private async Task AddFolders()
        {
            if (_vault == null) return;

            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Multiselect = true,
                Title = "Select folders to encrypt"
            };

            if (dialog.ShowDialog() == true)
            {
                await ProcessDroppedFiles(dialog.FolderNames);
            }
        }

        [RelayCommand]
        private async Task ExtractItem(EncryptedItemModel item)
        {
            if (_vault == null || item.IsFolder) return;
            
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Extract File",
                FileName = item.Name,
                Filter = "All Files (*.*)|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                IsProcessing = true;
                StatusMessage = "Decrypting...";
                
                try
                {
                    // Pass the exact selected file path, not just the directory
                    string selectedDirectory = System.IO.Path.GetDirectoryName(dialog.FileName) ?? "";
                    string selectedName = System.IO.Path.GetFileName(dialog.FileName);
                    
                    // Temporarily change item name to match what user chose in dialog
                    string originalName = item.Name;
                    item.Name = selectedName;
                    
                    await _vault.DecryptFileAsync(item, selectedDirectory);
                    
                    item.Name = originalName; // Restore
                    
                    MessageBox.Show($"File successfully extracted to:\n{dialog.FileName}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (System.Exception ex)
                {
                    MessageBox.Show($"Error extracting file: {ex.Message}", "Extraction Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    IsProcessing = false;
                }
            }
        }

        [RelayCommand]
        private async Task DeleteItem(EncryptedItemModel item)
        {
            if (_vault == null) return;
            var result = MessageBox.Show($"Delete {item.Name}?", "Confirm Delete", MessageBoxButton.YesNo);
            if (result == MessageBoxResult.Yes)
            {
                await _vault.DeleteItemAsync(item);
                RefreshVaultView();
            }
        }
    }
}
