$ErrorActionPreference = 'Stop'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$PSScriptRoot\test.ps1"
if ($LASTEXITCODE -ne 0) { throw 'Tests failed; no release package was created.' }
$packageRoot = Join-Path $PSScriptRoot ('dist\portable-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $packageRoot | Out-Null
New-Item -ItemType Directory -Path "$packageRoot\docs" | Out-Null
Copy-Item -LiteralPath "$PSScriptRoot\dist\checks\QuickCrop.exe" -Destination "$packageRoot\QuickCrop.exe" -Force
Copy-Item -LiteralPath "$PSScriptRoot\LICENSE" -Destination "$packageRoot\LICENSE" -Force
Copy-Item -LiteralPath "$PSScriptRoot\README.md" -Destination "$packageRoot\README.md" -Force
Copy-Item -LiteralPath "$PSScriptRoot\CHANGELOG.md" -Destination "$packageRoot\CHANGELOG.md" -Force
foreach ($docName in @('USER_GUIDE.md', 'DEVELOPMENT.md', 'REVIEW.md')) {
    Copy-Item -LiteralPath (Join-Path "$PSScriptRoot\docs" $docName) -Destination (Join-Path "$packageRoot\docs" $docName)
}
$archive = Join-Path $PSScriptRoot 'dist\QuickCrop-portable.zip'
# The unique staging directory contains only the explicit copies above. Framework ZIP
# reading permits shared reads, unlike Compress-Archive's exclusive source-file opens.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$temporaryArchive = Join-Path $PSScriptRoot ('dist\package-' + [Guid]::NewGuid().ToString('N') + '.zip')
try {
    [System.IO.Compression.ZipFile]::CreateFromDirectory($packageRoot, $temporaryArchive)
    Move-Item -LiteralPath $temporaryArchive -Destination $archive -Force
} finally {
    if (Test-Path -LiteralPath $temporaryArchive) { Remove-Item -LiteralPath $temporaryArchive }
}
$digest = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$archive.sha256" -Value "$digest  QuickCrop-portable.zip" -Encoding Ascii
Write-Host "Packaged $archive"
