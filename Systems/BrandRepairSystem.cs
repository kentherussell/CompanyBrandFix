using Colossal.Serialization.Entities;
using Game;
using Game.Agents;
using Game.Buildings;
using Game.Common;
using Game.Companies;
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
    /// Repairs CompanyData.m_Brand when it is null or points to a prefab
    /// that no longer resolves to an actual loaded prefab.
    ///
    /// The repair is armed when a city/map finishes loading and executes
    /// on the next normal system update. No simulation-time delay is used.
    /// Existing valid brands are never modified.
    /// </summary>
    public partial class BrandRepairSystem : GameSystemBase
    {
        private const int MaxAttempts = 2;

        private EndFrameBarrier _endFrameBarrier;
        private PrefabSystem _prefabSystem;
        private EntityQuery _companyQuery;

        private bool _repairPending;
        private bool _needsFallbackArm = true;
        private int _attempt;

        /// <summary>
        /// Becomes true after this map's brand repair has either completed
        /// successfully or failed closed after its final retry.
        /// The prop repair system uses this as a sequencing barrier.
        /// </summary>
        public bool RepairFinishedForCurrentMap { get; private set; }

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();

            _endFrameBarrier =
                World.GetOrCreateSystemManaged<EndFrameBarrier>();

            _prefabSystem =
                World.GetOrCreateSystemManaged<PrefabSystem>();

            _companyQuery = GetEntityQuery(
                new EntityQueryDesc
                {
                    All = new[]
                    {
                        ComponentType.ReadWrite<CompanyData>(),
                        ComponentType.ReadOnly<PrefabRef>()
                    },
                    None = new[]
                    {
                        ComponentType.ReadOnly<MovingAway>(),
                        ComponentType.ReadOnly<Deleted>(),
                        ComponentType.ReadOnly<Temp>()
                    }
                });

            Mod.Log.Info(
                "BrandRepairSystem created; waiting for a city/map load.");
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
                _repairPending = true;
                _needsFallbackArm = false;
                RepairFinishedForCurrentMap = false;

                Mod.Log.Info(
                    "Brand repair armed for next system update.");
            }
            else
            {
                _repairPending = false;
                _needsFallbackArm = true;
                _attempt = 0;
                RepairFinishedForCurrentMap = false;
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

            // Fallback for lifecycle timing changes or a system that is created
            // after the normal map-load callback has already fired.
            if (!_repairPending &&
                !RepairFinishedForCurrentMap &&
                _needsFallbackArm)
            {
                _repairPending = true;
                _needsFallbackArm = false;
                _attempt = 0;

                Mod.Log.Info(
                    "Brand repair armed by first-game-update fallback.");
            }

            if (!_repairPending)
            {
                return;
            }

            // Clear first so a successful pass cannot accidentally execute twice.
            _repairPending = false;

            try
            {
                Mod.Log.Info("Starting brand repair pass.");

                RepairStats stats = RepairBrokenBrands();

                Mod.Log.Info(
                    $"Brand repair complete. " +
                    $"Scanned={stats.Scanned}, " +
                    $"Null={stats.NullBrands}, " +
                    $"Invalid={stats.InvalidBrands}, " +
                    $"Repaired={stats.Repaired}, " +
                    $"NoCompatibleBrand={stats.NoCompatibleBrand}, " +
                    $"InvalidCompanyPrefab={stats.InvalidCompanyPrefabs}.");

                _attempt = 0;
                RepairFinishedForCurrentMap = true;
            }
            catch (Exception ex)
            {
                _attempt++;

                if (_attempt < MaxAttempts)
                {
                    _repairPending = true;

                    Mod.Log.Warn(
                        $"Brand repair attempt {_attempt} failed; " +
                        $"retrying on next update. " +
                        $"{ex.GetType().Name}: {ex.Message}");

                    return;
                }

                // Fail closed for this map. Let the orphaned-prop pass continue
                // independently instead of leaving it blocked forever.
                RepairFinishedForCurrentMap = true;
                Mod.Log.Error(ex);
                Mod.Log.Error(
                    "Brand repair disabled for this map after retry failed.");
            }
        }

        private RepairStats RepairBrokenBrands()
        {
            RepairStats stats = default;

            EntityCommandBuffer endFrameCommands =
                _endFrameBarrier.CreateCommandBuffer();

            HashSet<Entity> propertiesToRefresh =
                new HashSet<Entity>();

            NativeArray<Entity> companies =
                _companyQuery.ToEntityArray(Allocator.Temp);

            try
            {
                stats.Scanned = companies.Length;

                for (int i = 0; i < companies.Length; i++)
                {
                    Entity company = companies[i];

                    if (!EntityManager.Exists(company) ||
                        !EntityManager.HasComponent<CompanyData>(company) ||
                        !EntityManager.HasComponent<PrefabRef>(company))
                    {
                        continue;
                    }

                    CompanyData companyData =
                        EntityManager.GetComponentData<CompanyData>(company);

                    Entity currentBrand = companyData.m_Brand;

                    if (IsValidPrefab(currentBrand))
                    {
                        continue;
                    }

                    if (currentBrand == Entity.Null)
                    {
                        stats.NullBrands++;
                    }
                    else
                    {
                        stats.InvalidBrands++;
                    }

                    Entity companyPrefab =
                        EntityManager
                            .GetComponentData<PrefabRef>(company)
                            .m_Prefab;

                    if (!IsValidPrefab(companyPrefab))
                    {
                        stats.InvalidCompanyPrefabs++;

                        Mod.Log.Warn(
                            $"Company {company} has an unresolved " +
                            $"company prefab {companyPrefab}.");

                        continue;
                    }

                    if (!EntityManager.HasBuffer<CompanyBrandElement>(
                            companyPrefab))
                    {
                        stats.NoCompatibleBrand++;
                        continue;
                    }

                    DynamicBuffer<CompanyBrandElement> compatibleBrands =
                        EntityManager.GetBuffer<CompanyBrandElement>(
                            companyPrefab,
                            true);

                    Entity replacementBrand =
                        ChooseCompatibleBrand(
                            company,
                            compatibleBrands);

                    if (replacementBrand == Entity.Null)
                    {
                        stats.NoCompatibleBrand++;
                        continue;
                    }

                    Entity oldBrand = companyData.m_Brand;
                    companyData.m_Brand = replacementBrand;
                    EntityManager.SetComponentData(company, companyData);
                    stats.Repaired++;

                    Mod.Log.Info(
                        $"Repaired company {company}: " +
                        $"brand {oldBrand} -> {replacementBrand}.");

                    if (EntityManager.HasComponent<PropertyRenter>(company))
                    {
                        Entity property =
                            EntityManager
                                .GetComponentData<PropertyRenter>(company)
                                .m_Property;

                        if (property != Entity.Null &&
                            EntityManager.Exists(property) &&
                            !EntityManager.HasComponent<Deleted>(property) &&
                            !EntityManager.HasComponent<Updated>(property) &&
                            propertiesToRefresh.Add(property))
                        {
                            endFrameCommands.AddComponent<Updated>(property);
                        }
                    }
                }
            }
            finally
            {
                companies.Dispose();
            }

            return stats;
        }

        private Entity ChooseCompatibleBrand(
            Entity company,
            DynamicBuffer<CompanyBrandElement> compatibleBrands)
        {
            if (compatibleBrands.Length == 0)
            {
                return Entity.Null;
            }

            // Deterministic distribution without touching CompanyData's RNG.
            int startIndex =
                (company.Index & int.MaxValue) % compatibleBrands.Length;

            for (int offset = 0;
                 offset < compatibleBrands.Length;
                 offset++)
            {
                int index =
                    (startIndex + offset) % compatibleBrands.Length;

                Entity candidate = compatibleBrands[index].m_Brand;

                if (IsValidPrefab(candidate))
                {
                    return candidate;
                }
            }

            return Entity.Null;
        }

        private bool IsValidPrefab(Entity entity)
        {
            if (entity == Entity.Null ||
                !EntityManager.Exists(entity) ||
                EntityManager.HasComponent<Deleted>(entity))
            {
                return false;
            }

            return _prefabSystem.TryGetPrefab(
                       entity,
                       out PrefabBase prefab) &&
                   prefab != null;
        }

        private struct RepairStats
        {
            public int Scanned;
            public int NullBrands;
            public int InvalidBrands;
            public int Repaired;
            public int NoCompatibleBrand;
            public int InvalidCompanyPrefabs;
        }
    }
}
