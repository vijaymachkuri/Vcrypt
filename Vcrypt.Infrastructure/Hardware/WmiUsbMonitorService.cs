using Vcrypt.Core.Interfaces;
using Vcrypt.Core.Models;
using System;
using System.IO;
using System.Management;
using System.Threading.Tasks;

namespace Vcrypt.Infrastructure.Hardware
{
    public class WmiUsbMonitorService : IUsbMonitorService
    {
        private ManagementEventWatcher? _insertWatcher;
        private ManagementEventWatcher? _removeWatcher;

        public event EventHandler<UsbDriveEventArgs>? DriveInserted;
        public event EventHandler<UsbDriveEventArgs>? DriveRemoved;

        public void StartMonitoring()
        {
            try
            {
                var insertQuery = new WqlEventQuery("SELECT * FROM __InstanceCreationEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_Volume'");
                _insertWatcher = new ManagementEventWatcher(insertQuery);
                _insertWatcher.EventArrived += OnDriveInserted;
                _insertWatcher.Start();

                var removeQuery = new WqlEventQuery("SELECT * FROM __InstanceDeletionEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_Volume'");
                _removeWatcher = new ManagementEventWatcher(removeQuery);
                _removeWatcher.EventArrived += OnDriveRemoved;
                _removeWatcher.Start();
                
                Task.Run(() => InitialScan());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error starting WMI monitor: {ex.Message}");
            }
        }
        
        private void InitialScan()
        {
            try
            {
                var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Volume WHERE DriveType=2 OR DriveType=3");
                foreach (ManagementObject queryObj in searcher.Get())
                {
                    var letter = queryObj["DriveLetter"]?.ToString();
                    if (!string.IsNullOrEmpty(letter))
                    {
                        CheckAndFireEvent(letter, queryObj["Label"]?.ToString());
                    }
                }
            }
            catch { }
        }

        private void OnDriveInserted(object sender, EventArrivedEventArgs e)
        {
            var instance = (ManagementBaseObject)e.NewEvent["TargetInstance"];
            var driveType = Convert.ToUInt32(instance["DriveType"]);
            
            if (driveType == 2 || driveType == 3)
            {
                var driveLetter = instance["DriveLetter"]?.ToString();
                var label = instance["Label"]?.ToString();
                
                if (!string.IsNullOrEmpty(driveLetter))
                {
                    Task.Delay(1500).ContinueWith(_ => CheckAndFireEvent(driveLetter, label));
                }
            }
        }

        private void OnDriveRemoved(object sender, EventArrivedEventArgs e)
        {
            var instance = (ManagementBaseObject)e.NewEvent["TargetInstance"];
            var driveType = Convert.ToUInt32(instance["DriveType"]);

            if (driveType == 2 || driveType == 3)
            {
                var driveLetter = instance["DriveLetter"]?.ToString();
                if (!string.IsNullOrEmpty(driveLetter))
                {
                    DriveRemoved?.Invoke(this, new UsbDriveEventArgs { DriveLetter = driveLetter });
                }
            }
        }
        
        private void CheckAndFireEvent(string driveLetter, string? volumeName)
        {
            if (!IsUsbDrive(driveLetter)) return;

            bool isVcrypt = false;
            try
            {
                var sigPath = Path.Combine(driveLetter + "\\", ".Vcrypt");
                if (File.Exists(sigPath))
                {
                    isVcrypt = true;
                }
            }
            catch { }

            DriveInserted?.Invoke(this, new UsbDriveEventArgs 
            { 
                DriveLetter = driveLetter, 
                VolumeName = volumeName ?? "USB Drive",
                IsVcrypt = isVcrypt 
            });
        }

        private bool IsUsbDrive(string driveLetter)
        {
            try
            {
                var letter = driveLetter.TrimEnd(':', '\\');
                string query1 = $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{letter}:'}} WHERE AssocClass=Win32_LogicalDiskToPartition";
                using (var searcher1 = new ManagementObjectSearcher(query1))
                {
                    foreach (ManagementObject partition in searcher1.Get())
                    {
                        string query2 = $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partition["DeviceID"]}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition";
                        using (var searcher2 = new ManagementObjectSearcher(query2))
                        {
                            foreach (ManagementObject drive in searcher2.Get())
                            {
                                var interfaceType = drive["InterfaceType"]?.ToString();
                                var mediaType = drive["MediaType"]?.ToString();
                                
                                if (string.Equals(interfaceType, "USB", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(mediaType, "External hard disk media", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(mediaType, "Removable Media", StringComparison.OrdinalIgnoreCase))
                                {
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            
            return false;
        }

        public void Dispose()
        {
            _insertWatcher?.Stop();
            _insertWatcher?.Dispose();
            _removeWatcher?.Stop();
            _removeWatcher?.Dispose();
        }
    }
}
