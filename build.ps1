$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
New-Item -ItemType Directory -Force -Path "$PSScriptRoot\dist" | Out-Null
New-Item -ItemType Directory -Force -Path "$PSScriptRoot\dist\checks" | Out-Null
$framework = Split-Path $compiler
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:System.Xaml.dll /r:System.Web.Extensions.dll "/r:$framework\WPF\WindowsBase.dll" "/r:$framework\WPF\PresentationCore.dll" "/out:$PSScriptRoot\dist\checks\QuickCrop.exe" "$PSScriptRoot\QuickCrop.cs"
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
try {
    Copy-Item -LiteralPath "$PSScriptRoot\dist\checks\QuickCrop.exe" -Destination "$PSScriptRoot\dist\QuickCrop.exe" -Force
    Write-Host 'Built dist\QuickCrop.exe (portable; no installer required).'
} catch {
    Copy-Item -LiteralPath "$PSScriptRoot\dist\checks\QuickCrop.exe" -Destination "$PSScriptRoot\dist\QuickCrop-updated.exe" -Force
    Write-Host 'QuickCrop.exe is in use; built dist\QuickCrop-updated.exe. The running app was left open.'
}
