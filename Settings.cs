using Colossal;
using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;
using System.Collections.Generic;

namespace CompanyBrandFix
{
    [FileLocation("CompanyBrandFix")]
    [SettingsUITabOrder(MainTab)]
    [SettingsUIGroupOrder(CleanupGroup)]
    [SettingsUIShowGroupName(CleanupGroup)]
    public sealed class CompanyBrandFixSetting : ModSetting
    {
        public const string MainTab = "Main";
        public const string CleanupGroup = "Cleanup";

        public CompanyBrandFixSetting(IMod mod)
            : base(mod)
        {
        }

        /// <summary>
        /// When enabled, runs one prop-only orphan scan after brand repair on map load.
        /// Broken standalone props are deleted. Building-owned broken props are
        /// removed and only their owning buildings are refreshed.
        /// </summary>
        [SettingsUISection(MainTab, CleanupGroup)]
        public bool DeleteBrokenPropsOnStartup { get; set; } = false;

        public override void SetDefaults()
        {
            DeleteBrokenPropsOnStartup = false;
        }
    }

    public sealed class LocaleEN : IDictionarySource
    {
        private readonly CompanyBrandFixSetting _setting;

        public LocaleEN(CompanyBrandFixSetting setting)
        {
            _setting = setting;
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(
            IList<IDictionaryEntryError> errors,
            Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { _setting.GetSettingsLocaleID(), "Company Brand Fix" },
                { _setting.GetOptionTabLocaleID(CompanyBrandFixSetting.MainTab), "Main" },
                { _setting.GetOptionGroupLocaleID(CompanyBrandFixSetting.CleanupGroup), "Cleanup" },
                {
                    _setting.GetOptionLabelLocaleID(
                        nameof(CompanyBrandFixSetting.DeleteBrokenPropsOnStartup)),
                    "Delete broken props on startup"
                },
                {
                    _setting.GetOptionDescLocaleID(
                        nameof(CompanyBrandFixSetting.DeleteBrokenPropsOnStartup)),
                    "After company-brand repair, scan placed props once. Broken standalone props are deleted. Building-owned broken props are removed and only affected buildings are refreshed. When disabled, no prop scan runs."
                }
            };
        }

        public void Unload()
        {
        }
    }
}
