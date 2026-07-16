Add-Type -AssemblyName System.Drawing
$pngPath = 'c:\Users\vijay\Desktop\securepro\Vcrypt.UI\icon.png'
$icoPath = 'c:\Users\vijay\Desktop\securepro\Vcrypt.UI\icon.ico'

$img = [System.Drawing.Image]::FromFile($pngPath)

# Calculate scale to preserve aspect ratio within 256x256
$targetSize = 256
$ratioX = $targetSize / $img.Width
$ratioY = $targetSize / $img.Height
$ratio = [Math]::Min($ratioX, $ratioY)

$newWidth = [int]($img.Width * $ratio)
$newHeight = [int]($img.Height * $ratio)

# Create a new blank 256x256 transparent bitmap
$bmp = [System.Drawing.Bitmap]::new($targetSize, $targetSize)
$bmp.MakeTransparent()

# Draw the scaled image into the center
$graphics = [System.Drawing.Graphics]::FromImage($bmp)
$graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$graphics.Clear([System.Drawing.Color]::Transparent)

$posX = ($targetSize - $newWidth) / 2
$posY = ($targetSize - $newHeight) / 2

$graphics.DrawImage($img, $posX, $posY, $newWidth, $newHeight)

# Create ICO file
$stream = [System.IO.FileStream]::new($icoPath, [System.IO.FileMode]::Create)
$stream.WriteByte(0); $stream.WriteByte(0)
$stream.WriteByte(1); $stream.WriteByte(0)
$stream.WriteByte(1); $stream.WriteByte(0)

$stream.WriteByte(0); $stream.WriteByte(0)
$stream.WriteByte(0); $stream.WriteByte(0)
$stream.WriteByte(1); $stream.WriteByte(0)
$stream.WriteByte(32); $stream.WriteByte(0)

$ms = [System.IO.MemoryStream]::new()
$bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
$bytes = $ms.ToArray()
$size = $bytes.Length

$stream.Write([System.BitConverter]::GetBytes([int]$size), 0, 4)
$stream.Write([System.BitConverter]::GetBytes([int]22), 0, 4)
$stream.Write($bytes, 0, $size)

$stream.Close()
$graphics.Dispose()
$bmp.Dispose()
$img.Dispose()
$ms.Dispose()

Write-Output "Icon created successfully with correct aspect ratio and transparency!"
