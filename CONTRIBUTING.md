# Contributing to CraftIndex

Thanks for helping improve CraftIndex.

## Before opening a change

1. Search existing issues and pull requests.
2. For behavior or layout changes, open an issue describing the affected Hammer tab or crafting station.
3. Keep the no-spoiler contract intact: never render content outside Valheim's current available-piece or available-recipe lists.
4. Do not change recipes, costs, progression, saves, worlds, or network state.

## Building

The project requires a local Valheim/BepInEx installation for reference assemblies:

```powershell
dotnet build CraftIndex.sln -c Release -p:ValheimRoot="C:\path\to\Valheim"
```

Run both verification suites after the build:

```powershell
dotnet run --project tests/InventoryUX.CoreTests/InventoryUX.CoreTests.csproj -c Release --no-build
dotnet run --project tests/InventoryUX.ApiTests/InventoryUX.ApiTests.csproj -c Release --no-build
```

## Pull requests

- Keep changes focused.
- Describe the player-visible result and how it was tested.
- Include before/after screenshots for UI changes.
- Avoid committing Valheim, Unity, or BepInEx binaries.
