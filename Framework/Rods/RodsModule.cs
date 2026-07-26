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
    /// <item><b>Feeder Rod</b> — an advanced rod with both bait and tackle slots (UpgradeLevel 3,
    /// same tier as iridium). Its gimmick: an inherent chance to hook a second fish during the
    /// minigame, even without the Double Hook tackle. When the Double Hook IS equipped, a third
    /// fish can appear. Sold by Willy once the player reaches fishing level 4.</item>
    /// </list>
    /// </remarks>
    internal sealed class RodsModule : IModule
    {
        /*********
        ** Constants
        *********/
        /// <summary>The unqualified tool ID of the feeder rod.</summary>
        public const string FeederRodId = "waymeeNhaku.FHE_FeederRod";

        /// <summary>The qualified tool ID of the feeder rod.</summary>
        public const string FeederRodQualifiedId = "(T)" + FeederRodId;

        /// <summary>The asset name of the feeder rod texture.</summary>
        public const string FeederRodTextureAssetName = "Mods/waymeeNhaku.FishingHorizonsExpanded/FeederRod";

        /// <summary>Willy's shop ID in <c>Data/Shops</c>.</summary>
        private const string FishShopId = "FishShop";

        /// <summary>The feeder rod's price in Willy's shop (fiberglass is 1 800g, iridium is 7 500g).</summary>
        private const int FeederRodShopPrice = 5000;

        /// <summary>The fishing level required before Willy sells the feeder rod (fiberglass is 2, iridium is 6).</summary>
        private const int FeederRodFishingLevel = 4;

        /// <summary>The tint baked into the grayscale casting animation (olive green for a feeder rod look).</summary>
        private static readonly Color FeederTint = new(80, 140, 60);


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
        public bool IsEnabled => this.Mod.Config.EnableFeederRod;


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
            // The feeder rod's fish-catching mechanic is handled entirely by DoubleHookPatches
            // (which detects the feeder rod in the constructor postfix). No separate Harmony
            // patches needed here — only asset injection.

            helper.Events.Content.AssetRequested += this.OnAssetRequested;
            helper.Events.Content.LocaleChanged += this.OnLocaleChanged;
        }


        /*********
        ** Private methods
        *********/
        /// <summary>Add the feeder rod tool, its texture, and Willy's shop entry.</summary>
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
                    data[FeederRodId] = new ToolData
                    {
                        ClassName = "FishingRod",
                        Name = "FeederRod",
                        DisplayName = this.Mod.Helper.Translation.Get("item.feeder-rod.name"),
                        Description = this.Mod.Helper.Translation.Get("item.feeder-rod.description"),
                        Texture = FeederRodTextureAssetName,
                        SpriteIndex = 0,
                        MenuSpriteIndex = -1,
                        SalePrice = FeederRodShopPrice,
                        UpgradeLevel = 3, // iridium tier: one bait slot + one tackle slot
                        CanBeLostOnDeath = false
                    };
                });
            }

            // tool texture
            else if (e.NameWithoutLocale.IsEquivalentTo(FeederRodTextureAssetName))
            {
                e.LoadFromModFile<Texture2D>("assets/feeder-rod.png", AssetLoadPriority.Exclusive);
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
                        Id = FeederRodId,
                        ItemId = FeederRodQualifiedId,
                        Price = FeederRodShopPrice,
                        Condition = $"PLAYER_FISHING_LEVEL Current {FeederRodFishingLevel}"
                    });
                });
            }
        }

        /// <summary>Copy the vanilla rod casting/reeling animation into the feeder rod's texture, tinted.</summary>
        /// <remarks>
        /// The in-hand cast/reel animation isn't drawn from the 16×16 item sprite: <c>Game1.drawTool</c> samples
        /// 48×48 frames from the tool's own texture at the same coordinates as the vanilla <c>TileSheets/tools</c>
        /// sheet (rows y = 240–384). Those vanilla frames are grayscale, and the game tints them with one color
        /// from <c>FishingRod.getColor()</c>, hardcoded by upgrade level. Our rod uses UpgradeLevel 3 → tint is
        /// violet (same as iridium rod), so we bake our own olive green tint ourselves: extend our texture to the
        /// vanilla sheet size, copy the grayscale animation region from the player's own game files at runtime,
        /// and multiply it by <see cref="FeederTint"/>. The 16×16 icon at sprite index 0 stays untouched,
        /// so WayMee's art only needs to be the inventory icon.
        /// </remarks>
        private void AddCastingAnimation(IAssetData asset)
        {
            try
            {
                Texture2D vanilla = this.Mod.Helper.GameContent.Load<Texture2D>("TileSheets/tools");

                var editor = asset.AsImage();
                editor.ExtendImage(
                    Math.Max(editor.Data.Width, vanilla.Width),
                    Math.Max(editor.Data.Height, vanilla.Height));

                var region = new Rectangle(0, 240, vanilla.Width,
                    Math.Min(vanilla.Height, editor.Data.Height) - 240);
                var pixels = new Color[region.Width * region.Height];
                vanilla.GetData(0, region, pixels, 0, pixels.Length);

                for (int i = 0; i < pixels.Length; i++)
                {
                    Color p = pixels[i];
                    if (p.A == 0)
                        continue;
                    pixels[i] = new Color(
                        p.R * FeederTint.R / 255,
                        p.G * FeederTint.G / 255,
                        p.B * FeederTint.B / 255,
                        p.A
                    );
                }

                editor.Data.SetData(0, region, pixels, 0, pixels.Length);
            }
            catch (Exception ex)
            {
                this.Mod.Monitor.Log(
                    $"Failed building the feeder rod casting animation; the rod may be invisible while casting.\n{ex}",
                    LogLevel.Warn);
            }
        }

        /// <summary>Reload the tool data so its name/description use the new language.</summary>
        private void OnLocaleChanged(object? sender, LocaleChangedEventArgs e)
        {
            this.Mod.Helper.GameContent.InvalidateCache("Data/Tools");
        }
    }
}
