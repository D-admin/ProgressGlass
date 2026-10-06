param(
    [string]$File = (Join-Path $PSScriptRoot 'progress.json'),
    [string]$TaskId,
    [ValidateSet('todo','doing','blocked','review','done')][string]$Status,
    [string]$Evidence,
    [double]$MeasuredCompleted,
    [double]$MeasuredTotal,
    [string]$Unit,
    [string]$Source,
    [string]$ObservedAt,
    [switch]$ClearMeasurement,
    [double]$Progress,
    [int]$CompletedUnits,
    [int]$TotalUnits,
    [double]$Weight,
    [string]$Current,
    [string]$Next
)
$ErrorActionPreference = 'Stop'
foreach ($obsolete in @('Progress','CompletedUnits','TotalUnits','Weight')) {
    if ($PSBoundParameters.ContainsKey($obsolete)) { throw 'v0.3 no longer accepts estimated percentages, stage counts or weights. Use measured counts with Unit/Source, or state and evidence only.' }
}
$File = [IO.Path]::GetFullPath($File)
$guard=$null; $temp=$null
try {
    $guard=[IO.File]::Open($File+'.lock','OpenOrCreate','ReadWrite','None')
    $board=Get-Content -LiteralPath $File -Raw -Encoding UTF8 | ConvertFrom-Json
    if (-not $board.project -or $null -eq $board.tasks) { throw 'Invalid project record.' }
    $metricKeys=@('MeasuredCompleted','MeasuredTotal','Unit','Source','ObservedAt')
    $hasMetric=@($metricKeys | Where-Object { $PSBoundParameters.ContainsKey($_) }).Count -gt 0
    if (($Status -or $PSBoundParameters.ContainsKey('Evidence') -or $hasMetric -or $ClearMeasurement) -and -not $TaskId) { throw 'TaskId required.' }
    if ($TaskId) {
        $found=@($board.tasks | Where-Object {$_.id -eq $TaskId})
        if ($found.Count -ne 1) { throw 'Task ID must match exactly one task.' }
        $task=$found[0]
        if ($hasMetric -and $ClearMeasurement) { throw 'Cannot clear and supply a measurement at the same time.' }
        if ($hasMetric) {
            foreach ($required in @('MeasuredCompleted','MeasuredTotal','Unit','Source')) { if(-not $PSBoundParameters.ContainsKey($required)) { throw "Missing measurement field: $required" } }
            if (-not $ObservedAt) { $ObservedAt=[DateTimeOffset]::Now.ToString('o') }
            $metric=[pscustomobject]@{completed=$MeasuredCompleted;total=$MeasuredTotal;unit=$Unit;source=$Source;observedAt=$ObservedAt}
            $task | Add-Member -NotePropertyName measurement -NotePropertyValue $metric -Force
        }
        if ($ClearMeasurement) { $task.PSObject.Properties.Remove('measurement') }
        # Preserve archival legacy data in unrelated tasks; stop presenting it as measured work.
        foreach ($old in @('progress','completedUnits','totalUnits','weight')) { $task.PSObject.Properties.Remove($old) }
        if ($Status) { $task.status=$Status }
        if ($PSBoundParameters.ContainsKey('Evidence')) { $task | Add-Member -NotePropertyName evidence -NotePropertyValue $Evidence -Force }
        if ($task.status -eq 'done' -and [string]::IsNullOrWhiteSpace($task.evidence)) { throw 'Verified completion requires evidence.' }
    }
    $ids=@{}
    foreach ($item in $board.tasks) {
        if (-not $item.id -or -not $item.title -or $ids.ContainsKey($item.id)) { throw 'Missing or duplicate task ID/title.' }
        $ids[$item.id]=$true
        if ($item.status -notin @('todo','doing','blocked','review','done')) { throw 'Unknown status.' }
        $m=$item.measurement
        if ($null -ne $m) {
            if ([double]::IsNaN($m.completed) -or [double]::IsInfinity($m.completed) -or [double]::IsNaN($m.total) -or [double]::IsInfinity($m.total) -or $m.total -le 0 -or $m.completed -lt 0 -or $m.completed -gt $m.total) { throw 'Invalid measured counts.' }
            if ([string]::IsNullOrWhiteSpace($m.unit) -or [string]::IsNullOrWhiteSpace($m.source)) { throw 'Measurement needs a unit and verifiable source.' }
            $parsedTime=[DateTimeOffset]::MinValue
            if (-not [DateTimeOffset]::TryParse($m.observedAt,[ref]$parsedTime)) { throw 'Invalid measurement timestamp.' }
            if ($item.status -eq 'done' -and $m.completed -ne $m.total) { throw 'Cannot mark incomplete measured work as done; counts are never auto-filled.' }
        }
    }
    if ($PSBoundParameters.ContainsKey('Current')) {$board.current=$Current}
    if ($PSBoundParameters.ContainsKey('Next')) {$board.next=$Next}
    $board.updatedAt=[DateTimeOffset]::Now.ToString('o')
    $temp=$File+'.'+[Guid]::NewGuid().ToString('N')+'.tmp'
    [IO.File]::WriteAllText($temp,($board|ConvertTo-Json -Depth 15),[Text.UTF8Encoding]::new($false))
    [IO.File]::Replace($temp,$File,[NullString]::Value)
    "Updated $File"
} finally {
    if($guard){$guard.Dispose()}
    if($temp -and [IO.File]::Exists($temp)){[IO.File]::Delete($temp)}
}
