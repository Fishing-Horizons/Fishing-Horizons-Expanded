using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley.GameData.Objects;
using StardewValley.GameData.Shops;

namespace FishingHorizonsExpanded.Framework.Tackle
{
    /// <summary>Custom tackle items (the "hook" slot of the modular rod system from the design doc).</summary>
    /// <remarks>
    /// Current tackle:
    /// <list type="bullet">
    /// <item><b>Double Hook</b> — while equipped, there's a chance that a second fish bites mid-minigame
    /// (once the catch bar is half full). On any rod it gives a second-fish chance; on the Feeder Rod
    /// it enables a <em>third</em> fish (the Feeder Rod's own mechanic covers the second).
    /// See <see cref="DoubleHookPatches"/>.</item>
    /// </list>
    /// Tackle items are plain <c>Data/Objects</c> entries with category -22, so the vanilla iridium rod
    /// (and our Feeder Rod) can equip them and the standard tackle durability applies. Uninstall-safe:
    /// removing the mod turns the item into an error item but never corrupts the save.
    /// </remarks>
    internal sealed class TackleModule : IModule
    {
        /*********
        ** Constants
        *********/
        /// <summary>The unqualified object ID of the double hook.</summary>
        public const string DoubleHookId = "waymeeNhaku.FHE_DoubleHook";

        /// <summary>The qualified object ID of the double hook.</summary>
        public const string DoubleHookQualifiedId = "(O)" + DoubleHookId;

        /// <summary>The asset name of the double hook texture.</summary>
        public const string DoubleHookTextureAssetName = "Mods/waymeeNhaku.FishingHorizonsExpanded/DoubleHook";

        /// <summary>Willy's shop ID in <c>Data/Shops</c>.</summary>
        private const string FishShopId = "FishShop";

        /// <summary>The double hook's price in Willy's shop (vanilla tackle is 500–1 000g).</summary>
        private const int DoubleHookShopPrice = 1500;

        /// <summary>The fishing level required before Willy sells the double hook (the iridium rod that can equip tackle unlocks at 6).</summary>
        private const int DoubleHookFishingLevel = 6;


        /*********
        ** Fields
        *********/
        /// <summary>The mod instance.</summary>
        private readonly ModEntry Mod;


        /*********
        ** Accessors
        *********/
        /// <inheritdoc/>
        public string Name => "Tackle";

        /// <inheritdoc/>
        public bool IsEnabled => this.Mod.Config.EnableDoubleHook;


        /*********
        ** Public methods
        *********/
        public TackleModule(ModEntry mod)
        {
            this.Mod = mod;
        }

        /// <inheritdoc/>
        public void Activate(IModHelper helper)
        {
            DoubleHookPatches.Apply(
                this.Mod.ModManifest.UniqueID,
                this.Mod.Monitor,
                () => this.IsEnabled || this.Mod.Config.EnableFeederRod, // active if either mechanic is on
                () => this.Mod.Config.DoubleHookChance,
                () => this.Mod.Config.FeederRodSecondFishChance,
                helper.GameContent
            );

            helper.Events.Content.AssetRequested += this.OnAssetRequested;
            helper.Events.Content.LocaleChanged += this.OnLocaleChanged;
        }


        /*********
        ** Private methods
        *********/
        /// <summary>Add the double hook object, its texture, extra fish textures, and Willy's shop entry.</summary>
        private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
        {
            // --- Double Hook object ---
            if (this.IsEnabled && e.NameWithoutLocale.IsEquivalentTo("Data/Objects"))
            {
                e.Edit(asset =>
                {
                    var data = asset.AsDictionary<string, ObjectData>().Data;
                    data[DoubleHookId] = new ObjectData
                    {
                        Name = "DoubleHook",
                        DisplayName = this.Mod.Helper.Translation.Get("item.double-hook.name"),
                        Description = this.Mod.Helper.Translation.Get("item.double-hook.description"),
                        Type = "interactive",
                        Category = -22, // tackle
                        Price = DoubleHookShopPrice / 2,
                        Texture = DoubleHookTextureAssetName,
                        SpriteIndex = 0,
                        ExcludeFromRandomSale = true
                    };
                });
            }

            // --- Double Hook texture ---
            else if (this.IsEnabled && e.NameWithoutLocale.IsEquivalentTo(DoubleHookTextureAssetName))
            {
                e.LoadFromModFile<Texture2D>("assets/double-hook.png", AssetLoadPriority.Exclusive);
            }

            // --- Second fish placeholder texture ---
            else if (e.NameWithoutLocale.IsEquivalentTo(DoubleHookPatches.SecondFishTextureAsset))
            {
                e.LoadFromModFile<Texture2D>("assets/second-fish.png", AssetLoadPriority.Exclusive);
            }

            // --- Third fish placeholder texture ---
            else if (e.NameWithoutLocale.IsEquivalentTo(DoubleHookPatches.ThirdFishTextureAsset))
            {
                e.LoadFromModFile<Texture2D>("assets/third-fish.png", AssetLoadPriority.Exclusive);
            }

            // --- Widened translucent bubble sprites (extra fish) ---
            else if (e.NameWithoutLocale.IsEquivalentTo(DoubleHookPatches.TwoFishBubbleAsset))
            {
                e.LoadFromModFile<Texture2D>("assets/2-fish-backwindow-bp.png", AssetLoadPriority.Exclusive);
            }
            else if (e.NameWithoutLocale.IsEquivalentTo(DoubleHookPatches.ThreeFishBubbleAsset))
            {
                e.LoadFromModFile<Texture2D>("assets/3-fish-backwindow-bp.png", AssetLoadPriority.Exclusive);
            }

            // --- Widened wooden frame sprites (extra fish) ---
            else if (e.NameWithoutLocale.IsEquivalentTo(DoubleHookPatches.TwoFishFrameAsset))
            {
                e.LoadFromModFile<Texture2D>("assets/2-fish-frame.png", AssetLoadPriority.Exclusive);
            }
            else if (e.NameWithoutLocale.IsEquivalentTo(DoubleHookPatches.ThreeFishFrameAsset))
            {
                e.LoadFromModFile<Texture2D>("assets/3-fish-frame.png", AssetLoadPriority.Exclusive);
            }

            // --- Multi-slot sonar sprites (extra fish) ---
            else if (e.NameWithoutLocale.IsEquivalentTo(DoubleHookPatches.TwoFishSonarAsset))
            {
                e.LoadFromModFile<Texture2D>("assets/2-fish-sonar.png", AssetLoadPriority.Exclusive);
            }
            else if (e.NameWithoutLocale.IsEquivalentTo(DoubleHookPatches.ThreeFishSonarAsset))
            {
                e.LoadFromModFile<Texture2D>("assets/3-fish-sonar.png", AssetLoadPriority.Exclusive);
            }

            // --- Willy's shop entry ---
            else if (this.IsEnabled && e.NameWithoutLocale.IsEquivalentTo("Data/Shops"))
            {
                e.Edit(asset =>
                {
                    var data = asset.AsDictionary<string, ShopData>().Data;
                    if (!data.TryGetValue(FishShopId, out ShopData? shop))
                        return;

                    shop.Items.Add(new ShopItemData
                    {
                        Id = DoubleHookId,
                        ItemId = DoubleHookQualifiedId,
                        Price = DoubleHookShopPrice,
                        Condition = $"PLAYER_FISHING_LEVEL Current {DoubleHookFishingLevel}"
                    });
                });
            }
        }

        /// <summary>Reload the object data so its name/description use the new language.</summary>
        private void OnLocaleChanged(object? sender, LocaleChangedEventArgs e)
        {
            this.Mod.Helper.GameContent.InvalidateCache("Data/Objects");
        }
    }
}
