#!/usr/bin/env bash
#
# scaffold-game-server.sh
#
# Scaffold a new C# game server project from FastPortGameServerTemplate.
#
# Design Ref: docs/02-design/features/game-server-template-scaffold-scripts.design.md
# Plan Ref:   docs/01-plan/features/game-server-template-scaffold-scripts.plan.md
# PRD Ref:    docs/00-pm/game-server-template-scaffold-scripts.prd.md
#
# 12-step flow:
#   1.  parse arguments
#   2.  validate project name (regex + blocked-tokens.txt)
#   3.  validate destination path (--force / idempotency)
#   4.  --dry-run: print plan and exit 0
#   5.  copy FastPortGameServerTemplate -> <dest>/<NewName>
#   6.  copy LibCommons -> <dest>/LibCommons
#   7.  copy LibNetworks -> <dest>/LibNetworks
#   8.  token replacement (FastPortGameServerTemplate -> <NewName>)
#   9.  generate <dest>/.gitignore + .gitattributes + README.md
#   10. generate <dest>/<NewName>.sln (dotnet new sln + sln add x3)
#   11. (--no-git false) git init + initial commit
#   12. (--skip-smoke false) dotnet build smoke
#
# Compatibility: macOS bash 3.2+ (no `mapfile`/`readarray`/associative arrays).
#                BSD sed and GNU sed both supported (via `sed -i.bak`).
#
# Exit codes:
#   0 success
#   2 input validation failed (bad name / bad path)
#   3 destination conflict (use --force)
#   4 smoke build failed
#   5 IO / git / dotnet error

set -euo pipefail

# ---------- constants -------------------------------------------------------

readonly SCRIPT_NAME="scaffold-game-server.sh"
readonly TEMPLATE_TOKEN="FastPortGameServerTemplate"
readonly NAME_REGEX='^[A-Z][A-Za-z0-9]{0,63}$'

# Resolve repo root from this script's location:
#   scripts/scaffold-game-server.sh -> repo root is one directory up.
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
readonly SCRIPT_DIR
readonly REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
readonly TEMPLATE_SRC="${REPO_ROOT}/template-projects/${TEMPLATE_TOKEN}"
# Design Ref: template-contracts-scaffold-fix §2.1 — Contracts sub-project.
readonly CONTRACTS_SRC="${REPO_ROOT}/template-projects/${TEMPLATE_TOKEN}.Contracts"
readonly LIBCOMMONS_SRC="${REPO_ROOT}/LibCommons"
readonly LIBNETWORKS_SRC="${REPO_ROOT}/LibNetworks"
readonly BLOCKED_TOKENS_FILE="${REPO_ROOT}/tests/scaffold/_shared/blocked-tokens.txt"

# Text file extensions subject to in-place token replacement (step 8).
readonly TEXT_EXTS="cs csproj proto json md sln yml yaml xml gitignore gitattributes"

# ---------- helpers ---------------------------------------------------------

log()  { printf '%s\n' "$*"; }
err()  { printf 'error: %s\n' "$*" >&2; }
hint() { printf 'hint:  %s\n' "$*" >&2; }

usage() {
  cat <<'USAGE'
Usage: scaffold-game-server.sh <NewProjectName> <DestinationPath> [OPTIONS]

Positional:
  NewProjectName     PascalCase ASCII identifier, ^[A-Z][A-Za-z0-9]{0,63}$
                     Must not appear in tests/scaffold/_shared/blocked-tokens.txt.
  DestinationPath    Absolute or relative target directory. Created if missing.
                     Refused (exit 3) if exists and non-empty without --force.

Options:
  --force            Overwrite existing destination (irreversibly removes contents).
  --no-git           Skip 'git init' + initial commit.
  --skip-smoke       Skip 'dotnet build' verification.
  --dry-run          Print planned actions; no filesystem changes.
  -h, --help         Print usage and exit 0.

Exit codes:
  0  success
  2  input validation failed
  3  destination conflict
  4  smoke build failed
  5  filesystem / git / dotnet error
USAGE
}

# Print blocked tokens (one per line), stripping comments and blanks.
read_blocked_tokens() {
  if [ ! -f "${BLOCKED_TOKENS_FILE}" ]; then
    err "blocked-tokens.txt not found at ${BLOCKED_TOKENS_FILE}"
    hint "this scaffold script must run from inside a FastPortSharp clone"
    exit 5
  fi
  # Strip inline comments after `#`, trim whitespace, drop empty lines.
  # bash 3.2 compatible: pipe through sed/grep instead of using readarray.
  sed -e 's/[[:space:]]*#.*$//' -e 's/^[[:space:]]*//' -e 's/[[:space:]]*$//' \
    "${BLOCKED_TOKENS_FILE}" | grep -v '^$' || true
}

# Check whether $1 (the candidate name) appears in the blocked tokens list.
# Exits 2 with a friendly message if it does.
check_blocked_token() {
  local name="$1"
  local token
  while IFS= read -r token; do
    if [ "${name}" = "${token}" ]; then
      err "name \"${name}\" is in the blocked tokens list."
      hint "this name conflicts with an internal folder/namespace token."
      hint "see ${BLOCKED_TOKENS_FILE#${REPO_ROOT}/} for the full list."
      exit 2
    fi
  done <<EOF
$(read_blocked_tokens)
EOF
}

# Apply in-place token replacement to a single text file.
# Compatible with BSD sed (macOS) and GNU sed: -i.bak then remove backup.
replace_in_file() {
  local file="$1"
  local from="$2"
  local to="$3"
  sed -i.bak "s/${from}/${to}/g" "${file}"
  rm -f "${file}.bak"
}

# Copy a source tree to a destination, excluding bin/, obj/, *.user.
# Uses tar pipe to preserve mode but skip excluded patterns portably.
copy_tree() {
  local src="$1"
  local dest="$2"
  mkdir -p "${dest}"
  # Exclude build outputs and IDE-only files. tar is POSIX and present on
  # macOS / Linux. Trailing slash on src is intentional (copy contents).
  ( cd "${src}" && tar --exclude='bin' --exclude='obj' --exclude='*.user' \
      -cf - . ) | ( cd "${dest}" && tar -xf - )
}

# ---------- step 1: parse arguments -----------------------------------------

NEW_NAME=""
DEST_PATH=""
OPT_FORCE=0
OPT_NO_GIT=0
OPT_SKIP_SMOKE=0
OPT_DRY_RUN=0

parse_args() {
  while [ $# -gt 0 ]; do
    case "$1" in
      -h|--help)       usage; exit 0 ;;
      --force)         OPT_FORCE=1; shift ;;
      --no-git)        OPT_NO_GIT=1; shift ;;
      --skip-smoke)    OPT_SKIP_SMOKE=1; shift ;;
      --dry-run)       OPT_DRY_RUN=1; shift ;;
      --)              shift; break ;;
      -*)
        err "unknown option: $1"
        hint "see -h for usage"
        exit 2
        ;;
      *)
        if [ -z "${NEW_NAME}" ]; then
          NEW_NAME="$1"
        elif [ -z "${DEST_PATH}" ]; then
          DEST_PATH="$1"
        else
          err "unexpected positional argument: $1"
          hint "see -h for usage"
          exit 2
        fi
        shift
        ;;
    esac
  done

  if [ -z "${NEW_NAME}" ] || [ -z "${DEST_PATH}" ]; then
    err "both <NewProjectName> and <DestinationPath> are required."
    usage
    exit 2
  fi
}

# ---------- step 2: validate name -------------------------------------------

validate_name() {
  if ! printf '%s' "${NEW_NAME}" | grep -E -q "${NAME_REGEX}"; then
    err "name \"${NEW_NAME}\" does not match required pattern."
    hint "must match ${NAME_REGEX} (PascalCase ASCII, 1-64 chars, starts uppercase)"
    exit 2
  fi
  check_blocked_token "${NEW_NAME}"
}

# ---------- step 3: validate destination ------------------------------------

validate_dest() {
  # Note: bash 3.2 has no `realpath`/`readlink -f` portably. We resolve via
  # mkdir + cd to canonicalise. If the path doesn't exist yet we create it
  # (empty) so subsequent steps can write into it.
  if [ -e "${DEST_PATH}" ]; then
    if [ ! -d "${DEST_PATH}" ]; then
      err "destination \"${DEST_PATH}\" exists and is not a directory."
      exit 3
    fi
    # Check whether the directory is non-empty.
    if [ -n "$(ls -A "${DEST_PATH}" 2>/dev/null || true)" ]; then
      if [ "${OPT_FORCE}" -ne 1 ]; then
        err "destination \"${DEST_PATH}\" already exists and is not empty."
        hint "use --force to overwrite (irreversible), or pick a different path."
        exit 3
      fi
      if [ "${OPT_DRY_RUN}" -ne 1 ]; then
        # --force: clear the directory contents (keep the directory itself).
        # Avoid 'rm -rf "$DEST_PATH"' if it's a symlink etc.; clear contents only.
        find "${DEST_PATH}" -mindepth 1 -maxdepth 1 -exec rm -rf {} +
      fi
    fi
  else
    if [ "${OPT_DRY_RUN}" -ne 1 ]; then
      mkdir -p "${DEST_PATH}"
    fi
  fi
  if [ "${OPT_DRY_RUN}" -ne 1 ]; then
    DEST_PATH="$(cd "${DEST_PATH}" && pwd)"
  fi
}

# ---------- step 4: dry-run -------------------------------------------------

dry_run_plan() {
  log "[DRY-RUN] would scaffold:"
  log "[DRY-RUN]   NewName       : ${NEW_NAME}"
  log "[DRY-RUN]   Destination   : ${DEST_PATH}"
  log "[DRY-RUN]   --force       : $([ "${OPT_FORCE}"      -eq 1 ] && echo on || echo off)"
  log "[DRY-RUN]   --no-git      : $([ "${OPT_NO_GIT}"     -eq 1 ] && echo on || echo off)"
  log "[DRY-RUN]   --skip-smoke  : $([ "${OPT_SKIP_SMOKE}" -eq 1 ] && echo on || echo off)"
  log "[DRY-RUN] would copy:"
  log "[DRY-RUN]   ${TEMPLATE_SRC}    -> ${DEST_PATH}/${NEW_NAME}"
  log "[DRY-RUN]   ${CONTRACTS_SRC}   -> ${DEST_PATH}/${NEW_NAME}.Contracts"
  log "[DRY-RUN]   ${LIBCOMMONS_SRC}  -> ${DEST_PATH}/LibCommons"
  log "[DRY-RUN]   ${LIBNETWORKS_SRC} -> ${DEST_PATH}/LibNetworks"
  log "[DRY-RUN] would replace token \"${TEMPLATE_TOKEN}\" -> \"${NEW_NAME}\" in:"
  log "[DRY-RUN]   text files (extensions: ${TEXT_EXTS}) under <dest>/${NEW_NAME} and <dest>/${NEW_NAME}.Contracts"
  log "[DRY-RUN] would generate:"
  log "[DRY-RUN]   ${DEST_PATH}/.gitignore"
  log "[DRY-RUN]   ${DEST_PATH}/.gitattributes"
  log "[DRY-RUN]   ${DEST_PATH}/README.md"
  log "[DRY-RUN]   ${DEST_PATH}/${NEW_NAME}.sln (4 projects)"
  if [ "${OPT_NO_GIT}" -ne 1 ]; then
    log "[DRY-RUN]   .git + initial commit"
  fi
  if [ "${OPT_SKIP_SMOKE}" -ne 1 ]; then
    log "[DRY-RUN] would run: dotnet build ${DEST_PATH}/${NEW_NAME}.sln -c Release"
  fi
}

# ---------- step 5-7: copy --------------------------------------------------

copy_template() {
  copy_tree "${TEMPLATE_SRC}"   "${DEST_PATH}/${TEMPLATE_TOKEN}"
}
# Design Ref: template-contracts-scaffold-fix §2.1 — Contracts sub-project.
copy_contracts() {
  copy_tree "${CONTRACTS_SRC}"  "${DEST_PATH}/${TEMPLATE_TOKEN}.Contracts"
}
copy_libcommons() {
  copy_tree "${LIBCOMMONS_SRC}"  "${DEST_PATH}/LibCommons"
}
copy_libnetworks() {
  copy_tree "${LIBNETWORKS_SRC}" "${DEST_PATH}/LibNetworks"
}

# ---------- step 8: token replacement ---------------------------------------

# Build a `find` extension predicate from $TEXT_EXTS.
# bash 3.2 friendly: build incrementally as a single string of args.
# Returns it via stdout.
build_text_find_args() {
  local first=1
  printf -- '\\( '
  local ext
  for ext in ${TEXT_EXTS}; do
    if [ "${first}" -eq 1 ]; then
      printf -- '-name "*.%s"' "${ext}"
      first=0
    else
      printf -- ' -o -name "*.%s"' "${ext}"
    fi
  done
  printf -- ' \\)'
}

replace_tokens() {
  # Design Ref: template-contracts-scaffold-fix §2.1 —
  # Both Template and Contracts subtrees need token replacement.
  # Each subtree is iterated separately to keep `find` scope clearly bounded
  # (LibCommons/LibNetworks must NOT be touched).
  local subtrees=(
    "${DEST_PATH}/${TEMPLATE_TOKEN}"
    "${DEST_PATH}/${TEMPLATE_TOKEN}.Contracts"
  )
  local find_expr
  find_expr="$(build_text_find_args)"

  local count=0
  local file
  local subtree
  for subtree in "${subtrees[@]}"; do
    while IFS= read -r file; do
      [ -f "${file}" ] || continue
      # Only touch files that actually contain the token (perf + minimise mtime churn).
      if grep -F -q -- "${TEMPLATE_TOKEN}" "${file}" 2>/dev/null; then
        replace_in_file "${file}" "${TEMPLATE_TOKEN}" "${NEW_NAME}"
        count=$((count + 1))
      fi
    done <<EOF
$(eval "find \"${subtree}\" -type f ${find_expr}")
EOF
  done

  # Rename the Template subtree directory + csproj.
  mv "${DEST_PATH}/${TEMPLATE_TOKEN}" "${DEST_PATH}/${NEW_NAME}"
  mv "${DEST_PATH}/${NEW_NAME}/${TEMPLATE_TOKEN}.csproj" \
     "${DEST_PATH}/${NEW_NAME}/${NEW_NAME}.csproj"

  # Design Ref: §2.1 — Rename the Contracts subtree directory + csproj.
  mv "${DEST_PATH}/${TEMPLATE_TOKEN}.Contracts" "${DEST_PATH}/${NEW_NAME}.Contracts"
  mv "${DEST_PATH}/${NEW_NAME}.Contracts/${TEMPLATE_TOKEN}.Contracts.csproj" \
     "${DEST_PATH}/${NEW_NAME}.Contracts/${NEW_NAME}.Contracts.csproj"

  # Design Ref: template-contracts-scaffold-fix §2.1 —
  # Source csproj has `..\..\LibCommons` (template-projects/ depth) but scaffold
  # output is flat, so adjust to `..\LibCommons`. Same for LibNetworks.
  local csproj_files=(
    "${DEST_PATH}/${NEW_NAME}/${NEW_NAME}.csproj"
    "${DEST_PATH}/${NEW_NAME}.Contracts/${NEW_NAME}.Contracts.csproj"
  )
  local cf
  for cf in "${csproj_files[@]}"; do
    [ -f "${cf}" ] || continue
    sed -i.bak \
      -e 's|\.\.\\\.\.\\LibCommons|..\\LibCommons|g' \
      -e 's|\.\.\\\.\.\\LibNetworks|..\\LibNetworks|g' \
      "${cf}"
    rm -f "${cf}.bak"
  done

  log "        replaced token in ${count} files."
}

# ---------- step 9: aux files (.gitignore / .gitattributes / README.md) -----

generate_gitignore() {
  cat > "${DEST_PATH}/.gitignore" <<'GITIGNORE'
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
GITIGNORE
}

generate_gitattributes() {
  cat > "${DEST_PATH}/.gitattributes" <<'GITATTR'
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
GITATTR
}

generate_readme() {
  cat > "${DEST_PATH}/README.md" <<README
# ${NEW_NAME}

A game server scaffolded from the FastPortSharp template
(<https://github.com/boinred/FastPortSharp>).

## Build & Run

\`\`\`bash
dotnet build ${NEW_NAME}.sln -c Release
dotnet run --project ${NEW_NAME} -c Release
\`\`\`

The server listens on \`0.0.0.0:7777\` by default. Edit
\`${NEW_NAME}/appsettings.json\` to change.

## Layout

- \`${NEW_NAME}/\`   — your game server (start here)
- \`LibCommons/\`   — engine: buffers, packet primitives (read-only baseline)
- \`LibNetworks/\`  — engine: TCP listener / session (read-only baseline)

## Adding packets

See \`${NEW_NAME}/README.md\` and \`${NEW_NAME}/QUICKSTART.ko.md\` for the
template's packet/handler customisation guide.

## License

MIT (inherits from the upstream FastPortSharp template).
README
}

# ---------- step 10: generate sln -------------------------------------------

generate_sln() {
  # .NET 10's `dotnet new sln` defaults to the newer .slnx (XML) format.
  # Force the classic .sln format so existing IDE tooling and CI scripts
  # that match `*.sln` continue to work.
  ( cd "${DEST_PATH}" \
    && dotnet new sln --format sln -n "${NEW_NAME}"                          >/dev/null \
    && dotnet sln "${NEW_NAME}.sln" add "${NEW_NAME}/${NEW_NAME}.csproj"     >/dev/null \
    && dotnet sln "${NEW_NAME}.sln" add "${NEW_NAME}.Contracts/${NEW_NAME}.Contracts.csproj"  >/dev/null \
    && dotnet sln "${NEW_NAME}.sln" add "LibCommons/LibCommons.csproj"       >/dev/null \
    && dotnet sln "${NEW_NAME}.sln" add "LibNetworks/LibNetworks.csproj"     >/dev/null )
}

# ---------- step 11: git init -----------------------------------------------

git_init_and_commit() {
  ( cd "${DEST_PATH}" \
    && git init -q -b main \
    && git add . \
    && git -c user.name='scaffold-game-server' \
           -c user.email='scaffold@local' \
           commit -q -m "Initial scaffold from ${TEMPLATE_TOKEN}" )
}

# ---------- step 12: smoke build --------------------------------------------

smoke_build() {
  if ! dotnet build "${DEST_PATH}/${NEW_NAME}.sln" -c Release --nologo; then
    err "'dotnet build ${DEST_PATH}/${NEW_NAME}.sln -c Release' failed."
    hint "this usually means a token was missed during replacement."
    hint "run with --dry-run to inspect, or file an issue."
    exit 4
  fi
}

# ---------- main ------------------------------------------------------------

main() {
  parse_args "$@"

  log "[1/12]  Parsing arguments...                      OK (NewName=${NEW_NAME}, Dest=${DEST_PATH})"
  log "[2/12]  Validating project name..."
  validate_name
  log "        OK"

  log "[3/12]  Resolving destination..."
  validate_dest
  log "        OK"

  if [ "${OPT_DRY_RUN}" -eq 1 ]; then
    log "[4/12]  Dry-run mode active. Filesystem unchanged."
    dry_run_plan
    exit 0
  fi

  log "[5/12]  Copying ${TEMPLATE_TOKEN} + ${TEMPLATE_TOKEN}.Contracts..."
  copy_template
  copy_contracts
  log "        OK"

  log "[6/12]  Copying LibCommons..."
  copy_libcommons
  log "        OK"

  log "[7/12]  Copying LibNetworks..."
  copy_libnetworks
  log "        OK"

  log "[8/12]  Replacing tokens (${TEMPLATE_TOKEN} -> ${NEW_NAME})..."
  replace_tokens
  log "        OK"

  log "[9/12]  Generating .gitignore, .gitattributes, README.md..."
  generate_gitignore
  generate_gitattributes
  generate_readme
  log "        OK"

  log "[10/12] Creating ${NEW_NAME}.sln..."
  generate_sln
  log "        OK"

  if [ "${OPT_NO_GIT}" -ne 1 ]; then
    log "[11/12] git init + initial commit..."
    git_init_and_commit
    log "        OK"
  else
    log "[11/12] git init skipped (--no-git)."
  fi

  if [ "${OPT_SKIP_SMOKE}" -ne 1 ]; then
    log "[12/12] dotnet build smoke..."
    smoke_build
    log "        OK"
  else
    log "[12/12] smoke build skipped (--skip-smoke)."
  fi

  log ""
  log "Done."
  log ""
  log "Next steps:"
  log "  cd ${DEST_PATH}"
  log "  dotnet run --project ${NEW_NAME} -c Release"
}

main "$@"
