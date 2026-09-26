CompanyBrandFix
============================

Purpose
-------
1. Always performs the lightweight CompanyData.m_Brand repair after each map load.
2. Optionally performs one prop-only orphan cleanup AFTER brand repair.

Settings
--------
Options -> Mods -> Company Brand Fix -> Main -> Cleanup

"Delete broken props on startup"
Default: OFF

When OFF:
- No orphaned-prop query/scan runs at all.
- Only the lightweight company-brand repair runs.

When ON:
- Brand repair runs first.
- The prop cleanup waits one additional system update so brand/property refreshes can play back.
- One prop-only scan runs, then stops for the rest of the map session.
- Healthy props and healthy buildings receive no writes.
- Root buildings are never deleted/replaced by this system.

Standalone broken prop behavior
-------------------------------
The mod first attempts a conservative rename recovery:
- It reads the stale prefab/entity debug name when available.
- It normalizes names by keeping only letters/digits and lowercasing.
  Example: R0158___Aldi and R0158_Aldi normalize to the same key.
- It only replaces when exactly ONE healthy prop prefab already represented in the city has that exact normalized key.
- It does NOT use fuzzy/Levenshtein similarity and never guesses between ambiguous matches.
- If no safe match exists, the broken standalone prop is deleted.

Important: entity/debug names are best-effort runtime metadata. Some broken references may not retain a recoverable name; those fall back to deletion.

Building-owned broken prop behavior
-----------------------------------
- The broken child prop is removed.
- Only the owning building is marked Updated.
- Healthy buildings are untouched.
- The mod does not name-match generated building subobjects; the current building definition should be authoritative.

Non-building-owned objects
--------------------------
Broken objects owned by networks/infrastructure/other non-building owner graphs are logged and skipped for safety.

Logging
-------
Search the game log for CompanyBrandFix.
Useful summary line:

Orphaned prop scan complete. Scanned=..., UniquePrefabsChecked=..., Broken=..., ReplacedStandalone=..., RemovedStandalone=..., RemovedBuildingOwned=..., BuildingsRefreshed=..., SkippedNonBuildingOwned=...

Safety / performance
--------------------
- Prop cleanup is opt-in and defaults OFF.
- When disabled, there is no prop scan.
- When enabled, it is a one-shot in-memory ECS scan after each map load.
- Prefab validity is cached per unique prefab entity.
- Only confirmed broken prop instances are mutated.
- Company brand repair and prop cleanup each retry once after an exception, then fail closed for that map.

Build note
----------
This source package has not been compiled against your locally installed CS2 toolchain. Build it in your existing CompanyBrandFix project and resolve any game-version-specific API/compiler differences reported by Visual Studio.
