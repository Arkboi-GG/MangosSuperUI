#requires -Version 5.1
<#
  Find-BotBrainServiceRefs.ps1

  Finds everything that references BotBrainService directly, and — keyed off the
  PUBLIC members actually declared in BotBrainService.cs — which of those members
  are called from OTHER files. That call set is the exact surface the rebuilt
  host must preserve to keep the build green.

  Pure text scan. No build required.

  Run from the project folder:
      .\Find-BotBrainServiceRefs.ps1
  Or from anywhere:
      powershell -ExecutionPolicy Bypass -File .\Find-BotBrainServiceRefs.ps1

  Output prints to the console AND is saved to .\botbrainservice-refs.txt
#>

param(
    [string]$Root = 'C:\Users\nico\source\repos\MangosSuperUI\MangosSuperUI'
)

$ErrorActionPreference = 'Stop'
$type = 'BotBrainService'
$out  = New-Object System.Collections.Generic.List[string]
function W([string]$s = '') { [void]$out.Add($s) }

W "=== BotBrainService reference scan ==="
W ("root : {0}" -f $Root)
W ("time : {0}" -f (Get-Date -Format s))
W ""

# ---- collect .cs files (skip bin/obj) ----
$allCs = Get-ChildItem -Path $Root -Recurse -Filter *.cs |
         Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }
$defFile = $allCs | Where-Object { $_.Name -ieq 'BotBrainService.cs' } | Select-Object -First 1
W ("{0} .cs files scanned (bin/obj excluded)" -f $allCs.Count)
if ($defFile) { W ("definition : {0}" -f $defFile.FullName) } else { W "definition : NOT FOUND (BotBrainService.cs)" }
W ""

# ---- public members declared in BotBrainService.cs (candidate surface) ----
$memberNames = New-Object System.Collections.Generic.List[string]
if ($defFile) {
    $defText  = Get-Content -Raw -LiteralPath $defFile.FullName
    $methodRx = [regex]'(?m)^\s*public\s+(?:(?:async|static|virtual|override|sealed|new|unsafe|partial)\s+)*[A-Za-z_][\w<>\[\]\.,\?\s]*?\s+([A-Za-z_]\w*)\s*\('
    $propRx   = [regex]'(?m)^\s*public\s+(?:(?:static|virtual|override|sealed|new|partial)\s+)*[A-Za-z_][\w<>\[\]\.,\?\s]*?\s+([A-Za-z_]\w*)\s*(?:\{|=>)'
    foreach ($m in $methodRx.Matches($defText)) { [void]$memberNames.Add($m.Groups[1].Value) }
    foreach ($m in $propRx.Matches($defText))   { [void]$memberNames.Add($m.Groups[1].Value) }
    $memberNames = @($memberNames | Where-Object { $_ -ne $type } | Sort-Object -Unique)

    W ("=== PUBLIC members declared in BotBrainService.cs ({0}) ===" -f $memberNames.Count)
    foreach ($mn in $memberNames) { W ("  ." + $mn) }
    W ""
}

# ---- files that reference the TYPE (excluding the definition itself) ----
$consumers = @()
foreach ($f in $allCs) {
    if ($defFile -and $f.FullName -eq $defFile.FullName) { continue }
    if (Select-String -LiteralPath $f.FullName -Pattern ('\b' + $type + '\b') -Quiet) { $consumers += $f }
}
W ("=== CONSUMER FILES: {0} reference {1} ===" -f $consumers.Count, $type)
foreach ($f in $consumers) { W ("  " + $f.FullName) }
W ""

# ---- per-consumer: type-reference lines + member-call lines ----
$accessed = @{}   # member -> list of "File:line"
foreach ($f in $consumers) {
    W ("----- " + $f.FullName)
    W "  [type refs]"
    foreach ($mm in (Select-String -LiteralPath $f.FullName -Pattern ('\b' + $type + '\b'))) {
        W ('    {0,5}: {1}' -f $mm.LineNumber, $mm.Line.Trim())
    }

    if ($memberNames.Count -gt 0) {
        W "  [member calls  ->  .<member>]"
        $found = $false
        foreach ($mn in $memberNames) {
            foreach ($h in (Select-String -LiteralPath $f.FullName -Pattern ('\.' + [regex]::Escape($mn) + '\b'))) {
                $found = $true
                W ('    {0,5}: .{1,-26} | {2}' -f $h.LineNumber, $mn, $h.Line.Trim())
                if (-not $accessed.ContainsKey($mn)) { $accessed[$mn] = @() }
                $accessed[$mn] += ('{0}:{1}' -f $f.Name, $h.LineNumber)
            }
        }
        if (-not $found) { W "    (type referenced, but no declared public member call matched)" }
    }
    W ""
}

# ---- summary ----
W "=== SURFACE TO PRESERVE (public members called from other files) ==="
if ($accessed.Count -eq 0) {
    W "  (none auto-detected)"
} else {
    foreach ($mn in ($accessed.Keys | Sort-Object)) {
        W ('  .{0,-28} <- {1}' -f $mn, (($accessed[$mn] | Sort-Object -Unique) -join ', '))
    }
}
W ""
W "=== public members with NO external caller (internal-only / dead) ==="
$unused = @($memberNames | Where-Object { -not $accessed.ContainsKey($_) })
if ($unused.Count -eq 0) { W "  (none)" } else { foreach ($mn in $unused) { W ("  ." + $mn) } }

# ---- emit to console + file ----
$dest = Join-Path $Root 'botbrainservice-refs.txt'
$out | Tee-Object -FilePath $dest | Out-Host
Write-Host ""
Write-Host ("Written to: {0}" -f $dest)
