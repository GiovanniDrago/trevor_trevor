# GTA V TrevorTrevor

This repository now contains only the `TrevorTrevor` ScriptHookVDotNet mod for GTA V.

## Project Layout

- `src/TrevorTrevor/TrevorTrevor.cs`
- `src/TrevorTrevor/TrevorTrevor.csproj`
- `scripts/deploy-heroic.sh`

## Prerequisites

1. GTA V with ScriptHookV installed.
2. ScriptHookVDotNet (v3) installed in GTA V.
3. Build tools on Windows (Visual Studio Build Tools or Visual Studio with MSBuild).
4. Place these DLLs in `lib/` at repo root:
   - `lib/ScriptHookVDotNet3.dll`

## Build and Install

On Linux (Heroic default prefix path), deploy:

```bash
./scripts/deploy-heroic.sh
```

Or with a custom GTA V path:

```bash
./scripts/deploy-heroic.sh "/path/to/Grand Theft Auto V"
```

The deploy script copies `TrevorTrevor.dll` to `<GTA_PATH>/scripts`.

## In Game

Open the mod menu with either:
- hold D-pad right for 0.5s (controller)
- press `F6` (keyboard)

Logs are written in your GTA `scripts/` folder:
- `TrevorTrevor.log`
