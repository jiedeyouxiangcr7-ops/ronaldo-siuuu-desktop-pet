$ErrorActionPreference = 'Stop'
$petRoot = Split-Path -Parent $PSScriptRoot
$netRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$refs = @('System.dll','System.Core.dll','System.Xml.dll','System.Xml.Linq.dll','System.Drawing.dll','System.Windows.Forms.dll','WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll','System.Xaml.dll')
$arguments = @('/nologo','/target:winexe','/platform:anycpu','/optimize+',('/out:' + (Join-Path $petRoot 'RonaldoPet.exe')),('/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')))
foreach ($reference in $refs) {
    $referencePath = Join-Path $netRoot $reference
    if (!(Test-Path -LiteralPath $referencePath)) { $referencePath = Join-Path (Join-Path $netRoot 'WPF') $reference }
    $arguments += '/reference:' + $referencePath
}
$icon = Join-Path $petRoot 'Assets\ronaldo.ico'
if (Test-Path -LiteralPath $icon) { $arguments += '/win32icon:' + $icon }
$arguments += Join-Path $PSScriptRoot 'RonaldoPet.cs'
& (Join-Path $netRoot 'csc.exe') @arguments
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
Write-Output (Join-Path $petRoot 'RonaldoPet.exe')
