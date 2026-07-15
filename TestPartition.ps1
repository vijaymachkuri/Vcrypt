Clear-Disk -Number 2 -RemoveData -Confirm:$false
New-Partition -DiskNumber 2 -Size 250MB -AssignDriveLetter | Format-Volume -FileSystem exFAT -NewFileSystemLabel 'Public' -Force -Confirm:$false
New-Partition -DiskNumber 2 -UseMaximumSize | Format-Volume -FileSystem NTFS -NewFileSystemLabel 'SecureVault' -Force -Confirm:$false
