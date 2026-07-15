Add-Type -AssemblyName System.Drawing
$pngPath = 'c:\Users\vijay\Desktop\securepro\SecureDock.UI\icon.png'
$icoPath = 'c:\Users\vijay\Desktop\securepro\SecureDock.UI\icon.ico'

$img = [System.Drawing.Image]::FromFile($pngPath)
$resized = [System.Drawing.Bitmap]::new($img, 256, 256)

$stream = [System.IO.FileStream]::new($icoPath, [System.IO.FileMode]::Create)
$stream.WriteByte(0); $stream.WriteByte(0)
$stream.WriteByte(1); $stream.WriteByte(0)
$stream.WriteByte(1); $stream.WriteByte(0)

$stream.WriteByte(0); $stream.WriteByte(0)
$stream.WriteByte(0); $stream.WriteByte(0)
$stream.WriteByte(1); $stream.WriteByte(0)
$stream.WriteByte(32); $stream.WriteByte(0)

$ms = [System.IO.MemoryStream]::new()
$resized.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
$bytes = $ms.ToArray()
$size = $bytes.Length

$stream.Write([System.BitConverter]::GetBytes([int]$size), 0, 4)
$stream.Write([System.BitConverter]::GetBytes([int]22), 0, 4)
$stream.Write($bytes, 0, $size)

$stream.Close()
$resized.Dispose()
$img.Dispose()
$ms.Dispose()

Write-Output "Icon created successfully!"
