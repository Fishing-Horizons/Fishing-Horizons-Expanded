using StardewModdingAPI;

namespace FishingHorizonsExpanded.Framework.CrabPots
{
    /// <summary>Extends crab pots: location-gated trap fish, and pots that work in the mines (and lava).</summary>
    /// <remarks>
    /// Three features, individually toggleable:
    ///
    /// <list type="bullet">
    /// <item><b>Location trap fish</b> — content packs can use a location name (e.g. <c>Woods</c>, <c>Mountain</c>,
    /// <c>IslandWest</c>) or <c>Ocean</c> as the water type of a <c>Data/Fish</c> trap entry, and the fish is then
    /// only caught by crab pots in that location. This makes the pack's King Crab, Blue Crayfish, River Shrimp and
    /// Firefly Crab actually catchable (vanilla only matches <c>freshwater</c>/<c>ocean</c>).</item>
    /// <item><b>Cave crab pots</b> — pots can be placed on water tiles in the mines and are persisted across the
    /// floor regeneration via <see cref="CaveCrabPotManager"/>. They catch <c>Cave</c>-type trap fish.</item>
    /// <item><b>Lava crab pots</b> — same for the lava floors (80–119) with <c>Lava</c>-type trap fish. Off by
    /// default: per the design doc this will later be unlocked by the obsidian rod set instead of a config flag.</item>
    /// </list>
    /// </remarks>
    internal sealed class CrabPotsModule : IModule
    {
        /*********
        ** Fields
        *********/
        /// <summary>The mod instance.</summary>
        private readonly ModEntry Mod;

        /// <summary>Manages persistence and daily catches for pots in the mines.</summary>
        private CaveCrabPotManager? Manager;


        /*********
        ** Accessors
        *********/
        /// <inheritdoc/>
        public string Name => "Crab Pots";

        /// <inheritdoc/>
        public bool IsEnabled => this.Mod.Config.EnableTrapFishLocationTypes || this.Mod.Config.EnableCaveCrabPots;


        /*********
        ** Public methods
        *********/
        public CrabPotsModule(ModEntry mod)
        {
            this.Mod = mod;
        }

        /// <inheritdoc/>
        public void Activate(IModHelper helper)
        {
            CrabPotPatches.Apply(
                modId: this.Mod.ModManifest.UniqueID,
                monitor: this.Mod.Monitor,
                trapTypesEnabled: () => this.Mod.Config.EnableTrapFishLocationTypes,
                cavePotsEnabled: () => this.Mod.Config.EnableCaveCrabPots,
                lavaPotsEnabled: () => this.Mod.Config.EnableCaveCrabPots && this.Mod.Config.EnableLavaCrabPots
            );

            this.Manager = new CaveCrabPotManager(
                mod: this.Mod,
                isEnabled: () => this.Mod.Config.EnableCaveCrabPots,
                lavaEnabled: () => this.Mod.Config.EnableCaveCrabPots && this.Mod.Config.EnableLavaCrabPots
            );
            this.Manager.Activate(helper);
        }
    }
}
