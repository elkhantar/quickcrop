$ErrorActionPreference = 'Stop'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$PSScriptRoot\build.ps1"
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll "/r:$PSScriptRoot\dist\checks\QuickCrop.exe" "/out:$PSScriptRoot\dist\checks\Tests.exe" "$PSScriptRoot\Tests.cs" "$PSScriptRoot\ReviewTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
& "$PSScriptRoot\dist\checks\Tests.exe"
if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
