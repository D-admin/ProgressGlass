param([string]$ArtifactDirectory = (Join-Path ([IO.Path]::GetTempPath()) ('ProgressGlass-tests-' + [Guid]::NewGuid().ToString('N'))))
$ErrorActionPreference = 'Stop'
$ArtifactDirectory=[IO.Path]::GetFullPath($ArtifactDirectory)
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $ArtifactDirectory
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /platform:x64 /codepage:65001 "/out:$ArtifactDirectory\ProgressGlassTests.exe" "/reference:$ArtifactDirectory\ProgressGlass.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll (Join-Path $PSScriptRoot 'tests\ProgressGlassTests.cs')
if($LASTEXITCODE -ne 0){throw 'Test compilation failed'}
& (Join-Path $ArtifactDirectory 'ProgressGlassTests.exe') $ArtifactDirectory
if($LASTEXITCODE -ne 0){throw 'Core tests failed'}
$record=Join-Path $ArtifactDirectory 'writer-test.json'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'examples\blank-progress.json') -Destination $record
$id=(Get-Content -LiteralPath $record -Raw|ConvertFrom-Json).tasks[0].id
$update=Join-Path $PSScriptRoot 'update-progress.ps1'
& $update -File $record -TaskId $id -Status doing -MeasuredCompleted 240 -MeasuredTotal 1000 -Unit '条' -Source 'worker-result.json' -Evidence 'Test fixture'
$t=(Get-Content -LiteralPath $record -Raw|ConvertFrom-Json).tasks|Where-Object {$_.id -eq $id}
if($t.measurement.completed -ne 240 -or $t.measurement.total -ne 1000){throw 'Measured count update failed'}
foreach($invalid in @(@{Progress=33},@{CompletedUnits=1;TotalUnits=3},@{Status='done'},@{MeasuredCompleted=1001;MeasuredTotal=1000;Unit='条';Source='fixture'},@{MeasuredCompleted=10;MeasuredTotal=100;Unit='条';Source=''})){
    $before=(Get-FileHash -LiteralPath $record).Hash;$rejected=$false
    try{& $update -File $record -TaskId $id @invalid}catch{$rejected=$true}
    if(-not $rejected -or (Get-FileHash -LiteralPath $record).Hash -ne $before){throw 'Invalid update changed file'}
}
& $update -File $record -TaskId $id -MeasuredCompleted 1000 -MeasuredTotal 1000 -Unit '条' -Source 'fixture final count' -Status done -Evidence 'Fixture verified'
'PASS writer measured counts; unsupported estimates and auto-completion rejected'
