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
    }
}
