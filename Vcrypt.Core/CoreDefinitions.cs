using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Vcrypt.Core.Models
{
    public enum DuplicateResolution
    {
        Skip,
        Replace,
        SkipAll,
        ReplaceAll
    }

    public class EncryptedItemModel
    {
        public string Name { get; set; } = string.Empty;
        public bool IsFolder { get; set; }
        public long Size { get; set; }
        public string BlobId { get; set; } = string.Empty;
        public string FileHash { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public List<EncryptedItemModel> Children { get; set; } = new();
    }

    public class VaultIndex
    {
        public EncryptedItemModel Root { get; set; } = new EncryptedItemModel { Name = "Root", IsFolder = true };
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
        Task EncryptFileAsync(string sourcePath, string targetParentPath = "", IProgress<CopyProgressReport>? progress = null, CopyProgressReport? state = null, Func<string, bool, Task<DuplicateResolution>>? onDuplicateFound = null);
        Task DecryptFileAsync(EncryptedItemModel file, string destinationDirectory);
        Task<string> ComputeHashForEncryptedBlobAsync(EncryptedItemModel file);
        Task DeleteItemAsync(EncryptedItemModel item);
        Task CreateFolderAsync(string folderName, string parentPath = "");
    }

    public interface IUsbMonitorService : IDisposable
    {
        event EventHandler<UsbDriveEventArgs> DriveInserted;
        event EventHandler<UsbDriveEventArgs> DriveRemoved;
        void StartMonitoring();
    }

    public class VaultCapacityInfo
    {
        public long TotalBytes { get; set; }
        public long FreeBytes { get; set; }
    }

    public interface IPartitionManager
    {
        Task<string?> FormatAndLockAsync(string driveLetter, int publicMB, string fileSystem, string passwordHash, IProgress<string>? progressText = null, IProgress<int>? progressValue = null);
        Task<string?> MountVaultAsync(string publicDriveLetter);
        Task<bool> UnmountVaultAsync(string publicDriveLetter);
        Task<VaultCapacityInfo?> GetVaultCapacityAsync(string publicDriveLetter);
    }
}
