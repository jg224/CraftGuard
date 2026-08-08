# Changelog

## 0.2.0 - 2026-08-08

- Rebuilds the organized Hammer layout only when pieces, tabs, or relevant settings change, eliminating full-list scans and hierarchy reordering on idle frames.
- Computes names, identifiers, progression keys, and group metadata once per piece during a refresh instead of repeatedly inside sort comparisons.
- Reduces custom Hammer UI object count by replacing four-object borders with lightweight outlines.
- Removes redundant grid transpilers now that CraftGuard uses Valheim's native 15 by 6 Hammer grid.

## 0.1.2 - 2026-08-07

- Fixes the static Repair button so it enters repair mode from every Hammer tab without switching categories.

## 0.1.1 - 2026-08-07

- Publishes CraftGuard under the correct `jg224` Thunderstore namespace.
- Places the native `Q` previous-category control in the open header space immediately left of Misc.
- Adds persistent `Default View` and `Mod View` buttons beneath Repair, rebuilding the live Hammer list when switching so visuals, hover, and selection stay aligned.
- Orders Misc Travel as Cart, Raft, Karve, Longship, Drakkar, and Portal; moves the Cartography Table to Utility and uses it as that section's icon; places Roundpole Fence first and Shield Generator last in Defense.

## 0.1.0 - 2026-08-07

Initial public release of CraftGuard, renamed from the private InventoryUX development builds.

### Hammer menu

- Adds organized, progression-aware layouts to Misc., Crafting, Building, Heavy Build, and Furniture.
- Uses compact inset category cards, subtle dividers, native hover feedback, and corrected hit targets.
- Keeps Repair static and outside dynamic sorting.
- Adds optional outlined Hammer piece names, disabled by default.
- Caches menu decoration work to avoid frame loss while organized tabs are open.

### Crafting stations

- Adds fixed `Default`, `Type`/`Stat`, and `Biome` view controls.
- Adds recipe search with a clear button.
- Groups equipment by type and biome.
- Groups meals by nutrition and shows readable HP, Stamina, and Eitr values.
- Resolves doughs, uncooked pies, feasts, and other prepared foods through their live final edible item.
- Groups Mead Ketill recipes as HP, Stamina, or Utility.
- Removes inappropriate station-upgrade copy from food and equipment recipe details.

### Safety and compatibility

- Organizes only Hammer pieces and recipes already exposed by Valheim.
- Leaves recipes, costs, unlocks, saves, worlds, and multiplayer state unchanged.
- Preserves modded content through fallback groups.
- Retains the legacy `com.inventoryux.valheim` plugin identifier so existing settings migrate automatically.
