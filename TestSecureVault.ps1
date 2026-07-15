Remove-Partition -DiskNumber 2 -PartitionNumber 2 -Confirm:$false
New-Partition -DiskNumber 2 -UseMaximumSize -AssignDriveLetter | Format-Volume -FileSystem NTFS -NewFileSystemLabel 'SecureVault' -Force
$driveLetter = (Get-Partition -DiskNumber 2 -PartitionNumber 2).DriveLetter
if ($driveLetter) { Remove-PartitionAccessPath -DiskNumber 2 -PartitionNumber 2 -AccessPath "$($driveLetter):\" }
Set-Partition -DiskNumber 2 -PartitionNumber 2 -MbrType 23
