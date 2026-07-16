using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Vcrypt.Core.Models
{
    public class EncryptedFileModel
    {
        public string OriginalName { get; set; } = string.Empty;
        public long Size { get; set; }
        public string BlobId { get; set; } = string.Empty;
    }

    public class VaultIndex
    {
        public List<EncryptedFileModel> Files { get; set; } = new List<EncryptedFileModel>();
    }

    public class UsbDriveEventArgs : EventArgs
    {
        public string DriveLetter { get; set; } = string.Empty;
        public string VolumeName { get; set; } = string.Empty;
        public bool IsVcrypt { get; set; }
    }
}

namespace Vcrypt.Core.Interfaces
{
    using Vcrypt.Core.Models;

    public interface IEncryptionProvider
    {
        bool IsUnlocked { get; }
        VaultIndex? CurrentIndex { get; }
        
        void Initialize(string vaultPath);
        Task InitializeNewVaultAsync(string password);
        Task<bool> UnlockAsync(string password);
        void Lock();
        Task EncryptFileAsync(string sourcePath);
    }

    public interface IUsbMonitorService : IDisposable
    {
        event EventHandler<UsbDriveEventArgs> DriveInserted;
        event EventHandler<UsbDriveEventArgs> DriveRemoved;
        void StartMonitoring();
    }

    public interface IPartitionManager
    {
        Task<string?> FormatAndLockAsync(string driveLetter, int publicMB, string fileSystem, string passwordHash, IProgress<string>? progressText = null, IProgress<int>? progressValue = null);
        Task<string?> MountVaultAsync();
        Task<bool> UnmountVaultAsync();
    }
}
