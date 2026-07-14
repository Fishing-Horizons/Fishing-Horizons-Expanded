using FishingHorizonsExpanded.Framework.Journal;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;

namespace FishingHorizonsExpanded.Framework.Assistant
{
    /// <summary>The Angler's Sense module: a toggleable HUD panel showing which fish can bite at the current location — right now, later today (with the exact time window), or not today (with the readable reason).</summary>
    /// <remarks>
    /// The panel unlocks together with the Fisherman's Journal (it represents the same accumulated knowledge),
    /// unless the journal module is disabled, in which case it's always available.
    /// Uncaught fish appear as silhouettes so the journal's discovery mechanic isn't spoiled; a gold "!"
    /// marks silhouettes that can bite right now.
    /// </remarks>
    internal sealed class FishAssistantModule : IModule
    {
        /*********
        ** Fields
        *********/
        /// <summary>The mod instance.</summary>
        private readonly ModEntry Mod;

        /// <summary>The overlay renderer (created on activation).</summary>
        private AssistantOverlay? Overlay;

        /// <summary>Whether the panel is currently shown (per split-screen player).</summary>
        private readonly PerScreen<bool> Visible = new(() => false);


        /*********
        ** Accessors
        *********/
        /// <inheritdoc/>
        public string Name => "FishAssistant";

        /// <inheritdoc/>
        public bool IsEnabled => this.Mod.Config.EnableFishAssistant;


        /*********
        ** Public methods
        *********/
        public FishAssistantModule(ModEntry mod)
        {
            this.Mod = mod;
        }

        /// <inheritdoc/>
        public void Activate(IModHelper helper)
        {
            this.Overlay = new AssistantOverlay(helper.Translation, this.Mod.Monitor);

            helper.Events.Input.ButtonsChanged += this.OnButtonsChanged;
            helper.Events.Display.RenderedHud += this.OnRenderedHud;
            helper.Events.GameLoop.ReturnedToTitle += this.OnReturnedToTitle;
            helper.Events.Content.LocaleChanged += this.OnLocaleChanged;
        }


        /*********
        ** Private methods
        *********/
        /// <summary>Toggle the panel when the hotkey is pressed.</summary>
        private void OnButtonsChanged(object? sender, ButtonsChangedEventArgs e)
        {
            if (!this.IsEnabled || !Context.IsWorldReady)
                return;

            if (!this.Mod.Config.AssistantKey.JustPressed())
                return;

            // the panel is part of the journal's knowledge: it unlocks together with it
            if (!this.IsUnlocked())
            {
                if (Context.IsPlayerFree)
                    Game1.showRedMessage(this.Mod.Helper.Translation.Get("hud.assistant.locked"));
                return;
            }

            this.Visible.Value = !this.Visible.Value;
            Game1.playSound(this.Visible.Value ? "shwip" : "smallSelect");
        }

        /// <summary>Draw the panel over the HUD.</summary>
        private void OnRenderedHud(object? sender, RenderedHudEventArgs e)
        {
            if (!this.IsEnabled || !this.Visible.Value || !Context.IsWorldReady)
                return;

            if (Game1.eventUp || Game1.activeClickableMenu is not null || !this.IsUnlocked())
                return;

            this.Overlay?.Draw(e.SpriteBatch);
        }

        /// <summary>Whether the current player can use the panel.</summary>
        private bool IsUnlocked()
        {
            // if the journal module is turned off, the assistant works standalone
            return !this.Mod.Config.EnableJournal || JournalModule.IsUnlocked();
        }

        /// <summary>Reset the cached data when the save is closed.</summary>
        private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
        {
            this.Visible.Value = false;
            this.Overlay?.InvalidateCache();
        }

        /// <summary>Reset the cached data so reasons and names use the new language.</summary>
        private void OnLocaleChanged(object? sender, LocaleChangedEventArgs e)
        {
            this.Overlay?.InvalidateCache();
        }
    }
}
