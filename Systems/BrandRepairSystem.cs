using Colossal.Serialization.Entities;
using Game;
using Game.Agents;
using Game.Buildings;
using Game.Common;
using Game.Companies;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Simulation;
using Game.Tools;
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.Scripting;

namespace CompanyBrandFix
{
    /// <summary>
    /// Repairs CompanyData.m_Brand when it is null or points to an invalid/deleted entity.
    ///
    /// A repair is scheduled after every city/map load. The system also has a
    /// first-game-tick fallback so it still works if it is created after the
    /// normal loading-complete lifecycle callback has already fired.
    /// </summary>
    public partial class BrandRepairSystem : GameSystemBase
    {
        // CS2 uses 262,144 simulation ticks per in-game day.
        // 2,048 ticks is about 11.25 in-game minutes at the vanilla time scale.
        private const uint InitialDelayTicks = 2048;
        private const uint RetryDelayTicks = 2048;

        // While waiting, this is only a couple of integer/boolean checks.
        // 2,048 / 512 = four wake-ups before the normal repair point.
        private const int CheckIntervalTicks = 512;

        // Initial attempt + one retry. After that, fail closed for this map.
        private const int MaxAttempts = 2;

        private SimulationSystem _simulationSystem;
        private EndFrameBarrier _endFrameBarrier;
        private EntityQuery _companyQuery;

        private uint _dueFrame;
        private int _attempt;
        private bool _repairScheduled;

        // Starts true intentionally. Some mods/systems can be created after
        // OnGameLoadingComplete has already fired; the first simulation tick in
        // Game mode then becomes a safe fallback trigger.
        private bool _needsFallbackSchedule = true;

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return CheckIntervalTicks;
        }

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();

            _simulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            _endFrameBarrier = World.GetOrCreateSystemManaged<EndFrameBarrier>();

            _companyQuery = GetEntityQuery(new EntityQueryDesc
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

            // IMPORTANT: do not disable this system here. If it is disabled before
            // the game's load lifecycle reaches it, the repair may never be armed.
            // The idle OnUpdate path is intentionally almost free.
            Mod.Log.Info("BrandRepairSystem created; waiting for a city/map load.");
        }

        [Preserve]
        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            if (GameModeExtensions.IsGame(mode))
            {
                ScheduleRepair("map load callback");
            }
            else
            {
                // Returning to menus/editor cancels any old schedule. Keep the
                // fallback armed so entering a city still works even if a future
                // game update changes lifecycle callback timing.
                _repairScheduled = false;
                _needsFallbackSchedule = true;
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

            uint currentFrame = _simulationSystem.frameIndex;

            // Fallback for late-created systems or lifecycle/API timing changes.
            if (!_repairScheduled)
            {
                if (_needsFallbackSchedule)
                {
                    ScheduleRepair("first game tick fallback");
                }
                else
                {
                    return;
                }
            }

            if (!HasReached(currentFrame, _dueFrame))
            {
                return;
            }

            try
            {
                Mod.Log.Info($"Starting brand repair pass at simulation frame {currentFrame}.");

                RepairStats stats = RepairBrokenBrands();

                Mod.Log.Info(
                    $"Brand repair complete. Scanned={stats.Scanned}, " +
                    $"Null={stats.NullBrands}, Invalid={stats.InvalidBrands}, " +
                    $"Repaired={stats.Repaired}, NoCompatibleBrand={stats.NoCompatibleBrand}.");

                // Remain enabled so future map-load callbacks cannot be missed,
                // but become an extremely cheap no-op until the next load.
                _repairScheduled = false;
                _needsFallbackSchedule = false;
                _attempt = 0;
            }
            catch (Exception ex)
            {
                _attempt++;

                if (_attempt < MaxAttempts)
                {
                    _dueFrame = unchecked(currentFrame + RetryDelayTicks);
                    Mod.Log.Warn(
                        $"Brand repair attempt {_attempt} failed; retrying once after " +
                        $"{RetryDelayTicks} simulation ticks. {ex.GetType().Name}: {ex.Message}");
                    return;
                }

                // Fail closed for this map. The next real map-load callback will
                // schedule a fresh attempt; no damaged/partial values are reverted.
                Mod.Log.Error(ex);
                Mod.Log.Error("Brand repair disabled for this map after the retry failed.");
                _repairScheduled = false;
                _needsFallbackSchedule = false;
            }
        }

        private void ScheduleRepair(string reason)
        {
            _attempt = 0;
            _dueFrame = unchecked(_simulationSystem.frameIndex + InitialDelayTicks);
            _repairScheduled = true;
            _needsFallbackSchedule = false;

            Mod.Log.Info(
                $"Brand repair scheduled ({reason}). CurrentFrame={_simulationSystem.frameIndex}, " +
                $"DueFrame={_dueFrame}, DelayTicks={InitialDelayTicks}.");
        }

        private RepairStats RepairBrokenBrands()
        {
            RepairStats stats = default;
            EntityCommandBuffer endFrameCommands = _endFrameBarrier.CreateCommandBuffer();
            HashSet<Entity> propertiesToRefresh = new HashSet<Entity>();

            NativeArray<Entity> companies = _companyQuery.ToEntityArray(Allocator.Temp);
            try
            {
                stats.Scanned = companies.Length;

                for (int i = 0; i < companies.Length; i++)
                {
                    Entity company = companies[i];

                    // Queries can become stale if another system changed an entity between
                    // materialization and access. Treat that as a skip, never as corruption.
                    if (!EntityManager.Exists(company) ||
                        !EntityManager.HasComponent<CompanyData>(company) ||
                        !EntityManager.HasComponent<PrefabRef>(company))
                    {
                        continue;
                    }

                    CompanyData companyData = EntityManager.GetComponentData<CompanyData>(company);
                    Entity currentBrand = companyData.m_Brand;

                    // A non-null ECS entity is not necessarily a valid live entity. Treat
                    // stale/deleted brand references as broken too, but leave every valid
                    // existing brand untouched.
                    bool brandIsValid =
                        currentBrand != Entity.Null &&
                        EntityManager.Exists(currentBrand) &&
                        !EntityManager.HasComponent<Deleted>(currentBrand);

                    if (brandIsValid)
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

                    Entity companyPrefab = EntityManager.GetComponentData<PrefabRef>(company).m_Prefab;
                    if (companyPrefab == Entity.Null ||
                        !EntityManager.Exists(companyPrefab) ||
                        !EntityManager.HasBuffer<CompanyBrandElement>(companyPrefab))
                    {
                        stats.NoCompatibleBrand++;
                        continue;
                    }

                    DynamicBuffer<CompanyBrandElement> compatibleBrands =
                        EntityManager.GetBuffer<CompanyBrandElement>(companyPrefab, true);

                    Entity brand = ChooseCompatibleBrand(company, compatibleBrands);
                    if (brand == Entity.Null)
                    {
                        stats.NoCompatibleBrand++;
                        continue;
                    }

                    companyData.m_Brand = brand;
                    EntityManager.SetComponentData(company, companyData);
                    stats.Repaired++;

                    // Change Company marks the property Updated after a brand/company change
                    // so billboard visuals can refresh. Do the same, but defer the structural
                    // change to EndFrameBarrier and deduplicate properties.
                    if (EntityManager.HasComponent<PropertyRenter>(company))
                    {
                        Entity property = EntityManager.GetComponentData<PropertyRenter>(company).m_Property;
                        if (property != Entity.Null &&
                            EntityManager.Exists(property) &&
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

        private Entity ChooseCompatibleBrand(Entity company, DynamicBuffer<CompanyBrandElement> compatibleBrands)
        {
            if (compatibleBrands.Length == 0)
            {
                return Entity.Null;
            }

            // Deterministic distribution without touching CompanyData's simulation RNG.
            int startIndex = (company.Index & int.MaxValue) % compatibleBrands.Length;

            for (int offset = 0; offset < compatibleBrands.Length; offset++)
            {
                int index = (startIndex + offset) % compatibleBrands.Length;
                Entity candidate = compatibleBrands[index].m_Brand;

                if (candidate != Entity.Null &&
                    EntityManager.Exists(candidate) &&
                    !EntityManager.HasComponent<Deleted>(candidate))
                {
                    return candidate;
                }
            }

            return Entity.Null;
        }

        // Wrap-safe comparison for uint simulation frame counters.
        private static bool HasReached(uint currentFrame, uint dueFrame)
        {
            return unchecked((int)(currentFrame - dueFrame)) >= 0;
        }

        private struct RepairStats
        {
            public int Scanned;
            public int NullBrands;
            public int InvalidBrands;
            public int Repaired;
            public int NoCompatibleBrand;
        }
    }
}
