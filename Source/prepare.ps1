$ErrorActionPreference = 'Stop'
$petRoot = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$helper = Join-Path $PSScriptRoot 'PrepareAssets.exe'
& $compiler /nologo /target:exe /reference:System.Drawing.dll ('/out:' + $helper) (Join-Path $PSScriptRoot 'PrepareAssets.cs')
if ($LASTEXITCODE -ne 0) { throw 'Asset helper compilation failed' }
& $helper $petRoot
if ($LASTEXITCODE -ne 0) { throw 'Asset preparation failed' }
