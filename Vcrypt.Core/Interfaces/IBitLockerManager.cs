using System;
using System.Threading.Tasks;

namespace Vcrypt.Core.Interfaces
{
    public interface IBitLockerManager
    {
        Task<bool> EnableBitLockerAsync(string driveLetter, string password, IProgress<string>? progressText = null, IProgress<int>? progressValue = null);
        Task<bool> UnlockBitLockerAsync(string driveLetter, string password);
        Task<bool> LockBitLockerAsync(string driveLetter);
        Task<bool> IsBitLockerEnabledAsync(string driveLetter);
    }
}
