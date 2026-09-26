using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;

namespace CompanyBrandFix
{
    public sealed class Mod : IMod
    {
        public const string Name = "CompanyBrandFix";
        public const string Version = "0.2.2-test";

        public static readonly ILog Log = LogManager.GetLogger(Name)
            .SetShowsErrorsInUI(false)
            .SetShowsStackTraceAboveLevels(Level.Error);

        public static CompanyBrandFixSetting Settings { get; private set; }

        public void OnLoad(UpdateSystem updateSystem)
        {
            Log.Info($"{Name} {Version} loading.");

            Settings = new CompanyBrandFixSetting(this);
            GameManager.instance.localizationManager.AddSource(
                "en-US",
                new LocaleEN(Settings));

            AssetDatabase.global.LoadSettings(
                Name,
                Settings,
                new CompanyBrandFixSetting(this));

            Settings.RegisterInOptionsUI();

            updateSystem.UpdateAt<BrandRepairSystem>(
                SystemUpdatePhase.PostSimulation);

            updateSystem.UpdateAfter<OrphanedPropRepairSystem, BrandRepairSystem>(
                SystemUpdatePhase.PostSimulation);
        }

        public void OnDispose()
        {
            if (Settings != null)
            {
                Settings.UnregisterInOptionsUI();
                Settings = null;
            }

            Log.Info($"{Name} disposed.");
        }
    }
}
