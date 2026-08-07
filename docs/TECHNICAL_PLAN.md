# CraftGuard technical implementation plan

## Verified game surfaces

This implementation targets the assemblies researched on 2026-08-06 at `C:\ValheimServer\server`.

- `PieceTable.UpdateAvailable` is the authoritative Hammer visibility gate. CraftGuard runs after it and reorders only the resulting `m_availablePieces` buckets.
- `Hud.UpdatePieceList` remains the native renderer and input surface. CraftGuard adds non-interactive labels to its existing icon objects.
- `Player.GetAvailableRecipes` feeds `InventoryGui.UpdateRecipeList`. At any active crafting station, CraftGuard filters/sorts the resulting private `RecipeDataPair` list after vanilla has built it, then moves the same row objects and adds group headers.
- `InventoryGui.UpdateRecipe` remains completely vanilla; CraftGuard does not append station-upgrade or guidance text to recipe details.
- The station mode strip is parented to the non-scrolling crafting panel and positioned from the vanilla Upgrade tab. Search is parented to the recipe viewport above the scrolling content.

## Data flow

```text
Valheim known-content filtering
    -> already-visible Piece / Recipe objects
    -> metadata + heuristic classification
    -> deterministic group and sort keys
    -> existing native UI objects, reordered and labeled
```

No recipes, pieces, unlock keys, inventories, saves, or network state are written.

Crafting-station search never queries `ObjectDB`. It matches text only against the recipe rows already produced by `Player.GetAvailableRecipes`, so a search term cannot reveal an undiscovered recipe or create an empty future biome heading.

## Classifier strategy

- Equipment-station `Default` view keeps Valheim's existing order; `Type` uses native item type and skill metadata, then orders each type by biome.
- Food stations use their own remembered `Default` / `Stat` / `Biome` mode. Meal `Stat` uses HP/Stamina/Eitr data with a 20% specialist threshold and sorts strongest-to-weakest. Food Preparation labels its recipes as `Feasts`; their rows show per-serving nutrition. Mead stations use HP/Stamina/Utility effect groups.
- Prepared-food nutrition is resolved from live game relationships: `CookingStation` conversion chains lead doughs and uncooked items to the final edible prefab, while feast inputs resolve through `Feast.m_foodItem`. This avoids copied stat tables and follows compatible modded conversions automatically.
- Progression biome uses the latest recognized source marker across the output item and all ingredients, plus a safe `Other` fallback. Biomes and within-type recipes render late-game first.
- Hammer crafting families use native `CraftingStation` and `StationExtension.m_craftingStation` relationships.
- Hammer Crafting presents six fixed family rows. A narrowed non-interactive family rail occupies the left side; unlocked native piece objects are visually permuted beside it and receive readable two-line names.
- Workbench and Forge preserve their native base/extension relationships; broader Food & Brewing, Advanced Crafting, Processing, and Specialized rows collect standalone stations and processors using component/station metadata plus safe name fallbacks.
- All organized Hammer tabs use a visual-slot map so mouse, keyboard, and controller selection continue to target the correct underlying piece. Dense non-Crafting tabs use up to seven 13-piece shelves; oversized modded tabs fall back to the ordinary grid instead of hiding overflow.
- Hammer decorations are keyed by HUD instance, category, icon grid, known-piece identities, family labels, and separator configuration. The postfix therefore becomes a cheap signature check during Valheim's frequent `Hud.UpdatePieceList` calls and rebuilds graphics only on a real presentation change.
- Building consolidates unlocked pieces into Wood, Core Wood, Darkwood, and Ashwood blocks with vertically spanning material cards and structural subgroup ordering; Core Wood does not add redundant internal dividers. Material groups receive authoritative row blocks, compressed into the existing panel height when rounding requires more than seven rows. Furniture retains functional type cards and derives an early-to-late within-type sort key from live piece/resource biome metadata, with rugs and banners held as contiguous Display / Decor blocks and Hot Tub explicitly classified as Utility / Other. Misc. uses functional metadata and safe name hints.
- Unknown modded content remains visible.

## Known differences from the concept mockup

- Every active crafting station has fixed controls and search. Equipment stations show Default/Type/Biome; food-oriented stations show Default/Stat/Biome. Personal inventory crafting remains vanilla.
- CraftGuard retains Valheim's 15×6 logical Hammer grid and expands only the panel/mask height by one native row spacing.
- Hammer Crafting distributes six enlarged family rows across that height. Misc., Building, Heavy Build, and Furniture can use seven compact visual shelves without adding logical grid cells.
- Station-upgrade information is intentionally absent from recipe details.
- Built-but-out-of-range detection is deferred because the reviewed public state does not reliably expose that condition without a world scan.
- Biome metadata begins as hybrid marker metadata. Enable the CSV inventory diagnostic to review the complete live content set and promote ambiguous vanilla entries into explicit overrides.

## Verification gates

- Build with warnings treated as errors.
- Pure classifier tests cover food thresholds, mead effect grouping, biome selection/order, type classification, stable ordering independent of craftability, and absence of empty future groups.
- Cecil API tests verify every private patch/access target against the current game assembly.
- Client-side visual and progression-state QA remains required because only a dedicated-server installation is present locally.
