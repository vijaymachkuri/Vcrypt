using System;
using System.Management;

class Program 
{ 
    static void Main() 
    { 
        var letter = "F";
        string query1 = $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{letter}:'}} WHERE AssocClass=Win32_LogicalDiskToPartition";
        using (var searcher1 = new ManagementObjectSearcher(query1))
        {
            foreach (ManagementObject partition in searcher1.Get())
            {
                Console.WriteLine($"Found Partition: {partition["DeviceID"]}");
                string query2 = $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partition["DeviceID"]}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition";
                using (var searcher2 = new ManagementObjectSearcher(query2))
                {
                    foreach (ManagementObject drive in searcher2.Get())
                    {
                        var interfaceType = drive["InterfaceType"]?.ToString();
                        Console.WriteLine($"Found Drive InterfaceType: {interfaceType}");
                    }
                }
            }
        }
    } 
}
