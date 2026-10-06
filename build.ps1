param([string]$OutputDirectory = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' -Recurse | ForEach-Object { $_.FullName })
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 "/win32icon:$PSScriptRoot\assets\mint-girl.ico" "/out:$OutputDirectory\ProgressGlass.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll $sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
if ($OutputDirectory -ne $PSScriptRoot) {
    $assetOutput = Join-Path $OutputDirectory 'assets'
    New-Item -ItemType Directory -Force -Path $assetOutput | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'assets') -File | Where-Object { $_.Extension -in @('.png','.ico') } | Copy-Item -Destination $assetOutput -Force
    foreach ($document in @('README.md','README.en.md','preview-style.png')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $document) -Destination $OutputDirectory -Force
    }
}
$progressOutput = Join-Path $OutputDirectory 'progress.json'
if (-not (Test-Path -LiteralPath $progressOutput)) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'examples\blank-progress.json') -Destination $progressOutput
}
Write-Output "Built: $OutputDirectory\ProgressGlass.exe"
