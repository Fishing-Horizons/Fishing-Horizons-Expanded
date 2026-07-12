using StardewModdingAPI;

namespace FishingHorizonsExpanded.Framework.Mines
{
    /// <summary>Lets content packs add custom fish to the lava mine floors (80–119) via <c>Data/Locations</c>.</summary>
    /// <remarks>
    /// In vanilla 1.6, <c>MineShaft.getFish</c> returns a lava eel, cave jelly, or trash on the lava floors
    /// <em>before</em> it ever reads the <c>UndergroundMine</c> entry in <c>Data/Locations</c> — so data-based
    /// custom fish can never spawn there. This module patches that gap; all other floors already work without it.
    /// </remarks>
    internal sealed class MineFishingModule : IModule
    {
        /*********
        ** Fields
        *********/
        /// <summary>The mod instance.</summary>
        private readonly ModEntry Mod;


        /*********
        ** Accessors
        *********/
        /// <inheritdoc/>
        public string Name => "Mine Fishing";

        /// <inheritdoc/>
        public bool IsEnabled => this.Mod.Config.EnableLavaFloorFish;


        /*********
        ** Public methods
        *********/
        public MineFishingModule(ModEntry mod)
        {
            this.Mod = mod;
        }

        /// <inheritdoc/>
        public void Activate(IModHelper helper)
        {
            MineFishingPatches.Apply(this.Mod.ModManifest.UniqueID, this.Mod.Monitor, () => this.IsEnabled);
        }
    }
}
