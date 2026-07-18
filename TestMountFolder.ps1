# Test mount to folder
$mountPath = "C:\ProgramData\Vcrypt\Mounts\TestDrive"
if (-not (Test-Path $mountPath)) {
    New-Item -ItemType Directory -Force -Path $mountPath
}
# Find the first USB drive's second partition
$disk = Get-Disk | Where-Object BusType -eq 'USB' | Select-Object -First 1
if ($disk) {
    $part = Get-Partition -DiskNumber $disk.Number -PartitionNumber 2 -ErrorAction SilentlyContinue
    if ($part) {
        Add-PartitionAccessPath -InputObject $part -AccessPath $mountPath
        Write-Output "Mounted to $mountPath"
    } else {
        Write-Output "No partition 2 found."
    }
} else {
    Write-Output "No USB disk found."
}
