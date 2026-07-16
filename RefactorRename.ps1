$dir = "c:\Users\vijay\Desktop\securepro"

# 1. Replace text in files
$extensions = @("*.cs", "*.xaml", "*.csproj", "*.sln", "*.md", "*.ps1")
foreach ($ext in $extensions) {
    $files = Get-ChildItem -Path $dir -Filter $ext -Recurse | Where-Object { $_.FullName -notmatch "\\bin\\" -and $_.FullName -notmatch "\\obj\\" -and $_.FullName -notmatch "\\build\\" }
    foreach ($file in $files) {
        $content = Get-Content $file.FullName -Raw
        if ($content -match "Vcrypt") {
            $newContent = $content -replace "Vcrypt", "Vcrypt"
            Set-Content -Path $file.FullName -Value $newContent -NoNewline
        }
    }
}

# 2. Rename .csproj files
$csprojFiles = Get-ChildItem -Path $dir -Filter "Vcrypt*.csproj" -Recurse
foreach ($file in $csprojFiles) {
    $newName = $file.Name -replace "Vcrypt", "Vcrypt"
    Rename-Item -Path $file.FullName -NewName $newName
}

# 3. Rename .sln file
$slnFiles = Get-ChildItem -Path $dir -Filter "Vcrypt.sln"
foreach ($file in $slnFiles) {
    $newName = $file.Name -replace "Vcrypt", "Vcrypt"
    Rename-Item -Path $file.FullName -NewName $newName
}

# 4. Rename directories
$dirsToRename = Get-ChildItem -Path $dir -Filter "Vcrypt*" -Directory
foreach ($d in $dirsToRename) {
    $newName = $d.Name -replace "Vcrypt", "Vcrypt"
    Rename-Item -Path $d.FullName -NewName $newName
}

Write-Output "Refactor complete."
