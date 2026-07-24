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


        /*********
        ** Crab pots module
        *********/
        /// <summary>Whether trap fish can use a location name (or <c>Ocean</c>) as their water type in <c>Data/Fish</c>, gating them to crab pots in that location.</summary>
        public bool EnableTrapFishLocationTypes { get; set; } = true;

        /// <summary>Whether crab pots can be placed on water tiles in the mines (persisted across floor regeneration).</summary>
        public bool EnableCaveCrabPots { get; set; } = true;

        /// <summary>Whether crab pots also work in the lava mine area (80–119). Off by default — planned to be unlocked by the obsidian rod set instead.</summary>
        public bool EnableLavaCrabPots { get; set; } = false;


        /*********
        ** Rods module
        *********/
        /// <summary>Whether the golden rod is sold by Willy (always-gold-quality fish, between the fiberglass and iridium rods).</summary>
        public bool EnableGoldenRod { get; set; } = true;


        /*********
        ** Tackle module
        *********/
        /// <summary>Whether the double hook tackle is sold by Willy (chance of a second fish biting mid-minigame).</summary>
        public bool EnableDoubleHook { get; set; } = true;

        /// <summary>The chance (0–1) that a second fish bites during a catch while the double hook is equipped.</summary>
        public float DoubleHookChance { get; set; } = 0.4f;
    }
}
