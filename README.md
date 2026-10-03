# Company Brand Fix

A Cities: Skylines II mod that repairs missing or broken company brands when you load a city. It is useful after a company brand pack is updated, removed, or unsubscribed from, leaving companies with references to brands that no longer exist.

**[Get Company Brand Fix on Paradox Mods](https://mods.paradoxplaza.com/mods/160848/Windows)**

## Install and use

1. Subscribe to the mod on Paradox Mods and enable it in your active playset.
2. Load your city. Company-brand repair runs automatically after loading; there is no button to press.
3. If broken props remain, enable the optional cleanup described below and reload the city.

Brand repair is always active while the mod is enabled. Prop cleanup is off by default.

## What it fixes

The mod checks companies for brand references that are missing, deleted, or no longer resolve to a loaded prefab. It selects a valid replacement from that company's compatible brand list and requests a refresh of its rented property, if present.

- Companies with valid brands keep them.
- Companies without a valid company prefab or an available compatible brand are skipped.
- The repair runs once per map load. It does not continuously scan the city.

It assigns an available compatible brand; it cannot restore assets from a brand pack you have removed.

## Optional broken-prop cleanup

Open **Options > Mods > Company Brand Fix > Main > Cleanup** and enable **Delete broken props on startup** before loading your city.

This runs a single cleanup pass after company-brand repair, with one additional system update to allow the property refreshes to take effect. It targets static prop instances with missing or invalid prefabs, including broken props that can appear as gridded boxes.

| Object | Cleanup behavior |
| --- | --- |
| Broken standalone prop | Removed. |
| Broken prop owned by a building | Removed, with a refresh requested for the owning building. |
| Broken object owned by something other than a building | Skipped. |
| Prop with a valid prefab | Left alone. |

The cleanup excludes root buildings, trees, plants, network objects, pillars, utility objects, outside connections, and placeholders. It does not guess replacement props or recover renamed assets.

Save a copy of your city before using cleanup if you may want to restore the removed props. Turning the option off stops future cleanup passes; it does not undo removals saved with the city.

## Troubleshooting

**A company still has a broken brand:** the mod needs a valid company prefab and at least one loaded compatible brand. Check that the relevant asset packs are enabled, then reload the city.

**Broken props remain:** check that cleanup was enabled before loading the city. Objects owned by non-building entities are intentionally skipped, and this mod does not repair every kind of missing asset.

**You enabled cleanup while the city was already open:** reload the city to run the cleanup pass.

Search the game log for `CompanyBrandFix`. The `Brand repair complete` summary reports how many companies were scanned, repaired, or skipped. When cleanup is enabled, `Orphaned prop scan complete` reports broken props, removals, building refreshes, and skipped objects. Each pass retries once on an exception, then stops for that map if the retry fails.

When reporting a problem, include your game version, mod version, whether cleanup was enabled, and the relevant log summaries or errors.

## Building from source

The project uses C# 9 and targets .NET Framework 4.7.2. It depends on the Cities: Skylines II modding toolchain and game assemblies, so the .NET SDK alone is not enough.

1. Install and configure the Cities: Skylines II modding toolchain.
2. Check that the user environment variable `CSII_TOOLPATH` points to the toolchain directory containing `Mod.props` and `Mod.targets`. The project imports both files from that location.
3. Open `CompanyBrandFix.sln` in Visual Studio with .NET Framework 4.7.2 targeting support, select **Release**, and build.

The project treats compiler warnings as errors. Build against the game assemblies for the version you intend to use.

### Source layout

- `Mod.cs`: mod startup, settings registration, logging, and system update order.
- `Settings.cs`: the optional cleanup setting and English option labels.
- `Systems/BrandRepairSystem.cs`: company-brand validation, replacement, and property refresh.
- `Systems/OrphanedPropRepairSystem.cs`: optional prop cleanup and owner checks.
- `Properties/PublishConfiguration.xml`: Paradox Mods publishing metadata.

## Tests

Run the behavior tests with the .NET 9 SDK:

```text
dotnet test Tests/CompanyBrandFix.Tests.csproj
```

The test project compiles the same `RepairDecisions.cs` source used by the mod, without requiring game assemblies or a running city. It checks compatible-brand selection, invalid and empty brand lists, deterministic distribution, nested building ownership, missing and non-building owners, self-ownership, cycles, and the ownership traversal limit. The mod project excludes the test sources from its build.

These tests protect the repair decisions. They do not verify game lifecycle callbacks, ECS query exclusions, command-buffer playback, or visual property refreshes. Those still need an in-game check: load a city after removing a brand pack, verify brand repair with cleanup disabled, then enable cleanup and reload to check broken-prop removal and preservation of healthy objects.
