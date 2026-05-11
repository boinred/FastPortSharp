# scripts/

Cross-platform scaffolding utilities for the FastPortSharp game-server template.

## scaffold-game-server

Bootstraps a new self-contained game server project from
`template-projects/FastPortGameServerTemplate/` +
`template-projects/FastPortGameServerTemplate.Contracts/` (shared proto +
PacketIds) + the engine (`LibCommons/` + `LibNetworks/`).

Two scripts, identical behaviour, byte-identical output:

| Platform | Script | Requires |
|----------|--------|----------|
| Linux / macOS | `scripts/scaffold-game-server.sh` | Bash 3.2+ |
| Windows / cross-platform | `scripts/scaffold-game-server.ps1` | PowerShell 7+ |

Both scripts also need `dotnet` 10+ on `PATH` (used to generate the `.sln`),
and `git` if you don't pass `--no-git`.

### Usage

```bash
# bash / zsh
scripts/scaffold-game-server.sh   <NewProjectName> <DestinationPath> [OPTIONS]

# PowerShell 7+
pwsh -File scripts/scaffold-game-server.ps1 <NewProjectName> <DestinationPath> [OPTIONS]
```

### Arguments

| Positional | Description |
|------------|-------------|
| `NewProjectName` | Must match `^[A-Z][A-Za-z0-9]{0,63}$` (PascalCase ASCII) and not be in `tests/scaffold/_shared/blocked-tokens.txt`. |
| `DestinationPath` | Absolute or relative target directory. Refused if it already exists and is not empty (use `--force` to overwrite). |

### Options

| Bash (POSIX) | PowerShell | Description |
|------|------------|-------------|
| `--force` | `-Force` | Overwrite destination (irreversibly removes its contents). |
| `--no-git` | `-NoGit` | Skip `git init` + initial commit. |
| `--dry-run` | `-DryRun` | Print planned actions; no filesystem changes. |
| `--skip-smoke` | `-SkipSmoke` | Skip the post-scaffold `dotnet build` smoke test. |
| `-h`, `--help` | `-Help` | Print usage. |

### Exit codes

| Code | Cause |
|------|-------|
| `0` | Success. |
| `2` | Input validation failed (bad name / blocked / missing args). |
| `3` | Destination already exists and is not empty (and `--force` not given). |
| `4` | Smoke build failed. The destination is left in place for inspection. |
| `5` | I/O / `git` / `dotnet` error. |

### Quick example

```bash
scripts/scaffold-game-server.sh MyLobbyServer ../my-lobby

# In another terminal: connect with the sample client to see echo round-trip.
cd ../my-lobby
dotnet run --project MyLobbyServer -c Release
```

The new project is self-contained:
- `<NewName>/` — your game server (start here)
- `<NewName>.Contracts/` — proto contracts + PacketIds (shared with future
  consumers, e.g. matching sample client)
- `LibCommons/` — engine: buffers, packet primitives
- `LibNetworks/` — engine: TCP listener / session
- `<NewName>.sln` — solution that ties the four projects together
- `.gitignore`, `.gitattributes`, `README.md` — repo hygiene

### What gets renamed

The scripts substitute the literal token `FastPortGameServerTemplate`
with `<NewProjectName>` in:
- folder names (`<NewName>/`, `<NewName>.Contracts/`)
- file names (the `.csproj` pair)
- file contents (`.cs`, `.csproj`, `.proto`, `.json`, `.md`)
- the generated `.sln` references (4 projects)
- the C# namespace

The scripts also adjust `..\..\LibCommons` / `..\..\LibNetworks` relative
paths in the copied csproj files to `..\LibCommons` / `..\LibNetworks`,
since the source `template-projects/` is at depth 2 but the scaffold
output is flat.

The token is a unique 26-char compound so collateral matches are essentially
impossible (there is also a guard list in
`tests/scaffold/_shared/blocked-tokens.txt` — naming your project
`Application`, `LibCommons`, etc. is rejected at validation time).

## Troubleshooting

### "blocked tokens list" — exit 2
Your chosen name collides with an internal folder or sibling project name.
Pick a different name; see `tests/scaffold/_shared/blocked-tokens.txt`.

### "does not match required pattern" — exit 2
Names must start with an uppercase ASCII letter and contain only
`[A-Za-z0-9]`, max 64 chars. Hyphens, dots, spaces, non-ASCII are rejected.

### "destination already exists and is not empty" — exit 3
Add `--force` (bash) or `-Force` (PowerShell) — the script will delete the
destination's contents and then scaffold fresh. There is no merge mode.

### "dotnet build smoke" failed — exit 4
This usually indicates a token was missed during replacement (a real bug —
please file an issue with `--dry-run` output). The destination is left in
place so you can inspect the partial result.

### Smoke build is slow / I just want files
Use `--skip-smoke` (bash) or `-SkipSmoke` (PowerShell). The script still
generates the `.sln` (which needs `dotnet new sln`) but skips the build.

### Windows: "execution of scripts is disabled on this system"
Run the `.ps1` via `pwsh -File` (as shown in Usage above) which bypasses
the per-user execution policy, or set the policy explicitly:
```powershell
Set-ExecutionPolicy -Scope CurrentUser RemoteSigned
```

## Verifying the scripts (golden-file diff)

Both scripts are exercised by `tests/scaffold/run.{sh,ps1}` against 7
golden-file cases. To run them locally:

```bash
# Drive .sh scaffold with bash runner (default)
tests/scaffold/run.sh

# Drive .ps1 scaffold with bash runner
tests/scaffold/run.sh --script ps1

# Drive .ps1 scaffold with PS runner
pwsh -NoProfile -File tests/scaffold/run.ps1

# Drive .sh scaffold with PS runner
pwsh -NoProfile -File tests/scaffold/run.ps1 -Script sh
```

All four combinations should report **7 PASS / 0 FAIL**. Cross-OS
byte-identical parity is verified by `.github/workflows/scaffold.yml`.

To regenerate the case-01 sha256/tree baseline after intentional template
changes:
```bash
tests/scaffold/run.sh --update-golden case-01-simple
```
