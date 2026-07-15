#Requires -Version 7.0
<#
.SYNOPSIS
  Golden-file diff runner for scaffold-game-server.{sh,ps1}.
.DESCRIPTION
  PowerShell counterpart to tests/scaffold/run.sh. Same per-case schema,
  same exclusion set for sha256/tree comparison.
  Design Ref: §11.2 step 5, §8.3.
.PARAMETER Script
  Which scaffold script to drive: 'sh' (bash) or 'ps1' (PowerShell).
  Default: 'ps1'.
.PARAMETER UpdateGolden
  Regenerate sha256.txt + tree.txt for case-01-simple.
.PARAMETER Keep
  Keep working directories on success (debug).
.PARAMETER Cases
  Specific case directory names; default: all case-* dirs.
#>
[CmdletBinding()]
param(
    [ValidateSet('sh','ps1')]
    [string]$Script = 'ps1',
    [switch]$UpdateGolden,
    [switch]$Keep,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Cases
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $PSCommandPath
$RepoRoot  = Resolve-Path (Join-Path $ScriptDir '..' '..')
$ScaffoldBash = Join-Path $RepoRoot 'scripts' 'scaffold-game-server.sh'
$ScaffoldPs1  = Join-Path $RepoRoot 'scripts' 'scaffold-game-server.ps1'

# ---------------------------------------------------------------------------
# Validate selected flavor
# ---------------------------------------------------------------------------
switch ($Script) {
    'sh' {
        if (-not (Test-Path $ScaffoldBash)) {
            Write-Error "missing: $ScaffoldBash"
            exit 5
        }
        if (-not (Get-Command bash -ErrorAction SilentlyContinue)) {
            Write-Error "'bash' not on PATH (required for --Script sh)"
            exit 5
        }
    }
    'ps1' {
        if (-not (Test-Path $ScaffoldPs1)) {
            Write-Error "missing: $ScaffoldPs1"
            exit 5
        }
    }
}

# ---------------------------------------------------------------------------
# Resolve case list
# ---------------------------------------------------------------------------
if (-not $Cases -or $Cases.Count -eq 0) {
    $Cases = Get-ChildItem -Directory -Path $ScriptDir -Filter 'case-*' |
        Sort-Object Name |
        ForEach-Object { $_.Name }
}

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------
function Read-Lines {
    param([string]$Path)
    if (-not (Test-Path $Path)) { return @() }
    $lines = Get-Content -LiteralPath $Path |
        Where-Object { $_ -ne $null -and $_.Trim().Length -gt 0 -and -not $_.TrimStart().StartsWith('#') }
    return @($lines)
}

# Pure ordinal byte-order sort to match GNU `LC_ALL=C sort`.
function Sort-Ordinal {
    param([Parameter(ValueFromPipeline=$true)][string]$Item)
    begin { $list = New-Object System.Collections.Generic.List[string] }
    process { $list.Add($Item) }
    end {
        $arr = $list.ToArray()
        [Array]::Sort($arr, [System.StringComparer]::Ordinal)
        $arr
    }
}

function Test-Excluded {
    param([string]$Rel, [bool]$ExcludeSln)
    if ($Rel -eq '.git' -or $Rel.StartsWith('.git/')) { return $true }
    if ($Rel.StartsWith('bin/') -or $Rel.Contains('/bin/')) { return $true }
    if ($Rel.StartsWith('obj/') -or $Rel.Contains('/obj/')) { return $true }
    if ($Rel.EndsWith('.bak')) { return $true }
    if ($ExcludeSln -and $Rel.EndsWith('.sln')) { return $true }
    return $false
}

function Compute-Tree {
    param([string]$Root)
    $items = Get-ChildItem -Recurse -File -Force -LiteralPath $Root |
        ForEach-Object {
            [System.IO.Path]::GetRelativePath($Root, $_.FullName).Replace('\','/')
        } |
        Where-Object { -not (Test-Excluded -Rel $_ -ExcludeSln $false) } |
        ForEach-Object { "./$_" }
    @($items) | Sort-Ordinal
}

function Compute-Sha256 {
    param([string]$Root)
    $rels = Get-ChildItem -Recurse -File -Force -LiteralPath $Root |
        ForEach-Object {
            [pscustomobject]@{
                Rel  = [System.IO.Path]::GetRelativePath($Root, $_.FullName).Replace('\','/')
                Path = $_.FullName
            }
        } |
        Where-Object { -not (Test-Excluded -Rel $_.Rel -ExcludeSln $true) }

    # Sort by Rel using ordinal comparison.
    $sortedRels = @($rels.Rel) | Sort-Ordinal
    $relToPath = @{}
    foreach ($x in $rels) { $relToPath[$x.Rel] = $x.Path }

    foreach ($rel in $sortedRels) {
        $h = (Get-FileHash -LiteralPath $relToPath[$rel] -Algorithm SHA256).Hash.ToLowerInvariant()
        # Match GNU sha256sum format: "<hash><2 spaces><path>"
        "${h}  ./$rel"
    }
}

function Invoke-Scaffold {
    param(
        [string]$Flavor,
        [string[]]$ArgList,
        [string]$StdoutFile,
        [string]$StderrFile
    )
    if ($Flavor -eq 'ps1') {
        # Translate POSIX-style flags to PowerShell switch names.
        $translated = @()
        foreach ($a in $ArgList) {
            switch ($a) {
                '--force'      { $translated += '-Force';     break }
                '--no-git'     { $translated += '-NoGit';     break }
                '--skip-smoke' { $translated += '-SkipSmoke'; break }
                '--dry-run'    { $translated += '-DryRun';    break }
                '--protos-path' { $translated += '-ProtosPath'; break }
                '--help'       { $translated += '-Help';      break }
                '-h'           { $translated += '-Help';      break }
                default        { $translated += $a }
            }
        }
        $procArgs = @('-NoProfile','-File',$ScaffoldPs1) + $translated
        $proc = Start-Process -FilePath 'pwsh' -ArgumentList $procArgs `
            -RedirectStandardOutput $StdoutFile `
            -RedirectStandardError  $StderrFile `
            -PassThru -Wait -NoNewWindow
    } else {
        $procArgs = @($ScaffoldBash) + $ArgList
        $proc = Start-Process -FilePath 'bash' -ArgumentList $procArgs `
            -RedirectStandardOutput $StdoutFile `
            -RedirectStandardError  $StderrFile `
            -PassThru -Wait -NoNewWindow
    }
    return $proc.ExitCode
}

function Run-Case {
    param([string]$CaseName)
    $caseDir   = Join-Path $ScriptDir $CaseName
    $inputDir  = Join-Path $caseDir 'input'
    $expDir    = Join-Path $caseDir 'expected'

    if (-not (Test-Path $caseDir)) {
        Write-Host "  FAIL: case directory not found: $caseDir" -ForegroundColor Red
        return @{ Pass = $false; Tmp = $null }
    }
    $argsFile = Join-Path $inputDir 'args.txt'
    if (-not (Test-Path $argsFile)) {
        Write-Host "  FAIL: missing $argsFile" -ForegroundColor Red
        return @{ Pass = $false; Tmp = $null }
    }

    $tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("scaffold-$CaseName-" + [Guid]::NewGuid().ToString('N').Substring(0,8))
    New-Item -ItemType Directory -Path $tmp -Force | Out-Null
    $dest = Join-Path $tmp 'out'

    # Pre-populate dest from input/pre/
    $preDir = Join-Path $inputDir 'pre'
    if (Test-Path $preDir) {
        New-Item -ItemType Directory -Path $dest -Force | Out-Null
        Copy-Item -Recurse -Force -Path (Join-Path $preDir '*') -Destination $dest
    }

    # Build args, substituting {DEST}
    $argList = @()
    foreach ($line in (Read-Lines $argsFile)) {
        $argList += $line.Replace('{DEST}', $dest)
    }

    $stdoutFile = Join-Path $tmp 'stdout.log'
    $stderrFile = Join-Path $tmp 'stderr.log'

    $rc = Invoke-Scaffold -Flavor $Script -ArgList $argList -StdoutFile $stdoutFile -StderrFile $stderrFile

    # Assertions
    $reasons = New-Object System.Collections.Generic.List[string]

    $expectedRcLines = @(Read-Lines (Join-Path $expDir 'exit-code.txt'))
    $expectedRc = if ($expectedRcLines.Length -gt 0) { [int]$expectedRcLines[0] } else { 0 }
    if ($rc -ne $expectedRc) {
        $reasons.Add("exit code: expected $expectedRc, got $rc")
    }

    $stdoutText = if (Test-Path $stdoutFile) { Get-Content -Raw $stdoutFile } else { '' }
    $stderrText = if (Test-Path $stderrFile) { Get-Content -Raw $stderrFile } else { '' }

    foreach ($needle in (Read-Lines (Join-Path $expDir 'stdout-contains.txt'))) {
        if (-not $stdoutText.Contains($needle)) {
            $reasons.Add("stdout missing: '$needle'")
        }
    }
    foreach ($needle in (Read-Lines (Join-Path $expDir 'stderr-contains.txt'))) {
        if (-not $stderrText.Contains($needle)) {
            $reasons.Add("stderr missing: '$needle'")
        }
    }
    foreach ($rel in (Read-Lines (Join-Path $expDir 'files-present.txt'))) {
        if (-not (Test-Path (Join-Path $dest $rel))) {
            $reasons.Add("missing file: $rel")
        }
    }
    foreach ($rel in (Read-Lines (Join-Path $expDir 'files-absent.txt'))) {
        if (Test-Path (Join-Path $dest $rel)) {
            $reasons.Add("unexpected file present: $rel")
        }
    }

    # sha256 / tree (case-01 only)
    $sha256File = Join-Path $expDir 'sha256.txt'
    $treeFile   = Join-Path $expDir 'tree.txt'
    if ((Test-Path $sha256File) -and -not $UpdateGolden) {
        $expected = Get-Content -LiteralPath $sha256File
        $actual   = Compute-Sha256 -Root $dest
        $diff = Compare-Object -ReferenceObject $expected -DifferenceObject $actual -CaseSensitive
        if ($diff) {
            $reasons.Add("sha256 mismatch ($($diff.Count) lines differ)")
            $diff | Out-File -Encoding utf8 -FilePath (Join-Path $tmp 'sha256.diff')
            $script:KeepThisCase = $true
        }
    }
    if ((Test-Path $treeFile) -and -not $UpdateGolden) {
        $expected = Get-Content -LiteralPath $treeFile
        $actual   = Compute-Tree -Root $dest
        $diff = Compare-Object -ReferenceObject $expected -DifferenceObject $actual -CaseSensitive
        if ($diff) {
            $reasons.Add("tree mismatch ($($diff.Count) lines differ)")
            $diff | Out-File -Encoding utf8 -FilePath (Join-Path $tmp 'tree.diff')
            $script:KeepThisCase = $true
        }
    }

    if ($UpdateGolden -and $CaseName -eq 'case-01-simple' -and $rc -eq 0) {
        # UTF-8 NoBOM, LF
        $sha = Compute-Sha256 -Root $dest
        $tree = Compute-Tree -Root $dest
        $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
        [System.IO.File]::WriteAllText($sha256File, ($sha -join "`n") + "`n", $utf8NoBom)
        [System.IO.File]::WriteAllText($treeFile,   ($tree -join "`n") + "`n", $utf8NoBom)
        Write-Host "  updated: $sha256File" -ForegroundColor Yellow
        Write-Host "  updated: $treeFile"   -ForegroundColor Yellow
    }

    if ($reasons.Count -eq 0) {
        Write-Host "  PASS: $CaseName" -ForegroundColor Green
        if (-not $Keep) { Remove-Item -Recurse -Force -LiteralPath $tmp }
        else { Write-Host "    (kept: $tmp)" }
        return @{ Pass = $true; Tmp = $tmp }
    } else {
        Write-Host "  FAIL: $CaseName" -ForegroundColor Red
        foreach ($r in $reasons) { Write-Host "    - $r" }
        Write-Host "    tmpdir: $tmp"
        Write-Host "    stdout: $stdoutFile"
        Write-Host "    stderr: $stderrFile"
        return @{ Pass = $false; Tmp = $tmp }
    }
}

# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------
Write-Host "scaffold runner: flavor=$Script, update-golden=$UpdateGolden, cases=$($Cases.Count)"

$pass = 0
$fail = 0
$failedNames = @()
foreach ($c in $Cases) {
    $result = Run-Case -CaseName $c
    if ($result.Pass) { $pass++ } else { $fail++; $failedNames += $c }
}

Write-Host ''
Write-Host '── summary ──'
Write-Host "  pass: $pass"
Write-Host "  fail: $fail"
if ($fail -gt 0) {
    Write-Host '  failed cases:'
    foreach ($c in $failedNames) { Write-Host "    - $c" }
    exit 1
}
exit 0
