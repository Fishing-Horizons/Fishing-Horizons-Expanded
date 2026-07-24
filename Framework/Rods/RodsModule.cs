using System;
using FishingHorizonsExpanded.Framework;
using Microsoft.Xna.Framework;
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

        /// <summary>The gold tint baked into the grayscale casting animation (same idea as the game's per-rod tints, e.g. bamboo's goldenrod).</summary>
        private static readonly Color GoldTint = new(255, 190, 50);


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
                        CanBeLostOnDeath = false
                    };
                });
            }

            // tool texture
            else if (e.NameWithoutLocale.IsEquivalentTo(GoldenRodTextureAssetName))
            {
                e.LoadFromModFile<Texture2D>("assets/golden-rod.png", AssetLoadPriority.Exclusive);
                e.Edit(this.AddCastingAnimation, AssetEditPriority.Late);
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

        /// <summary>Copy the vanilla rod casting/reeling animation into the golden rod's texture, tinted gold.</summary>
        /// <remarks>
        /// The in-hand cast/reel animation isn't drawn from the 16×16 item sprite: <c>Game1.drawTool</c> samples
        /// 48×48 frames from the tool's own texture at the same coordinates as the vanilla <c>TileSheets/tools</c>
        /// sheet (rows y = 240–384). Those vanilla frames are grayscale, and the game tints them with one color
        /// from <c>FishingRod.getColor()</c>, hardcoded by upgrade level (bamboo = goldenrod, training = olive,
        /// fiberglass = white, iridium = violet). Our rod uses UpgradeLevel 2 → tint is white, so we bake the gold
        /// color in ourselves: extend our texture to the vanilla sheet size, copy the grayscale animation region
        /// from the player's own game files at runtime, and multiply it by <see cref="GoldTint"/>. The 16×16 icon
        /// at sprite index 0 stays untouched, so WayMee's art only needs to be the inventory icon.
        /// </remarks>
        private void AddCastingAnimation(IAssetData asset)
        {
            try
            {
                // load through the content pipeline (instead of Game1.toolSpriteSheet) so retexture mods are respected
                Texture2D vanilla = this.Mod.Helper.GameContent.Load<Texture2D>("TileSheets/tools");

                var editor = asset.AsImage();
                editor.ExtendImage(Math.Max(editor.Data.Width, vanilla.Width), Math.Max(editor.Data.Height, vanilla.Height));

                // rod animation region of the vanilla sheet: reeling frames (y 240–288) + casting frames (y 288–384)
                var region = new Rectangle(0, 240, vanilla.Width, Math.Min(vanilla.Height, editor.Data.Height) - 240);
                var pixels = new Color[region.Width * region.Height];
                vanilla.GetData(0, region, pixels, 0, pixels.Length);

                for (int i = 0; i < pixels.Length; i++)
                {
                    Color p = pixels[i];
                    if (p.A == 0)
                        continue;
                    pixels[i] = new Color(
                        p.R * GoldTint.R / 255,
                        p.G * GoldTint.G / 255,
                        p.B * GoldTint.B / 255,
                        p.A
                    );
                }

                editor.Data.SetData(0, region, pixels, 0, pixels.Length);
            }
            catch (Exception ex)
            {
                this.Mod.Monitor.Log($"Failed building the golden rod casting animation; the rod may be invisible while casting.\n{ex}", LogLevel.Warn);
            }
        }

        /// <summary>Reload the tool data so its name/description use the new language.</summary>
        private void OnLocaleChanged(object? sender, LocaleChangedEventArgs e)
        {
            this.Mod.Helper.GameContent.InvalidateCache("Data/Tools");
        }
    }
}
