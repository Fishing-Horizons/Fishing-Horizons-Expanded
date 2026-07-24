using FishingHorizonsExpanded.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley.GameData.Shops;
using StardewValley.GameData.Tools;

namespace FishingHorizonsExpanded.Framework.Rods
{
    /// <summary>Custom fishing rods (first step of the modular rod system from the design doc).</summary>
    /// <remarks>
    /// Vanilla rods are kept untouched so removing the mod never breaks a save — custom rods are
    /// added as separate <c>Data/Tools</c> entries next to them.
    ///
    /// Current rods:
    /// <list type="bullet">
    /// <item><b>Golden Rod</b> — sits between the fiberglass and iridium rods (bait slot, no tackle).
    /// Its gimmick: every fish it catches is always exactly gold quality — never less, never more.
    /// Sold by Willy once the player reaches fishing level 4.</item>
    /// </list>
    /// </remarks>
    internal sealed class RodsModule : IModule
    {
        /*********
        ** Constants
        *********/
        /// <summary>The unqualified tool ID of the golden rod.</summary>
        public const string GoldenRodId = "waymeeNhaku.FHE_GoldenRod";

        /// <summary>The qualified tool ID of the golden rod.</summary>
        public const string GoldenRodQualifiedId = "(T)" + GoldenRodId;

        /// <summary>The asset name of the golden rod texture.</summary>
        public const string GoldenRodTextureAssetName = "Mods/waymeeNhaku.FishingHorizonsExpanded/GoldenRod";

        /// <summary>Willy's shop ID in <c>Data/Shops</c>.</summary>
        private const string FishShopId = "FishShop";

        /// <summary>The golden rod's price in Willy's shop (fiberglass is 1 800g, iridium is 7 500g).</summary>
        private const int GoldenRodShopPrice = 4000;

        /// <summary>The fishing level required before Willy sells the golden rod (fiberglass is 2, iridium is 6).</summary>
        private const int GoldenRodFishingLevel = 4;


        /*********
        ** Fields
        *********/
        /// <summary>The mod instance.</summary>
        private readonly ModEntry Mod;


        /*********
        ** Accessors
        *********/
        /// <inheritdoc/>
        public string Name => "Rods";

        /// <inheritdoc/>
        public bool IsEnabled => this.Mod.Config.EnableGoldenRod;


        /*********
        ** Public methods
        *********/
        public RodsModule(ModEntry mod)
        {
            this.Mod = mod;
        }

        /// <inheritdoc/>
        public void Activate(IModHelper helper)
        {
            RodPatches.Apply(this.Mod.ModManifest.UniqueID, this.Mod.Monitor, () => this.IsEnabled);

            helper.Events.Content.AssetRequested += this.OnAssetRequested;
            helper.Events.Content.LocaleChanged += this.OnLocaleChanged;
        }


        /*********
        ** Private methods
        *********/
        /// <summary>Add the golden rod tool, its texture, and Willy's shop entry.</summary>
        private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
        {
            if (!this.IsEnabled)
                return;

            // tool definition
            if (e.NameWithoutLocale.IsEquivalentTo("Data/Tools"))
            {
                e.Edit(asset =>
                {
                    var data = asset.AsDictionary<string, ToolData>().Data;
                    data[GoldenRodId] = new ToolData
                    {
                        ClassName = "FishingRod",
                        Name = "GoldenRod",
                        DisplayName = this.Mod.Helper.Translation.Get("item.golden-rod.name"),
                        Description = this.Mod.Helper.Translation.Get("item.golden-rod.description"),
                        Texture = GoldenRodTextureAssetName,
                        SpriteIndex = 0,
                        MenuSpriteIndex = -1,
                        SalePrice = GoldenRodShopPrice,
                        UpgradeLevel = 2, // same tier behavior as fiberglass: bait slot, no tackle
                        ApplyUpgradeLevelToDisplayName = false,
                        CanBeLostOnDeath = false
                    };
                });
            }

            // tool texture
            else if (e.NameWithoutLocale.IsEquivalentTo(GoldenRodTextureAssetName))
            {
                e.LoadFromModFile<Texture2D>("assets/golden-rod.png", AssetLoadPriority.Exclusive);
            }

            // Willy's shop entry
            else if (e.NameWithoutLocale.IsEquivalentTo("Data/Shops"))
            {
                e.Edit(asset =>
                {
                    var data = asset.AsDictionary<string, ShopData>().Data;
                    if (!data.TryGetValue(FishShopId, out ShopData? shop))
                        return;

                    shop.Items.Add(new ShopItemData
                    {
                        Id = GoldenRodId,
                        ItemId = GoldenRodQualifiedId,
                        Price = GoldenRodShopPrice,
                        Condition = $"PLAYER_FISHING_LEVEL Current {GoldenRodFishingLevel}"
                    });
                });
            }
        }

        /// <summary>Reload the tool data so its name/description use the new language.</summary>
        private void OnLocaleChanged(object? sender, LocaleChangedEventArgs e)
        {
            this.Mod.Helper.GameContent.InvalidateCache("Data/Tools");
        }
    }
}
