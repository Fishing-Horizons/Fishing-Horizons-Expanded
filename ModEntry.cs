using System.Collections.Generic;
using FishingHorizonsExpanded.Framework;
using FishingHorizonsExpanded.Framework.Assistant;
using FishingHorizonsExpanded.Framework.Journal;
using FishingHorizonsExpanded.Framework.Mines;
using StardewModdingAPI;
using StardewModdingAPI.Events;

namespace FishingHorizonsExpanded
{
    /// <summary>The mod entry point. Loads config and activates mechanic modules.</summary>
    public sealed class ModEntry : Mod
    {
        /*********
        ** Fields
        *********/
        /// <summary>The mod configuration.</summary>
        internal ModConfig Config = null!;

        /// <summary>All mechanic modules.</summary>
        private readonly List<IModule> Modules = new();


        /*********
        ** Public methods
        *********/
        /// <inheritdoc/>
        public override void Entry(IModHelper helper)
        {
            this.Config = helper.ReadConfig<ModConfig>();

            // register modules (new mechanics get added here)
            this.Modules.Add(new JournalModule(this));
            this.Modules.Add(new MineFishingModule(this));

            foreach (IModule module in this.Modules)
            {
                module.Activate(helper);
                this.Monitor.Log($"Module '{module.Name}' activated (enabled: {module.IsEnabled}).");
            }

            helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
        }


        /*********
        ** Private methods
        *********/
        /// <summary>Register the config menu with Generic Mod Config Menu, if installed.</summary>
        private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
        {
            GmcmIntegration.Register(this);
        }
    }
}
