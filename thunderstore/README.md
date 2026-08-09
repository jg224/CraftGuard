![CraftGuard organized Hammer and crafting-station menus](https://raw.githubusercontent.com/jg224/CraftGuard/main/docs/images/craftguard-showcase.png)

# CraftGuard

Organizes Valheim's Hammer and crafting stations without changing recipes, costs, or progression.

---

## Features

`1 - Hammer and Crafting Stations`

- Groups unlocked Hammer pieces into clean, readable sections.
- Adds recipe views, search, and readable food stats to crafting stations.

---

**0.2.3 changes**

- Makes recipe search filter cached rows without rebuilding the crafting panel on every keystroke.
- Keeps typed text, focus, and the caret stable while filtering.
- Caches Hammer grouping and sorting, then refreshes only categories whose pieces changed.
- Reuses generated menu cards, dividers, labels, headings, and food-stat results to reduce allocations.

**0.2.2:** Adds the CraftGuard menu showcase above.

**0.2.1 changes**

- Remembers Default or Mod View independently for each build tool. Hammer starts in Mod View; Hoe and Cultivator start in Default View.
- Releases generated Hammer and crafting-station UI when its owning menu or scene closes.
- Stops Hammer UI work during teleporting and shutdown.
- Adds self-healing compatibility safeguards with rate-limited diagnostics.
- Prevents gameplay keys from activating while typing in crafting search.
- Removes the forced backdrop and matches custom controls to Valheim's native menu styling.

Client-side only. [Source and support](https://github.com/jg224/CraftGuard)
