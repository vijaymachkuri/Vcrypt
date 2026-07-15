Remove-Partition -DiskNumber 2 -PartitionNumber 2 -Confirm:$false
New-Partition -DiskNumber 2 -UseMaximumSize -AssignDriveLetter | Format-Volume -FileSystem NTFS -NewFileSystemLabel 'SecureVault' -Force
$dl = (Get-Partition -DiskNumber 2 -PartitionNumber 2).DriveLetter
Write-Output "Drive Letter: $dl"
Remove-PartitionAccessPath -DiskNumber 2 -PartitionNumber 2 -AccessPath "$($dl):"
Remove-PartitionAccessPath -DiskNumber 2 -PartitionNumber 2 -AccessPath "$($dl):\"
