using System;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;

namespace FishingHorizonsExpanded.Framework
{
    /// <summary>The subset of the Generic Mod Config Menu API used by this mod.</summary>
    public interface IGenericModConfigMenuApi
    {
        void Register(IManifest mod, Action reset, Action save, bool titleScreenOnly = false);
        void AddSectionTitle(IManifest mod, Func<string> text, Func<string>? tooltip = null);
        void AddBoolOption(IManifest mod, Func<bool> getValue, Action<bool> setValue, Func<string> name, Func<string>? tooltip = null, string? fieldId = null);
        void AddKeybindList(IManifest mod, Func<KeybindList> getValue, Action<KeybindList> setValue, Func<string> name, Func<string>? tooltip = null, string? fieldId = null);
    }

    /// <summary>Registers the mod config with Generic Mod Config Menu, if it's installed.</summary>
    internal static class GmcmIntegration
    {
        public static void Register(FishingHorizonsExpanded.ModEntry mod)
        {
            var api = mod.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (api is null)
                return;

            var i18n = mod.Helper.Translation;

            api.Register(
                mod: mod.ModManifest,
                reset: () => mod.Config = new ModConfig(),
                save: () => mod.Helper.WriteConfig(mod.Config)
            );

            api.AddSectionTitle(mod.ModManifest, () => i18n.Get("config.section.journal"));
            api.AddBoolOption(
                mod.ModManifest,
                getValue: () => mod.Config.EnableJournal,
                setValue: value => mod.Config.EnableJournal = value,
                name: () => i18n.Get("config.enable-journal.name"),
                tooltip: () => i18n.Get("config.enable-journal.tooltip")
            );
            api.AddKeybindList(
                mod.ModManifest,
                getValue: () => mod.Config.JournalKey,
                setValue: value => mod.Config.JournalKey = value,
                name: () => i18n.Get("config.journal-key.name"),
                tooltip: () => i18n.Get("config.journal-key.tooltip")
            );

            api.AddSectionTitle(mod.ModManifest, () => i18n.Get("config.section.assistant"));
            api.AddBoolOption(
                mod.ModManifest,
                getValue: () => mod.Config.EnableFishAssistant,
                setValue: value => mod.Config.EnableFishAssistant = value,
                name: () => i18n.Get("config.enable-assistant.name"),
                tooltip: () => i18n.Get("config.enable-assistant.tooltip")
            );
            api.AddKeybindList(
                mod.ModManifest,
                getValue: () => mod.Config.AssistantKey,
                setValue: value => mod.Config.AssistantKey = value,
                name: () => i18n.Get("config.assistant-key.name"),
                tooltip: () => i18n.Get("config.assistant-key.tooltip")
            );

            api.AddSectionTitle(mod.ModManifest, () => i18n.Get("config.section.mines"));
            api.AddBoolOption(
                mod.ModManifest,
                getValue: () => mod.Config.EnableLavaFloorFish,
                setValue: value => mod.Config.EnableLavaFloorFish = value,
                name: () => i18n.Get("config.enable-lava-fish.name"),
                tooltip: () => i18n.Get("config.enable-lava-fish.tooltip")
            );

            api.AddSectionTitle(mod.ModManifest, () => i18n.Get("config.section.crab-pots"));
            api.AddBoolOption(
                mod.ModManifest,
                getValue: () => mod.Config.EnableTrapFishLocationTypes,
                setValue: value => mod.Config.EnableTrapFishLocationTypes = value,
                name: () => i18n.Get("config.enable-trap-fish-locations.name"),
                tooltip: () => i18n.Get("config.enable-trap-fish-locations.tooltip")
            );
            api.AddBoolOption(
                mod.ModManifest,
                getValue: () => mod.Config.EnableCaveCrabPots,
                setValue: value => mod.Config.EnableCaveCrabPots = value,
                name: () => i18n.Get("config.enable-cave-crab-pots.name"),
                tooltip: () => i18n.Get("config.enable-cave-crab-pots.tooltip")
            );
            api.AddBoolOption(
                mod.ModManifest,
                getValue: () => mod.Config.EnableLavaCrabPots,
                setValue: value => mod.Config.EnableLavaCrabPots = value,
                name: () => i18n.Get("config.enable-lava-crab-pots.name"),
                tooltip: () => i18n.Get("config.enable-lava-crab-pots.tooltip")
            );

            api.AddSectionTitle(mod.ModManifest, () => i18n.Get("config.section.rods"));
            api.AddBoolOption(
                mod.ModManifest,
                getValue: () => mod.Config.EnableGoldenRod,
                setValue: value => mod.Config.EnableGoldenRod = value,
                name: () => i18n.Get("config.enable-golden-rod.name"),
                tooltip: () => i18n.Get("config.enable-golden-rod.tooltip")
            );
        }
    }
}
