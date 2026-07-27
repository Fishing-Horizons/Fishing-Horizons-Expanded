using System;
using System.Linq;
using FishingHorizonsExpanded.Framework;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley.GameData.Shops;
using StardewValley.GameData.Tools;

namespace FishingHorizonsExpanded.Framework.Rods
{
    /// <summary>Custom fishing rods, added as <c>Data/Tools</c> entries alongside the vanilla ones.</summary>
    /// <remarks>
    /// Vanilla rods are left untouched so removing the mod never breaks a save.
    ///
    /// Every rod is described by one <see cref="RodDefinition"/> in <see cref="AllRods"/>; the module
    /// does the rest. To add a rod, copy the template at the bottom of the rod list — the checklist is
    /// in <c>docs/adding-a-rod.md</c>.
    ///
    /// A new rod is a plain fishing rod with its own art, price and tier. The extra-fish mechanic is
    /// deliberately not part of this: it is the feeder rod's gimmick, and
    /// <see cref="Tackle.DoubleHookPatches"/> arms it by looking for that rod's ID specifically.
    /// </remarks>
    internal sealed class RodsModule : IModule
    {
        /*********
        ** Constants
        *********/
        /// <summary>The unqualified tool ID of the feeder rod.</summary>
        public const string FeederRodId = "waymeeNhaku.FHE_FeederRod";

        /// <summary>The qualified tool ID of the feeder rod, used by the extra-fish mechanic.</summary>
        public const string FeederRodQualifiedId = "(T)" + FeederRodId;

        /// <summary>The asset name of the feeder rod texture.</summary>
        public const string FeederRodTextureAssetName = "Mods/waymeeNhaku.FishingHorizonsExpanded/FeederRod";

        /// <summary>Willy's shop ID in <c>Data/Shops</c>.</summary>
        private const string FishShopId = "FishShop";


        /*********
        ** The rods
        *********/
        /// <summary>
        /// Every custom rod in the mod. Add one entry per rod; the module registers them all.
        /// </summary>
        public static readonly RodDefinition[] AllRods =
        {
            // --- Feeder Rod ---
            // An advanced rod at the iridium tier, with an inherent chance to hook a second fish
            // mid-minigame even without the Double Hook. That mechanic is unique to this rod and is
            // driven from DoubleHookPatches, not from anything in this file.
            new RodDefinition(
                Id: FeederRodId,
                Name: "FeederRod",
                TextureAsset: FeederRodTextureAssetName,
                TexturePath: "assets/feeder-rod.png",
                TranslationKey: "item.feeder-rod",
                Price: 5000,          // fibreglass is 1 800g, iridium 7 500g
                FishingLevel: 4)      // fibreglass needs 2, iridium 6
            {
                UpgradeLevel = 3,
                CastingTint = new Color(80, 140, 60),
                IsEnabled = config => config.EnableFeederRod
            },

            // ------------------------------------------------------------------
            // TEMPLATE — copy this block, uncomment it, change the values, done.
            // It also needs assets/example-rod.png and the two item.example-rod
            // keys in i18n/. Full checklist: docs/adding-a-rod.md
            // ------------------------------------------------------------------
            // new RodDefinition(
            //     Id: "waymeeNhaku.FHE_ExampleRod",         // must stay globally unique
            //     Name: "ExampleRod",
            //     TextureAsset: "Mods/waymeeNhaku.FishingHorizonsExpanded/ExampleRod",
            //     TexturePath: "assets/example-rod.png",    // a 16x16 icon is enough
            //     TranslationKey: "item.example-rod",
            //     Price: 3000,
            //     FishingLevel: 3)
            // {
            //     UpgradeLevel = 2,                         // 2 = bait slot only, 3 = bait + tackle
            //     CastingTint = new Color(150, 110, 70)     // omit for the vanilla colour of that tier
            //
            //     // Optional: to make the rod switchable, add a bool to ModConfig, then add
            //     //     IsEnabled = config => config.EnableExampleRod
            //     // here. Left out, the rod is always on.
            // },
        };


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
        public bool IsEnabled => AllRods.Any(rod => rod.Enabled(this.Mod.Config));


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
            RodPatches.Apply(this.Mod.ModManifest.UniqueID, this.Mod.Monitor, FindRod);

            helper.Events.Content.AssetRequested += this.OnAssetRequested;
            helper.Events.Content.LocaleChanged += this.OnLocaleChanged;
        }

        /// <summary>Find the custom rod with the given qualified tool ID, or <c>null</c> if it isn't one of ours.</summary>
        public static RodDefinition? FindRod(string? qualifiedId)
        {
            if (string.IsNullOrEmpty(qualifiedId))
                return null;

            foreach (RodDefinition rod in AllRods)
            {
                if (rod.QualifiedId == qualifiedId)
                    return rod;
            }

            return null;
        }


        /*********
        ** Private methods
        *********/
        /// <summary>Add each enabled rod's tool entry, texture and shop listing.</summary>
        private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
        {
            if (!this.IsEnabled)
                return;

            // tool definitions
            if (e.NameWithoutLocale.IsEquivalentTo("Data/Tools"))
            {
                e.Edit(asset =>
                {
                    var data = asset.AsDictionary<string, ToolData>().Data;
                    foreach (RodDefinition rod in this.EnabledRods())
                    {
                        data[rod.Id] = new ToolData
                        {
                            ClassName = "FishingRod",
                            Name = rod.Name,
                            DisplayName = this.Mod.Helper.Translation.Get($"{rod.TranslationKey}.name"),
                            Description = this.Mod.Helper.Translation.Get($"{rod.TranslationKey}.description"),
                            Texture = rod.TextureAsset,
                            SpriteIndex = 0,
                            MenuSpriteIndex = -1,
                            SalePrice = rod.Price,
                            UpgradeLevel = rod.UpgradeLevel,
                            CanBeLostOnDeath = false
                        };
                    }
                });
                return;
            }

            // Willy's shop entries
            if (e.NameWithoutLocale.IsEquivalentTo("Data/Shops"))
            {
                e.Edit(asset =>
                {
                    var data = asset.AsDictionary<string, ShopData>().Data;
                    if (!data.TryGetValue(FishShopId, out ShopData? shop))
                        return;

                    foreach (RodDefinition rod in this.EnabledRods())
                    {
                        shop.Items.Add(new ShopItemData
                        {
                            Id = rod.Id,
                            ItemId = rod.QualifiedId,
                            Price = rod.Price,
                            Condition = $"PLAYER_FISHING_LEVEL Current {rod.FishingLevel}"
                        });
                    }
                });
                return;
            }

            // tool textures
            foreach (RodDefinition rod in this.EnabledRods())
            {
                if (!e.NameWithoutLocale.IsEquivalentTo(rod.TextureAsset))
                    continue;

                RodDefinition target = rod;
                e.LoadFromModFile<Texture2D>(target.TexturePath, AssetLoadPriority.Exclusive);
                e.Edit(asset => this.AddCastingAnimation(asset, target), AssetEditPriority.Late);
                return;
            }
        }

        /// <summary>Get the rods switched on in the current config.</summary>
        private RodDefinition[] EnabledRods()
        {
            return AllRods.Where(rod => rod.Enabled(this.Mod.Config)).ToArray();
        }

        /// <summary>Copy the vanilla casting/reeling animation into a rod's texture, tinted to taste.</summary>
        /// <remarks>
        /// The in-hand animation isn't drawn from the 16×16 item sprite: <c>Game1.drawTool</c> samples
        /// 48×48 frames from the tool's own texture at the same coordinates as the vanilla
        /// <c>TileSheets/tools</c> sheet (rows y = 240–384). So each custom rod's texture is extended to
        /// the vanilla sheet's size and those rows are copied across from the player's own game files at
        /// runtime, which keeps the animation correct for whatever tool recolour they have installed.
        /// Only the 16×16 icon at sprite index 0 has to be drawn by hand.
        ///
        /// Vanilla's frames are greyscale and get their colour from <c>FishingRod.getColor()</c> at draw
        /// time. When a rod sets its own <see cref="RodDefinition.CastingTint"/> the tint is multiplied
        /// into the pixels here and <see cref="RodPatches"/> forces that draw colour to white, so the
        /// authored colour is what appears on screen. With no tint the greyscale is left alone and
        /// vanilla colours it by tier.
        /// </remarks>
        private void AddCastingAnimation(IAssetData asset, RodDefinition rod)
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

                if (rod.CastingTint is Color tint)
                {
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        Color p = pixels[i];
                        if (p.A == 0)
                            continue;
                        pixels[i] = new Color(
                            p.R * tint.R / 255,
                            p.G * tint.G / 255,
                            p.B * tint.B / 255,
                            p.A
                        );
                    }
                }

                editor.Data.SetData(0, region, pixels, 0, pixels.Length);
            }
            catch (Exception ex)
            {
                this.Mod.Monitor.Log(
                    $"Failed building the casting animation for {rod.Name}; the rod may be invisible while casting.\n{ex}",
                    LogLevel.Warn);
            }
        }

        /// <summary>Reload the tool data so names and descriptions pick up the new language.</summary>
        private void OnLocaleChanged(object? sender, LocaleChangedEventArgs e)
        {
            this.Mod.Helper.GameContent.InvalidateCache("Data/Tools");
        }
    }
}
