# Breakdown Revisited

A Cities: Skylines mod that shows you where your city's traffic actually comes from.

Click any building, road, vehicle, or district while the Traffic Routes overlay is active and a panel appears listing the top origin→destination district pairs driving traffic through it — ranked by volume, broken down by route type, and smoothable over time with Average mode.

**[► Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3713531137)**
**[► Original Breakdown by whyoh](https://github.com/whyoh/CitiesBreakdown)**

---

## Features

**Route popup** — click any entity in Traffic Routes mode to see the top-25 district pairs generating traffic through it. Rows show direction (from/to), district colours, vehicle-type breakdown on hover, and an optional route count.

**City-wide accordion** — without clicking anything in Traffic Routes mode, a panel lists every district ranked by total connections, expandable to see per-partner detail.

**Route type tooltips** — hover any row to see the traffic split: Pedestrians, Cyclists, Private Cars, Trucks, Public Transit, City Services.

**Districts / Roads toggle** — switch the display between district-level and road-segment-level origin/destination pairs.

**Average mode** — after a few minutes of play an `Average` button unlocks. It switches from a live snapshot to an exponential moving average (EMA) of traffic shares accumulated across the session. Useful for seeing long-term patterns without being misled by momentary spikes. Percentages are colour-coded: orange >10%, red >15%.

---

## How to use

1. Enable **Traffic Routes** in the game's info view panel.
2. Click any entity (building, road, vehicle, citizen…). A panel appears to the right.
3. Use **Districts** / **Roads** to change the grouping.
4. After a few minutes, **Average** becomes available — click it for smoothed historical data.
5. For a city-wide view, open Traffic Routes without clicking anything. The accordion panel appears below the info panel.

---

## Compatibility

- Cities: Skylines **1.17+** (1.x only; CS2 not supported)
- No Harmony dependency — uses only the standard ICities API and reflection
- No known conflicts

---

## Development

### Prerequisites

- Linux (or WSL) with Mono (`xbuild`) installed
- Access to a Windows machine with Cities: Skylines via SMB

### Build and deploy

```bash
# One-time: mount the Windows game share
./mount-cities.sh

# Build (debug) and copy to the game's Mods folder
./deploy.sh

# Release build
./deploy.sh --release
```

`deploy.sh` calls `xbuild`, stages `dist/BreakdownRevisited/`, copies to `/mnt/cities_skylines_data/Addons/Mods/BreakdownRevisited/`, and tails `output_log.txt` so you can catch runtime errors immediately.

### Publishing to Steam Workshop

```bash
# First publish — leaves WORKSHOP_ITEM_ID blank to create a new item
./deploy.sh --release
./publish.sh "Initial release"
# → steamcmd prints the new item ID; add it to .env as WORKSHOP_ITEM_ID=...

# Subsequent updates
./publish.sh "Fix: route display glitch"
```

### Release workflow (automated via GitHub Actions)

1. Use Conventional Commits (`feat:`, `fix:`, `chore:`, etc.) on every commit.
2. Release Please opens a Release PR when there is something worth releasing.
3. Before merging: run `./deploy.sh --release`, commit `dist/`.
4. Merge the Release PR → tag is created → `workshop-deploy.yml` fires and publishes to Steam.

### GitHub secrets required

| Secret | Value |
|---|---|
| `RELEASE_PLEASE_TOKEN` | PAT with `repo` scope (so tag push triggers workflows) |
| `STEAM_CONFIG_VDF` | `base64 ~/.steam/steamcmd/config/config.vdf` |
| `STEAM_USERNAME` | Your Steam username |
| `WORKSHOP_ITEM_ID` | Steam Workshop item ID (set after first publish) |

### Architecture

- `Breakdown.cs` — `BreakdownMod` (IUserMod), `BreakdownThread` (ThreadingExtensionBase), `PathCount`, `PathDetails`, `EmaEntry`, all route-walking and EMA logic
- `BreakdownUI.cs` — `UIBreakdownPanel` injected into each WorldInfoPanel (Districts/Roads/Average toggle + row display)
- `BreakdownAccordionPanel.cs` — city-wide `UIBreakdownAccordionPanel` shown in Traffic Routes mode
- `CitiesExtensions.cs` — extension methods: segment→district name, path tail walker
- `Counts.cs` — generic `Counts<T>` histogram

### Coding conventions

- No comments explaining *what* code does — only *why* (hidden constraint, workaround, subtle invariant)
- Target .NET 3.5 / C# 6 — no `async/await`, no `Span<T>`, string interpolation OK
- No Harmony, no NuGet — only game DLLs from the SMB share
- All UI components created lazily in `Start()` or `InitUI()`, never in constructors
- `OnUpdate` hot path deferred with `% 60` / `% 300` guards

---

## Credits

**whyoh** — original mod concept, route-walking logic, and UI layout.  
**roberto-naharro** — revival, city-wide accordion panel, Average/EMA mode, route-type tooltips, and ongoing maintenance.
