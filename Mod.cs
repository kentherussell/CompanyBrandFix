using Colossal.Logging;
using Game;
using Game.Modding;

namespace CompanyBrandFix
{
    public sealed class Mod : IMod
    {
        public const string Name = "CompanyBrandFix";
        public const string Version = "0.1.2";

        public static readonly ILog Log = LogManager.GetLogger(Name)
            .SetShowsErrorsInUI(false)
            .SetShowsStackTraceAboveLevels(Level.Error);

        public void OnLoad(UpdateSystem updateSystem)
        {
            Log.Info($"{Name} {Version} loading.");
            updateSystem.UpdateAt<BrandRepairSystem>(SystemUpdatePhase.GameSimulation);
        }

        public void OnDispose()
        {
            Log.Info($"{Name} disposed.");
        }
    }
}
