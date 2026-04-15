#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT_PATH="$REPO_ROOT/src/TrevorTrevor/TrevorTrevor.csproj"
CONFIGURATION="${CONFIGURATION:-Release}"
GTA_PATH="${1:-/home/USER/Documents/GTA-V_linux}"



if [[ ! -d "$GTA_PATH" ]]; then
  echo "Path does not exist: $GTA_PATH" >&2
  exit 1
fi

resolve_gta_dir() {
  local input_path="$1"
  local candidate=""

  if [[ -f "$input_path/GTA5.exe" ]]; then
    echo "$input_path"
    return
  fi

  if [[ -d "$input_path/drive_c" ]]; then
    local candidates=(
      "$input_path/drive_c/Program Files/Rockstar Games/Grand Theft Auto V"
      "$input_path/drive_c/Program Files (x86)/Rockstar Games/Grand Theft Auto V"
      "$input_path/drive_c/Program Files/Steam/steamapps/common/Grand Theft Auto V"
      "$input_path/drive_c/Program Files (x86)/Steam/steamapps/common/Grand Theft Auto V"
    )

    for candidate in "${candidates[@]}"; do
      if [[ -f "$candidate/GTA5.exe" ]]; then
        echo "$candidate"
        return
      fi
    done

    candidate="$(find "$input_path/drive_c" -maxdepth 8 -type f -name GTA5.exe -print -quit 2>/dev/null || true)"
    if [[ -n "$candidate" ]]; then
      echo "$(dirname "$candidate")"
      return
    fi
  fi

  echo ""
}

GTA_DIR="$(resolve_gta_dir "$GTA_PATH")"
if [[ -z "$GTA_DIR" ]]; then
  echo "Could not find GTA V game directory from: $GTA_PATH" >&2
  echo "Pass the directory that contains GTA5.exe." >&2
  exit 1
fi

echo "Resolved GTA directory: $GTA_DIR"

echo "Building TrevorTrevor ($CONFIGURATION)..."
dotnet build "$PROJECT_PATH" -c "$CONFIGURATION"

BUILT_DLL="$REPO_ROOT/src/TrevorTrevor/bin/$CONFIGURATION/net48/TrevorTrevor.dll"
if [[ ! -f "$BUILT_DLL" ]]; then
  echo "Build completed but output not found: $BUILT_DLL" >&2
  exit 1
fi

SCRIPTS_DIR="$GTA_DIR/scripts"
mkdir -p "$SCRIPTS_DIR"
cp "$BUILT_DLL" "$SCRIPTS_DIR/TrevorTrevor.dll"

if [[ ! -f "$GTA_DIR/dinput8.dll" || ! -f "$GTA_DIR/ScriptHookV.dll" ]]; then
  echo "Warning: ScriptHookV does not look installed (missing dinput8.dll or ScriptHookV.dll in game root)." >&2
fi

if [[ ! -f "$GTA_DIR/ScriptHookVDotNet.asi" ]]; then
  echo "Warning: ScriptHookVDotNet.asi is missing in game root. .NET scripts will not load without it." >&2
fi

echo "Installed: $SCRIPTS_DIR/TrevorTrevor.dll"
echo "Runtime dependencies were not copied by this script."
echo "Done. Start GTA V and open the TrevorTrevor menu in free roam."
