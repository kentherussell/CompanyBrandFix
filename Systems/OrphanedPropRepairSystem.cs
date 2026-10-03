using Colossal.Serialization.Entities;
using Game;
using Game.Buildings;
using Game.Common;
using Game.Objects;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Tools;
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.Scripting;

namespace CompanyBrandFix
{
    /// <summary>
    /// Optional one-shot map-load cleanup for orphaned static prop instances.
    ///
    /// The system does nothing unless "Delete broken props on startup" is enabled.
    /// It never scans or replaces root buildings. The query is limited to static
    /// prop-like runtime objects and excludes buildings, vegetation, network objects,
    /// pillars, utility objects, outside connections, placeholders, temp entities,
    /// deleted entities and prefab entities.
    ///
    /// It waits until BrandRepairSystem has finished, then waits one additional
    /// update so brand-repair end-of-frame changes can play back first.
    ///
    /// Broken standalone props are deleted. Broken building-owned props are deleted
    /// and only their affected building is marked Updated. Objects owned by non-building
    /// infrastructure are skipped for safety. Healthy props and buildings are never
    /// modified.
    /// </summary>
    public partial class OrphanedPropRepairSystem : GameSystemBase
    {
        private const int MaxAttempts = 2;
        private const int MaxDetailLogs = 25;

        private EndFrameBarrier _endFrameBarrier;
        private PrefabSystem _prefabSystem;
        private BrandRepairSystem _brandRepairSystem;
        private EntityQuery _propQuery;

        private bool _scanArmed;
        private bool _brandCompletionObserved;
        private bool _needsFallbackArm = true;
        private int _attempt;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();

            _endFrameBarrier =
                World.GetOrCreateSystemManaged<EndFrameBarrier>();

            _prefabSystem =
                World.GetOrCreateSystemManaged<PrefabSystem>();

            _brandRepairSystem =
                World.GetOrCreateSystemManaged<BrandRepairSystem>();

            _propQuery = GetEntityQuery(
                new EntityQueryDesc
                {
                    All = new[]
                    {
                        ComponentType.ReadOnly<Game.Objects.Object>(),
                        ComponentType.ReadOnly<Game.Objects.Static>(),
                        ComponentType.ReadOnly<Game.Objects.Transform>(),
                        ComponentType.ReadOnly<PrefabRef>()
                    },
                    None = new[]
                    {
                        ComponentType.ReadOnly<Deleted>(),
                        ComponentType.ReadOnly<Temp>(),
                        ComponentType.ReadOnly<PrefabData>(),

                        // Root buildings are never cleanup candidates.
                        ComponentType.ReadOnly<Game.Buildings.Building>(),

                        // Keep the cleanup focused on prop-like static objects.
                        ComponentType.ReadOnly<Game.Objects.Tree>(),
                        ComponentType.ReadOnly<Game.Objects.Plant>(),
                        ComponentType.ReadOnly<Game.Objects.NetObject>(),
                        ComponentType.ReadOnly<Game.Objects.Pillar>(),
                        ComponentType.ReadOnly<Game.Objects.UtilityObject>(),
                        ComponentType.ReadOnly<Game.Objects.OutsideConnection>(),
                        ComponentType.ReadOnly<Game.Objects.Placeholder>()
                    }
                });

            Mod.Log.Info(
                "OrphanedPropRepairSystem created; optional prop cleanup is settings-controlled.");
        }

        [Preserve]
        protected override void OnGameLoadingComplete(
            Purpose purpose,
            GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            if (GameModeExtensions.IsGame(mode))
            {
                _attempt = 0;
                _brandCompletionObserved = false;
                _needsFallbackArm = false;

                if (!ShouldRunPropCleanup())
                {
                    _scanArmed = false;
                    Mod.Log.Info(
                        "Orphaned prop cleanup disabled in settings; no prop scan will run for this map.");
                    return;
                }

                _scanArmed = true;
                Mod.Log.Info(
                    "Orphaned prop scan armed; waiting for brand repair to finish.");
            }
            else
            {
                _scanArmed = false;
                _brandCompletionObserved = false;
                _needsFallbackArm = true;
                _attempt = 0;
            }
        }

        [Preserve]
        protected override void OnUpdate()
        {
            GameMode mode = GameManager.instance.gameMode;
            if (!GameModeExtensions.IsGame(mode))
            {
                return;
            }

            // Lifecycle fallback for a system created after the normal load callback.
            if (!_scanArmed && _needsFallbackArm)
            {
                _needsFallbackArm = false;
                _brandCompletionObserved = false;
                _attempt = 0;

                if (!ShouldRunPropCleanup())
                {
                    Mod.Log.Info(
                        "Orphaned prop cleanup disabled in settings; fallback will not scan this map.");
                    return;
                }

                _scanArmed = true;
                Mod.Log.Info(
                    "Orphaned prop scan armed by first-game-update fallback.");
            }

            if (!_scanArmed)
            {
                return;
            }

            // Never get ahead of the company-brand repair.
            if (!_brandRepairSystem.RepairFinishedForCurrentMap)
            {
                return;
            }

            // Wait one additional update so BrandRepairSystem's EndFrameBarrier
            // changes are applied before deciding a prop is still orphaned.
            if (!_brandCompletionObserved)
            {
                _brandCompletionObserved = true;
                Mod.Log.Info(
                    "Brand repair finished; orphaned prop scan will run next update.");
                return;
            }

            _scanArmed = false;

            try
            {
                Mod.Log.Info("Starting one-shot orphaned prop scan.");

                PropRepairStats stats = RepairOrphanedProps();

                Mod.Log.Info(
                    $"Orphaned prop scan complete. " +
                    $"Scanned={stats.Scanned}, " +
                    $"UniquePrefabsChecked={stats.UniquePrefabsChecked}, " +
                    $"Broken={stats.Broken}, " +
                    $"RemovedStandalone={stats.RemovedStandalone}, " +
                    $"RemovedBuildingOwned={stats.RemovedBuildingOwned}, " +
                    $"BuildingsRefreshed={stats.BuildingsRefreshed}, " +
                    $"SkippedNonBuildingOwned={stats.SkippedNonBuildingOwned}.");

                _attempt = 0;
            }
            catch (Exception ex)
            {
                _attempt++;

                if (_attempt < MaxAttempts)
                {
                    _scanArmed = true;
                    _brandCompletionObserved = true;

                    Mod.Log.Warn(
                        $"Orphaned prop scan attempt {_attempt} failed; " +
                        $"retrying on next update. " +
                        $"{ex.GetType().Name}: {ex.Message}");
                    return;
                }

                Mod.Log.Error(ex);
                Mod.Log.Error(
                    "Orphaned prop repair disabled for this map after retry failed.");
            }
        }

        private bool ShouldRunPropCleanup()
        {
            return Mod.Settings != null &&
                   Mod.Settings.DeleteBrokenPropsOnStartup;
        }

        private PropRepairStats RepairOrphanedProps()
        {
            PropRepairStats stats = default;
            int detailLogs = 0;

            EntityCommandBuffer endFrameCommands =
                _endFrameBarrier.CreateCommandBuffer();

            // Cache one validity result per unique prefab. Thousands of instances
            // sharing the same prefab therefore require only one TryGetPrefab call.
            Dictionary<Entity, bool> prefabValidityCache =
                new Dictionary<Entity, bool>();

            HashSet<Entity> buildingsToRefresh =
                new HashSet<Entity>();

            NativeArray<Entity> props =
                _propQuery.ToEntityArray(Allocator.Temp);

            try
            {
                stats.Scanned = props.Length;

                // Single pass. Healthy props are read and immediately ignored.
                for (int i = 0; i < props.Length; i++)
                {
                    Entity prop = props[i];

                    if (!IsUsablePropEntity(prop))
                    {
                        continue;
                    }

                    Entity prefab =
                        EntityManager.GetComponentData<PrefabRef>(prop).m_Prefab;

                    if (IsValidPrefabCached(prefab, prefabValidityCache))
                    {
                        continue;
                    }

                    stats.Broken++;

                    OwnerResolution<Entity> ownerResolution = ResolveOwner(prop);

                    if (ownerResolution.Building != Entity.Null)
                    {
                        QueueDelete(endFrameCommands, prop);
                        stats.RemovedBuildingOwned++;

                        Entity building = ownerResolution.Building;
                        if (EntityManager.Exists(building) &&
                            !EntityManager.HasComponent<Deleted>(building) &&
                            buildingsToRefresh.Add(building))
                        {
                            // Only a building proven to contain a broken prop gets touched.
                            if (!EntityManager.HasComponent<Updated>(building))
                            {
                                endFrameCommands.AddComponent<Updated>(building);
                            }

                            stats.BuildingsRefreshed++;
                        }

                        LogBrokenProp(
                            ref detailLogs,
                            prop,
                            prefab,
                            "building-owned; removed + building refresh queued");

                        continue;
                    }

                    if (!ownerResolution.HasOwner)
                    {
                        QueueDelete(endFrameCommands, prop);
                        stats.RemovedStandalone++;

                        LogBrokenProp(
                            ref detailLogs,
                            prop,
                            prefab,
                            "standalone; removed");

                        continue;
                    }

                    // It belongs to something other than a building. Do not mutate
                    // networks/infrastructure/unknown owner graphs automatically.
                    stats.SkippedNonBuildingOwned++;

                    LogBrokenProp(
                        ref detailLogs,
                        prop,
                        prefab,
                        $"owned by non-building entity {ownerResolution.LastOwner}; skipped");
                }
            }
            finally
            {
                props.Dispose();
            }

            stats.UniquePrefabsChecked = prefabValidityCache.Count;

            if (stats.Broken > MaxDetailLogs)
            {
                Mod.Log.Warn(
                    $"Orphaned prop detail logging capped at {MaxDetailLogs}; " +
                    $"{stats.Broken - MaxDetailLogs} additional broken props omitted.");
            }

            return stats;
        }

        private bool IsUsablePropEntity(Entity prop)
        {
            return EntityManager.Exists(prop) &&
                   !EntityManager.HasComponent<Deleted>(prop) &&
                   EntityManager.HasComponent<PrefabRef>(prop);
        }

        private bool IsValidPrefabCached(
            Entity prefabEntity,
            Dictionary<Entity, bool> cache)
        {
            if (cache.TryGetValue(prefabEntity, out bool cached))
            {
                return cached;
            }

            bool isValid =
                prefabEntity != Entity.Null &&
                EntityManager.Exists(prefabEntity) &&
                !EntityManager.HasComponent<Deleted>(prefabEntity) &&
                _prefabSystem.TryGetPrefab(prefabEntity, out PrefabBase prefab) &&
                prefab != null;

            cache[prefabEntity] = isValid;
            return isValid;
        }

        private OwnerResolution<Entity> ResolveOwner(Entity prop)
        {
            return RepairDecisions.ResolveOwner(
                prop,
                Entity.Null,
                EntityManager.Exists,
                entity => EntityManager.HasComponent<Game.Common.Owner>(entity)
                    ? EntityManager.GetComponentData<Game.Common.Owner>(entity).m_Owner
                    : Entity.Null,
                entity => EntityManager.HasComponent<Game.Buildings.Building>(entity));
        }

        private static void QueueDelete(
            EntityCommandBuffer commandBuffer,
            Entity entity)
        {
            commandBuffer.AddComponent<Deleted>(entity);
        }

        private static void LogBrokenProp(
            ref int detailLogs,
            Entity prop,
            Entity prefab,
            string action)
        {
            if (detailLogs >= MaxDetailLogs)
            {
                return;
            }

            detailLogs++;
            Mod.Log.Warn(
                $"Broken prop {prop}, prefab={prefab}: {action}.");
        }

        private struct PropRepairStats
        {
            public int Scanned;
            public int UniquePrefabsChecked;
            public int Broken;
            public int RemovedStandalone;
            public int RemovedBuildingOwned;
            public int BuildingsRefreshed;
            public int SkippedNonBuildingOwned;
        }
    }
}
