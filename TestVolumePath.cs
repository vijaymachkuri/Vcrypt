using System;
using System.IO;
using System.Management;

class Program
{
    static void Main()
    {
        Console.WriteLine("Testing Volume GUID access...");
        var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Volume WHERE DriveLetter = 'C:'");
        string volumePath = "";
        foreach (ManagementObject vol in searcher.Get())
        {
            volumePath = vol["DeviceID"].ToString();
            Console.WriteLine($"Found C: at {volumePath}");
        }

        if (!string.IsNullOrEmpty(volumePath))
        {
            string testPath = Path.Combine(volumePath, "Temp", "VolumeTest.txt");
            try
            {
                File.WriteAllText(testPath, "This is a test writing via Volume GUID!");
                Console.WriteLine("Success writing to: " + testPath);
                Console.WriteLine("Read: " + File.ReadAllText(testPath));
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }
        }
    }
}
