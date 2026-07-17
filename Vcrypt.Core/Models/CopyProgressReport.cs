using System;

namespace Vcrypt.Core.Models
{
    public class CopyProgressReport
    {
        public string CurrentFileName { get; set; } = string.Empty;
        public int TotalItems { get; set; }
        public int ItemsRemaining { get; set; }
        public long BytesTransferred { get; set; }
        public long TotalBytes { get; set; }
        public double SpeedBytesPerSecond { get; set; }
        public TimeSpan TimeRemaining { get; set; }
    }
}
