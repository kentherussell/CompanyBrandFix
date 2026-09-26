# Company Brand Fix

A deliberately small Cities: Skylines II code mod that repairs live companies whose `CompanyData.m_Brand` is `Entity.Null`.

## Behavior

- Detects every `GameMode.Game` map/save load.
- Also has a first-game-tick fallback if the mod/system is created after the normal loading callback already fired.
- Waits **2,048 simulation ticks** before touching the loaded city (~11.25 in-game minutes at vanilla time scale).
- Checks the timer every **512 ticks** while active.
- Scans existing non-temp, non-deleted, non-moving-away companies once.
- Uses each company prefab's vanilla `CompanyBrandElement` buffer as the compatibility list.
- If several compatible brands exist, picks one deterministically from the company entity index.
- Sets only literal null brands. Existing non-null brands are never changed.
- Marks the rented property `Updated` through `EndFrameBarrier` so brand/billboard visuals can refresh.
- After success, remains enabled only as a tiny lifecycle listener; its idle update path is a few boolean checks every 512 simulation ticks and performs no company query.

## Logging / verification

Expected normal sequence:

- `CompanyBrandFix 0.1.2 loading.`
- `BrandRepairSystem created; waiting for a city/map load.`
- `Brand repair scheduled (map load callback)...` or `Brand repair scheduled (first game tick fallback)...`
- `Starting brand repair pass...`
- `Brand repair complete. Scanned=..., Null=..., Repaired=..., NoCompatibleBrand=...`

If `Repaired` is greater than zero, the repair ran and changed live company entities.

## Failure behavior

The pass is idempotent. If an exception happens after some companies were repaired, a retry will simply skip those companies.

- First exception: log one warning and retry once after another 2,048 ticks.
- Second exception: log the error and stop attempting repairs for the current map.
- A missing prefab, missing `CompanyBrandElement` buffer, empty compatibility list, or invalid brand entity is a safe skip, not an exception.
- The next map/save load gets a fresh attempt.

## Why this is intentionally small

There is no Harmony, reflection, settings UI, serialized mod state, custom components, or continuous company polling. It relies on vanilla's own prefab-to-brand compatibility buffer rather than duplicating company/resource rules.

Direct ECS field/type references are intentional. If a future game update actually removes or renames `CompanyData.m_Brand` or `CompanyBrandElement`, the mod should be rebuilt instead of hiding an incompatible API behind reflection.

## Build

Requirements:

1. Cities: Skylines II Modding Toolchain installed.
2. `CSII_TOOLPATH` configured by the toolchain.
3. .NET/MSBuild environment used by the CS2 toolchain.

Open `CompanyBrandFix.csproj` in the CS2 modding environment and build `Release`.

## Deliberate scope

This version repairs companies once per map/save load. A company that becomes null-branded later in the same play session is left alone until the next load.
