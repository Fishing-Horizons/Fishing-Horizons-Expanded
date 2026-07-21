using StardewModdingAPI;
using StardewModdingAPI.Utilities;

namespace FishingHorizonsExpanded
{
    /// <summary>Mod configuration. Each mechanic module has its own enable toggle.</summary>
    public sealed class ModConfig
    {
        /*********
        ** Journal module
        *********/
        /// <summary>Whether the Fisherman's Journal module is enabled.</summary>
        public bool EnableJournal { get; set; } = true;

        /// <summary>The key which opens the journal (once unlocked).</summary>
        public KeybindList JournalKey { get; set; } = new(SButton.J);


        /*********
        ** Angler's Sense module
        *********/
        /// <summary>Whether the Angler's Sense HUD panel is enabled.</summary>
        public bool EnableFishAssistant { get; set; } = true;

        /// <summary>The key which toggles the Angler's Sense panel.</summary>
        public KeybindList AssistantKey { get; set; } = new(SButton.H);


        /*********
        ** Mine fishing module
        *********/
        /// <summary>Whether custom <c>Data/Locations</c> fish can spawn on the lava mine floors (80–119).</summary>
        public bool EnableLavaFloorFish { get; set; } = true;
    }
}
