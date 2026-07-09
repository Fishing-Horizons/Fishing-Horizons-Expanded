using FishingHorizonsExpanded.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.Objects;
using StardewValley.GameData.Shops;

namespace FishingHorizonsExpanded.Framework.Journal
{
    /// <summary>The Fisherman's Journal: an in-game book that tracks the player's fishing progress.</summary>
    /// <remarks>
    /// Milestone 1: the journal item is sold by Willy. Activating it (right-click while held)
    /// consumes the item and permanently unlocks the journal, which can then be opened with a hotkey.
    /// </remarks>
    internal sealed class JournalModule : IModule
    {
        /*********
        ** Constants
        *********/
        /// <summary>The unqualified item ID of the journal item.</summary>
        public const string ItemId = "waymeeNhaku.FHE_FishingJournal";

        /// <summary>The qualified item ID of the journal item.</summary>
        public const string QualifiedItemId = "(O)" + ItemId;

        /// <summary>The mail flag set once the player has activated the journal.</summary>
        public const string UnlockFlag = "waymeeNhaku.FHE_JournalUnlocked";

        /// <summary>The asset name of the journal item texture.</summary>
        public const string ItemTextureAssetName = "Mods/waymeeNhaku.FishingHorizonsExpanded/FishingJournal";

        /// <summary>Willy's shop ID in <c>Data/Shops</c>.</summary>
        private const string FishShopId = "FishShop";

        /// <summary>The price of the journal in Willy's shop.</summary>
        private const int ShopPrice = 2500;


        /*********
        ** Fields
        *********/
        /// <summary>The mod instance.</summary>
        private readonly ModEntry Mod;


        /*********
        ** Accessors
        *********/
        /// <inheritdoc/>
        public string Name => "Journal";

        /// <inheritdoc/>
        public bool IsEnabled => this.Mod.Config.EnableJournal;


        /*********
        ** Public methods
        *********/
        public JournalModule(ModEntry mod)
        {
            this.Mod = mod;
        }

        /// <inheritdoc/>
        public void Activate(IModHelper helper)
        {
            helper.Events.Content.AssetRequested += this.OnAssetRequested;
            helper.Events.Content.LocaleChanged += this.OnLocaleChanged;
            helper.Events.Input.ButtonPressed += this.OnButtonPressed;
            helper.Events.Input.ButtonsChanged += this.OnButtonsChanged;
        }

        /// <summary>Whether the current player has unlocked the journal.</summary>
        public static bool IsUnlocked()
        {
            return Game1.player?.mailReceived.Contains(UnlockFlag) == true;
        }


        /*********
        ** Private methods
        *********/
        /// <summary>Add the journal item, its texture, and Willy's shop entry.</summary>
        private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
        {
            if (!this.IsEnabled)
                return;

            // item definition
            if (e.NameWithoutLocale.IsEquivalentTo("Data/Objects"))
            {
                e.Edit(asset =>
                {
                    var data = asset.AsDictionary<string, ObjectData>().Data;
                    data[ItemId] = new ObjectData
                    {
                        Name = ItemId,
                        DisplayName = this.Mod.Helper.Translation.Get("item.journal.name"),
                        Description = this.Mod.Helper.Translation.Get("item.journal.description"),
                        Type = "Basic",
                        Category = -999,
                        Price = 0,
                        Texture = ItemTextureAssetName,
                        SpriteIndex = 0,
                        ExcludeFromShippingCollection = true,
                        ExcludeFromRandomSale = true
                    };
                });
            }

            // item texture (placeholder art until the real one is drawn)
            else if (e.NameWithoutLocale.IsEquivalentTo(ItemTextureAssetName))
            {
                e.LoadFromModFile<Texture2D>("assets/fishing-journal.png", AssetLoadPriority.Exclusive);
            }

            // Willy's shop entry (hidden once the journal is unlocked)
            else if (e.NameWithoutLocale.IsEquivalentTo("Data/Shops"))
            {
                e.Edit(asset =>
                {
                    var data = asset.AsDictionary<string, ShopData>().Data;
                    if (!data.TryGetValue(FishShopId, out ShopData? shop))
                        return;

                    shop.Items.Add(new ShopItemData
                    {
                        Id = ItemId,
                        ItemId = QualifiedItemId,
                        Price = ShopPrice,
                        Condition = $"!PLAYER_HAS_MAIL Current {UnlockFlag}",
                        AvailableStock = 1,
                        AvailableStockLimit = LimitedStockMode.Player
                    });
                });
            }
        }

        /// <summary>Reload the item data so its name/description use the new language.</summary>
        private void OnLocaleChanged(object? sender, LocaleChangedEventArgs e)
        {
            this.Mod.Helper.GameContent.InvalidateCache("Data/Objects");
        }

        /// <summary>Handle activating the held journal item.</summary>
        private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
        {
            if (!this.IsEnabled || !Context.IsPlayerFree)
                return;

            if (!e.Button.IsActionButton() && !e.Button.IsUseToolButton())
                return;

            if (Game1.player.ActiveObject?.QualifiedItemId != QualifiedItemId)
                return;

            this.Mod.Helper.Input.Suppress(e.Button);

            if (IsUnlocked())
            {
                // already unlocked (e.g. bought a second copy) — just open the journal
                this.OpenJournal();
                return;
            }

            // consume the item and unlock the journal ability
            Game1.player.reduceActiveItemByOne();
            Game1.player.mailReceived.Add(UnlockFlag);
            Game1.playSound("newRecipe");
            Game1.addHUDMessage(HUDMessage.ForCornerTextbox(
                this.Mod.Helper.Translation.Get("message.journal-unlocked", new { key = this.Mod.Config.JournalKey.ToString() })
            ));
        }

        /// <summary>Handle the journal hotkey.</summary>
        private void OnButtonsChanged(object? sender, ButtonsChangedEventArgs e)
        {
            if (!this.IsEnabled || !Context.IsWorldReady)
                return;

            if (!this.Mod.Config.JournalKey.JustPressed())
                return;

            // close the journal if it's already open
            if (Game1.activeClickableMenu is JournalMenu openMenu)
            {
                openMenu.exitThisMenu();
                return;
            }

            if (!Context.IsPlayerFree || !IsUnlocked())
                return;

            this.OpenJournal();
        }

        /// <summary>Open the journal menu.</summary>
        private void OpenJournal()
        {
            Game1.playSound("bigSelect");
            Game1.activeClickableMenu = new JournalMenu(this.Mod.Helper.Translation, this.Mod.Helper.ModRegistry);
        }
    }
}
