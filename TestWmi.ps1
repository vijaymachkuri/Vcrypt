$parts = Get-WmiObject -Query "ASSOCIATORS OF {Win32_LogicalDisk.DeviceID='F:'} WHERE AssocClass=Win32_LogicalDiskToPartition"
foreach ($p in $parts) {
    $drives = Get-WmiObject -Query "ASSOCIATORS OF {Win32_DiskPartition.DeviceID='$($p.DeviceID)'} WHERE AssocClass=Win32_DiskDriveToDiskPartition"
    foreach ($d in $drives) {
        $d | Format-List *
    }
}
