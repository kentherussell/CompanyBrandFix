# Architecture notes

## Mapping source

Do not infer a brand from commercial/industrial/office/storage tags manually.

Vanilla `Game.Prefabs.CompanyInitializeSystem` builds a `CompanyBrandElement` buffer on company prefabs from each `BrandPrefab.m_Companies` list. Runtime company initialization also consumes that relationship. This mod reads the same buffer, so compatibility comes from the game and any correctly registered custom prefabs.

## Lifecycle / timing

The original 0.1.0 build disabled the repair system inside `OnCreate`, which made the map-load lifecycle trigger brittle and could leave the repair permanently unarmed. 0.1.1 keeps the system enabled as a tiny listener instead.

- `OnGameLoadingComplete`: schedules a repair for game maps.
- First simulation tick fallback: schedules a repair if the normal loading callback was missed because the system was created late.
- `GetUpdateInterval`: 512 simulation ticks.
- Post-load delay: 2,048 simulation ticks.
- Retry delay: 2,048 simulation ticks.
- Attempts: 2 total.

After a successful pass, the system performs no ECS company query until another map load. The idle path is only game-mode/state checks.

## API-drift strategy

Keep the dependency surface narrow:

- `CompanyData`
- `PrefabRef`
- `CompanyBrandElement`
- optional `PropertyRenter`
- `Updated`
- standard lifecycle/simulation systems

No private fields, reflection, Harmony patches, or copied vanilla algorithms.

Runtime drift is handled by existence/component/buffer checks, a late-load fallback trigger, one bounded retry, and fail-closed behavior for the map. Compile-time type/field removal intentionally requires a rebuild rather than a reflection compatibility layer.


## Brand validity
A brand reference is considered healthy only when it is non-null, the entity still exists, and it is not marked `Deleted`. Null and stale/deleted references are repaired using the company prefab's `CompanyBrandElement` compatibility buffer.
