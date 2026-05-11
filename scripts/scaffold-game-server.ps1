#Requires -Version 7.0
<#
.SYNOPSIS
  Scaffold a new C# game server project from FastPortGameServerTemplate.

.DESCRIPTION
  PowerShell 7+ counterpart of scripts/scaffold-game-server.sh.
  Both scripts implement the same 12-step flow and produce byte-identical
  output (UTF-8 NoBOM, LF line endings) for cross-platform parity.

  Design Ref: docs/02-design/features/game-server-template-scaffold-scripts.design.md
  Plan Ref:   docs/01-plan/features/game-server-template-scaffold-scripts.plan.md
  PRD Ref:    docs/00-pm/game-server-template-scaffold-scripts.prd.md

  12-step flow:
    1.  parse arguments
    2.  validate project name (regex + blocked-tokens.txt)
    3.  validate destination path (-Force / idempotency)
    4.  -DryRun: print plan and exit 0
    5.  copy FastPortGameServerTemplate -> <Dest>/<NewName>
    6.  copy LibCommons -> <Dest>/LibCommons
    7.  copy LibNetworks -> <Dest>/LibNetworks
    8.  token replacement (FastPortGameServerTemplate -> <NewName>)
    9.  generate <Dest>/.gitignore + .gitattributes + README.md
    10. generate <Dest>/<NewName>.sln (dotnet new sln + sln add x3)
    11. (-NoGit false) git init + initial commit
    12. (-SkipSmoke false) dotnet build smoke

.PARAMETER NewProjectName
  PascalCase ASCII identifier, ^[A-Z][A-Za-z0-9]{0,63}$.
  Must not appear in tests/scaffold/_shared/blocked-tokens.txt.

.PARAMETER DestinationPath
  Absolute or relative target directory. Created if missing.
  Refused (exit 3) if exists and non-empty without -Force.

.PARAMETER Force
  Overwrite existing destination (irreversibly removes contents).

.PARAMETER NoGit
  Skip 'git init' + initial commit.

.PARAMETER SkipSmoke
  Skip 'dotnet build' verification.

.PARAMETER DryRun
  Print planned actions; no filesystem changes.

.EXAMPLE
  ./scaffold-game-server.ps1 MyLobbyServer ../my-lobby

.NOTES
  Exit codes:
    0  success
    2  input validation failed
    3  destination conflict
    4  smoke build failed
    5  filesystem / git / dotnet error
#>

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$NewProjectName,

    [Parameter(Position = 1)]
    [string]$DestinationPath,

    [switch]$Force,
    [switch]$NoGit,
    [switch]$SkipSmoke,
    [switch]$DryRun,
    [switch]$Help
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ---------- constants -------------------------------------------------------

$Script:TemplateToken = 'FastPortGameServerTemplate'
$Script:NameRegex     = '^[A-Z][A-Za-z0-9]{0,63}$'

# Resolve repo root from this script's location:
#   scripts/scaffold-game-server.ps1 -> repo root is one directory up.
$Script:ScriptDir       = Split-Path -Parent $PSCommandPath
$Script:RepoRoot        = Resolve-Path (Join-Path $Script:ScriptDir '..') | Select-Object -ExpandProperty Path
$Script:TemplateSrc     = Join-Path $Script:RepoRoot (Join-Path 'template-projects' $Script:TemplateToken)
# Design Ref: template-contracts-scaffold-fix §2.1 — Contracts sub-project.
$Script:ContractsSrc    = Join-Path $Script:RepoRoot (Join-Path 'template-projects' "$Script:TemplateToken.Contracts")
$Script:LibCommonsSrc   = Join-Path $Script:RepoRoot 'LibCommons'
$Script:LibNetworksSrc  = Join-Path $Script:RepoRoot 'LibNetworks'
$Script:BlockedTokensFile = Join-Path $Script:RepoRoot 'tests/scaffold/_shared/blocked-tokens.txt'

# Text file extensions subject to in-place token replacement (step 8).
$Script:TextExtensions = @(
    '.cs', '.csproj', '.proto', '.json', '.md', '.sln',
    '.yml', '.yaml', '.xml', '.gitignore', '.gitattributes'
)

# Patterns to exclude when copying source trees (step 5-7).
$Script:ExcludeNames = @('bin', 'obj')
$Script:ExcludeExtensions = @('.user')

# UTF-8 encoding without BOM — single source for all file writes (parity).
$Script:Utf8NoBom = [System.Text.UTF8Encoding]::new($false)

# ---------- helpers ---------------------------------------------------------

function Write-Log  { Write-Host $args[0] }
function Write-Err  { [Console]::Error.WriteLine("error: $($args[0])") }
function Write-Hint { [Console]::Error.WriteLine("hint:  $($args[0])") }

function Show-Usage {
@'
Usage: scaffold-game-server.ps1 <NewProjectName> <DestinationPath> [OPTIONS]

Positional:
  NewProjectName     PascalCase ASCII identifier, ^[A-Z][A-Za-z0-9]{0,63}$
                     Must not appear in tests/scaffold/_shared/blocked-tokens.txt.
  DestinationPath    Absolute or relative target directory. Created if missing.
                     Refused (exit 3) if exists and non-empty without -Force.

Options:
  -Force             Overwrite existing destination (irreversibly removes contents).
  -NoGit             Skip 'git init' + initial commit.
  -SkipSmoke         Skip 'dotnet build' verification.
  -DryRun            Print planned actions; no filesystem changes.
  -Help              Print usage and exit 0.

Exit codes:
  0  success
  2  input validation failed
  3  destination conflict
  4  smoke build failed
  5  filesystem / git / dotnet error
'@
}

function Read-BlockedTokens {
    if (-not (Test-Path -LiteralPath $Script:BlockedTokensFile -PathType Leaf)) {
        Write-Err "blocked-tokens.txt not found at $Script:BlockedTokensFile"
        Write-Hint "this scaffold script must run from inside a FastPortSharp clone"
        exit 5
    }
    $tokens = @()
    foreach ($line in Get-Content -LiteralPath $Script:BlockedTokensFile -Encoding utf8) {
        # Strip inline comments after '#', trim whitespace, drop empty lines.
        $stripped = ($line -replace '\s*#.*$', '').Trim()
        if ($stripped.Length -gt 0) { $tokens += $stripped }
    }
    , $tokens
}

function Test-BlockedToken {
    param([string]$Name)
    $tokens = Read-BlockedTokens
    # `-ccontains` (case-sensitive). PowerShell's default `-contains` is
    # case-insensitive, which would over-block names that only differ by case.
    if ($tokens -ccontains $Name) {
        Write-Err  "name `"$Name`" is in the blocked tokens list."
        Write-Hint "this name conflicts with an internal folder/namespace token."
        $relative = $Script:BlockedTokensFile.Substring($Script:RepoRoot.Length).TrimStart([char]'/', [char]'\')
        Write-Hint "see $relative for the full list."
        exit 2
    }
}

function Write-FileUtf8NoBom {
    param([string]$Path, [string]$Content)
    # POSIX convention: text files end with a newline. Bash heredocs preserve
    # this naturally; PowerShell here-strings do not. Normalise here so cross-
    # platform parity holds (both scripts emit byte-identical files).
    if (-not $Content.EndsWith("`n")) { $Content += "`n" }
    [System.IO.File]::WriteAllText($Path, $Content, $Script:Utf8NoBom)
}

function Copy-TreeFiltered {
    param(
        [string]$Src,
        [string]$Dest
    )
    if (-not (Test-Path -LiteralPath $Dest)) {
        New-Item -ItemType Directory -Path $Dest -Force | Out-Null
    }
    # Walk the source tree; recreate matching subdirectories and copy file
    # contents verbatim. Excludes bin/, obj/ and *.user — same as the bash
    # tar-based copy.
    $srcLength = $Src.Length
    Get-ChildItem -LiteralPath $Src -Recurse -Force | ForEach-Object {
        $item     = $_
        $relative = $item.FullName.Substring($srcLength).TrimStart([char]'/', [char]'\')
        if ($relative.Length -eq 0) { return }

        # Skip excluded directories (and anything underneath them).
        $segments = $relative -split '[\\/]+'
        foreach ($seg in $segments) {
            if ($Script:ExcludeNames -contains $seg) { return }
        }

        if ($item.PSIsContainer) {
            New-Item -ItemType Directory -Path (Join-Path $Dest $relative) -Force | Out-Null
            return
        }

        # Skip excluded extensions.
        if ($Script:ExcludeExtensions -contains $item.Extension) { return }

        $target = Join-Path $Dest $relative
        $targetDir = Split-Path -Parent $target
        if ($targetDir -and -not (Test-Path -LiteralPath $targetDir)) {
            New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
        }
        Copy-Item -LiteralPath $item.FullName -Destination $target -Force
    }
}

# ---------- step 1: parse arguments -----------------------------------------

if ($Help) { Show-Usage; exit 0 }

if (-not $NewProjectName -or -not $DestinationPath) {
    Write-Err "both <NewProjectName> and <DestinationPath> are required."
    Show-Usage
    exit 2
}

# ---------- step 2: validate name -------------------------------------------

function Test-NewName {
    # `-cnotmatch` (case-sensitive) — PowerShell's default `-notmatch` is
    # case-insensitive, which would let `myGame` slip past `^[A-Z]...`.
    if ($NewProjectName -cnotmatch $Script:NameRegex) {
        Write-Err  "name `"$NewProjectName`" does not match required pattern."
        Write-Hint "must match $Script:NameRegex (PascalCase ASCII, 1-64 chars, starts uppercase)"
        exit 2
    }
    Test-BlockedToken -Name $NewProjectName
}

# ---------- step 3: validate destination ------------------------------------

function Resolve-Destination {
    if (Test-Path -LiteralPath $DestinationPath) {
        if (-not (Test-Path -LiteralPath $DestinationPath -PathType Container)) {
            Write-Err "destination `"$DestinationPath`" exists and is not a directory."
            exit 3
        }
        $children = @(Get-ChildItem -LiteralPath $DestinationPath -Force)
        if ($children.Count -gt 0) {
            if (-not $Force) {
                Write-Err  "destination `"$DestinationPath`" already exists and is not empty."
                Write-Hint "use -Force to overwrite (irreversible), or pick a different path."
                exit 3
            }
            if (-not $DryRun) {
                # -Force: clear directory contents, keep the directory itself.
                Get-ChildItem -LiteralPath $DestinationPath -Force | ForEach-Object {
                    Remove-Item -LiteralPath $_.FullName -Recurse -Force
                }
            }
        }
    }
    else {
        if (-not $DryRun) {
            New-Item -ItemType Directory -Path $DestinationPath -Force | Out-Null
        }
    }
    if (-not $DryRun) {
        $Script:DestPathResolved = (Resolve-Path -LiteralPath $DestinationPath).Path
    }
    else {
        $Script:DestPathResolved = $DestinationPath
    }
}

# ---------- step 4: dry-run -------------------------------------------------

function Show-DryRunPlan {
    Write-Log "[DRY-RUN] would scaffold:"
    Write-Log "[DRY-RUN]   NewName       : $NewProjectName"
    Write-Log "[DRY-RUN]   Destination   : $Script:DestPathResolved"
    Write-Log "[DRY-RUN]   -Force        : $(if ($Force)     { 'on' } else { 'off' })"
    Write-Log "[DRY-RUN]   -NoGit        : $(if ($NoGit)     { 'on' } else { 'off' })"
    Write-Log "[DRY-RUN]   -SkipSmoke    : $(if ($SkipSmoke) { 'on' } else { 'off' })"
    Write-Log "[DRY-RUN] would copy:"
    Write-Log "[DRY-RUN]   $Script:TemplateSrc -> $(Join-Path $Script:DestPathResolved $NewProjectName)"
    Write-Log "[DRY-RUN]   $Script:ContractsSrc -> $(Join-Path $Script:DestPathResolved "$NewProjectName.Contracts")"
    Write-Log "[DRY-RUN]   $Script:LibCommonsSrc -> $(Join-Path $Script:DestPathResolved 'LibCommons')"
    Write-Log "[DRY-RUN]   $Script:LibNetworksSrc -> $(Join-Path $Script:DestPathResolved 'LibNetworks')"
    Write-Log "[DRY-RUN] would replace token `"$Script:TemplateToken`" -> `"$NewProjectName`" in:"
    Write-Log "[DRY-RUN]   text files (extensions: $($Script:TextExtensions -join ' ')) under <Dest>/$NewProjectName and <Dest>/$NewProjectName.Contracts"
    Write-Log "[DRY-RUN] would generate:"
    Write-Log "[DRY-RUN]   $(Join-Path $Script:DestPathResolved '.gitignore')"
    Write-Log "[DRY-RUN]   $(Join-Path $Script:DestPathResolved '.gitattributes')"
    Write-Log "[DRY-RUN]   $(Join-Path $Script:DestPathResolved 'README.md')"
    Write-Log "[DRY-RUN]   $(Join-Path $Script:DestPathResolved "$NewProjectName.sln") (4 projects)"
    if (-not $NoGit) {
        Write-Log "[DRY-RUN]   .git + initial commit"
    }
    if (-not $SkipSmoke) {
        Write-Log "[DRY-RUN] would run: dotnet build $(Join-Path $Script:DestPathResolved "$NewProjectName.sln") -c Release"
    }
}

# ---------- step 5-7: copy --------------------------------------------------

function Copy-Template {
    Copy-TreeFiltered -Src $Script:TemplateSrc -Dest (Join-Path $Script:DestPathResolved $Script:TemplateToken)
}
# Design Ref: template-contracts-scaffold-fix §2.1 — Contracts sub-project.
function Copy-Contracts {
    Copy-TreeFiltered -Src $Script:ContractsSrc -Dest (Join-Path $Script:DestPathResolved "$Script:TemplateToken.Contracts")
}
function Copy-LibCommons {
    Copy-TreeFiltered -Src $Script:LibCommonsSrc -Dest (Join-Path $Script:DestPathResolved 'LibCommons')
}
function Copy-LibNetworks {
    Copy-TreeFiltered -Src $Script:LibNetworksSrc -Dest (Join-Path $Script:DestPathResolved 'LibNetworks')
}

# ---------- step 8: token replacement ---------------------------------------

function Update-Tokens {
    # Design Ref: template-contracts-scaffold-fix §2.1 —
    # Both Template and Contracts subtrees need token replacement.
    $subtrees = @(
        Join-Path $Script:DestPathResolved $Script:TemplateToken
        Join-Path $Script:DestPathResolved "$Script:TemplateToken.Contracts"
    )
    $count = 0

    foreach ($subtree in $subtrees) {
        Get-ChildItem -LiteralPath $subtree -Recurse -File -Force | ForEach-Object {
            $file = $_
            if ($Script:TextExtensions -notcontains $file.Extension) { return }

            # Read with explicit UTF-8 to avoid PowerShell auto-detecting other
            # encodings; preserves whatever line endings the source has.
            $content = [System.IO.File]::ReadAllText($file.FullName, $Script:Utf8NoBom)
            if (-not $content.Contains($Script:TemplateToken)) { return }

            $newContent = $content.Replace($Script:TemplateToken, $NewProjectName)
            Write-FileUtf8NoBom -Path $file.FullName -Content $newContent
            $count++
        }
    }

    # Rename the Template subtree directory + csproj.
    $newSubtree = Join-Path $Script:DestPathResolved $NewProjectName
    Rename-Item -LiteralPath (Join-Path $Script:DestPathResolved $Script:TemplateToken) -NewName $NewProjectName
    Rename-Item `
        -LiteralPath (Join-Path $newSubtree "$Script:TemplateToken.csproj") `
        -NewName "$NewProjectName.csproj"

    # Design Ref: §2.1 — Rename the Contracts subtree directory + csproj.
    $newContractsSubtree = Join-Path $Script:DestPathResolved "$NewProjectName.Contracts"
    Rename-Item `
        -LiteralPath (Join-Path $Script:DestPathResolved "$Script:TemplateToken.Contracts") `
        -NewName "$NewProjectName.Contracts"
    Rename-Item `
        -LiteralPath (Join-Path $newContractsSubtree "$Script:TemplateToken.Contracts.csproj") `
        -NewName "$NewProjectName.Contracts.csproj"

    # Design Ref: §2.1 — Source csproj has `..\..\LibCommons` (template-projects/
    # depth) but scaffold output is flat, so adjust to `..\LibCommons`.
    $csprojFiles = @(
        Join-Path $newSubtree "$NewProjectName.csproj"
        Join-Path $newContractsSubtree "$NewProjectName.Contracts.csproj"
    )
    foreach ($cf in $csprojFiles) {
        if (-not (Test-Path -LiteralPath $cf)) { continue }
        $c = [System.IO.File]::ReadAllText($cf, $Script:Utf8NoBom)
        $c = $c.Replace('..\..\LibCommons', '..\LibCommons')
        $c = $c.Replace('..\..\LibNetworks', '..\LibNetworks')
        Write-FileUtf8NoBom -Path $cf -Content $c
    }

    Write-Log "        replaced token in $count files."
}

# ---------- step 9: aux files (.gitignore / .gitattributes / README.md) -----

function New-GitIgnore {
    $body = @'
# Generated by scaffold-game-server (FastPortSharp template).

# .NET build artefacts
bin/
obj/
.vs/

# Logs and runtime artefacts
*.log

# IDE / OS
*.user
*.suo
.DS_Store
**/.DS_Store
'@
    Write-FileUtf8NoBom -Path (Join-Path $Script:DestPathResolved '.gitignore') -Content $body
}

function New-GitAttributes {
    $body = @'
# Generated by scaffold-game-server (FastPortSharp template).
# Force LF line endings + UTF-8 for source / config to keep
# cross-platform parity with upstream FastPortSharp.

* text=auto eol=lf

*.cs     text eol=lf
*.csproj text eol=lf
*.proto  text eol=lf
*.json   text eol=lf
*.md     text eol=lf
*.yml    text eol=lf
*.yaml   text eol=lf

# Visual Studio expects CRLF for .sln (dotnet new sln also emits CRLF).
*.sln    text eol=crlf

*.sh     text eol=lf
*.ps1    text eol=lf

*.png    binary
*.jpg    binary
*.jpeg   binary
*.gif    binary
*.ico    binary
'@
    Write-FileUtf8NoBom -Path (Join-Path $Script:DestPathResolved '.gitattributes') -Content $body
}

function New-Readme {
    $body = @"
# $NewProjectName

A game server scaffolded from the FastPortSharp template
(<https://github.com/boinred/FastPortSharp>).

## Build & Run

``````bash
dotnet build $NewProjectName.sln -c Release
dotnet run --project $NewProjectName -c Release
``````

The server listens on ``0.0.0.0:7777`` by default. Edit
``$NewProjectName/appsettings.json`` to change.

## Layout

- ``$NewProjectName/``   — your game server (start here)
- ``LibCommons/``   — engine: buffers, packet primitives (read-only baseline)
- ``LibNetworks/``  — engine: TCP listener / session (read-only baseline)

## Adding packets

See ``$NewProjectName/README.md`` and ``$NewProjectName/QUICKSTART.ko.md`` for the
template's packet/handler customisation guide.

## License

MIT (inherits from the upstream FastPortSharp template).
"@
    Write-FileUtf8NoBom -Path (Join-Path $Script:DestPathResolved 'README.md') -Content $body
}

# ---------- step 10: generate sln -------------------------------------------

function New-SolutionFile {
    Push-Location -LiteralPath $Script:DestPathResolved
    try {
        # .NET 10's `dotnet new sln` defaults to the newer .slnx (XML) format.
        # Force the classic .sln format so existing IDE tooling and CI scripts
        # that match `*.sln` continue to work.
        dotnet new sln --format sln -n $NewProjectName | Out-Null
        if ($LASTEXITCODE -ne 0) { exit 5 }
        dotnet sln "$NewProjectName.sln" add (Join-Path $NewProjectName "$NewProjectName.csproj") | Out-Null
        if ($LASTEXITCODE -ne 0) { exit 5 }
        # Design Ref: template-contracts-scaffold-fix §2.1 — Contracts sub-project.
        dotnet sln "$NewProjectName.sln" add (Join-Path "$NewProjectName.Contracts" "$NewProjectName.Contracts.csproj") | Out-Null
        if ($LASTEXITCODE -ne 0) { exit 5 }
        dotnet sln "$NewProjectName.sln" add (Join-Path 'LibCommons'  'LibCommons.csproj')  | Out-Null
        if ($LASTEXITCODE -ne 0) { exit 5 }
        dotnet sln "$NewProjectName.sln" add (Join-Path 'LibNetworks' 'LibNetworks.csproj') | Out-Null
        if ($LASTEXITCODE -ne 0) { exit 5 }
    }
    finally {
        Pop-Location
    }
}

# ---------- step 11: git init -----------------------------------------------

function Invoke-GitInit {
    Push-Location -LiteralPath $Script:DestPathResolved
    try {
        git init -q -b main
        if ($LASTEXITCODE -ne 0) { exit 5 }
        git add . | Out-Null
        if ($LASTEXITCODE -ne 0) { exit 5 }
        git -c user.name='scaffold-game-server' -c user.email='scaffold@local' `
            commit -q -m "Initial scaffold from $Script:TemplateToken"
        if ($LASTEXITCODE -ne 0) { exit 5 }
    }
    finally {
        Pop-Location
    }
}

# ---------- step 12: smoke build --------------------------------------------

function Invoke-SmokeBuild {
    $sln = Join-Path $Script:DestPathResolved "$NewProjectName.sln"
    dotnet build $sln -c Release --nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Err  "'dotnet build $sln -c Release' failed."
        Write-Hint "this usually means a token was missed during replacement."
        Write-Hint "run with -DryRun to inspect, or file an issue."
        exit 4
    }
}

# ---------- main ------------------------------------------------------------

function Invoke-Main {
    Write-Log "[1/12]  Parsing arguments...                      OK (NewName=$NewProjectName, Dest=$DestinationPath)"

    Write-Log "[2/12]  Validating project name..."
    Test-NewName
    Write-Log "        OK"

    Write-Log "[3/12]  Resolving destination..."
    Resolve-Destination
    Write-Log "        OK"

    if ($DryRun) {
        Write-Log "[4/12]  Dry-run mode active. Filesystem unchanged."
        Show-DryRunPlan
        exit 0
    }

    Write-Log "[5/12]  Copying $Script:TemplateToken + $Script:TemplateToken.Contracts..."
    Copy-Template
    Copy-Contracts
    Write-Log "        OK"

    Write-Log "[6/12]  Copying LibCommons..."
    Copy-LibCommons
    Write-Log "        OK"

    Write-Log "[7/12]  Copying LibNetworks..."
    Copy-LibNetworks
    Write-Log "        OK"

    Write-Log "[8/12]  Replacing tokens ($Script:TemplateToken -> $NewProjectName)..."
    Update-Tokens
    Write-Log "        OK"

    Write-Log "[9/12]  Generating .gitignore, .gitattributes, README.md..."
    New-GitIgnore
    New-GitAttributes
    New-Readme
    Write-Log "        OK"

    Write-Log "[10/12] Creating $NewProjectName.sln..."
    New-SolutionFile
    Write-Log "        OK"

    if (-not $NoGit) {
        Write-Log "[11/12] git init + initial commit..."
        Invoke-GitInit
        Write-Log "        OK"
    }
    else {
        Write-Log "[11/12] git init skipped (-NoGit)."
    }

    if (-not $SkipSmoke) {
        Write-Log "[12/12] dotnet build smoke..."
        Invoke-SmokeBuild
        Write-Log "        OK"
    }
    else {
        Write-Log "[12/12] smoke build skipped (-SkipSmoke)."
    }

    Write-Log ""
    Write-Log "Done."
    Write-Log ""
    Write-Log "Next steps:"
    Write-Log "  cd $Script:DestPathResolved"
    Write-Log "  dotnet run --project $NewProjectName -c Release"
}

Invoke-Main
