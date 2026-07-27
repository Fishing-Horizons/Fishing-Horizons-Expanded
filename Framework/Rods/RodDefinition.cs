using System;
using Microsoft.Xna.Framework;

namespace FishingHorizonsExpanded.Framework.Rods
{
    /// <summary>Everything the mod needs to know to add one custom fishing rod to the game.</summary>
    /// <remarks>
    /// Adding a rod means adding one entry to <see cref="RodsModule.AllRods"/> — the module reads this
    /// record to build the <c>Data/Tools</c> entry, load the icon, colour the casting animation and
    /// stock Willy's shop. See <c>docs/adding-a-rod.md</c> for the full checklist.
    ///
    /// A rod defined this way is an ordinary fishing rod with custom art, price and tier. It gets no
    /// special behaviour: the extra-fish mechanic belongs to the feeder rod alone, and lives in
    /// <see cref="Tackle.DoubleHookPatches"/>, which looks for that one rod's ID.
    /// </remarks>
    /// <param name="Id">The unqualified tool ID. Must be globally unique, so keep the author prefix (e.g. <c>waymeeNhaku.FHE_FeederRod</c>).</param>
    /// <param name="Name">The internal <c>Data/Tools</c> name. No spaces; the display name comes from the translation file.</param>
    /// <param name="TextureAsset">The game-content asset name the icon is served under (e.g. <c>Mods/waymeeNhaku.FishingHorizonsExpanded/FeederRod</c>).</param>
    /// <param name="TexturePath">The icon's path inside the mod folder. A 16×16 sprite is enough — the casting animation is generated.</param>
    /// <param name="TranslationKey">The i18n key prefix. The module reads <c>{key}.name</c> and <c>{key}.description</c>.</param>
    /// <param name="Price">The shop price, also used as the tool's sale price. Fibreglass is 1 800g, iridium 7 500g.</param>
    /// <param name="FishingLevel">The fishing level Willy requires before stocking it. Fibreglass is 2, iridium 6.</param>
    internal sealed record RodDefinition(
        string Id,
        string Name,
        string TextureAsset,
        string TexturePath,
        string TranslationKey,
        int Price,
        int FishingLevel)
    {
        /// <summary>The qualified tool ID, as used by inventory and shop lookups.</summary>
        public string QualifiedId => "(T)" + this.Id;

        /// <summary>
        /// The rod tier, which decides the attachment slots: 0 bamboo and 1 training have none,
        /// 2 (fibreglass) has bait only, 3 (iridium) has bait and tackle, 4 (advanced iridium) has
        /// bait and two tackle slots. Defaults to the iridium tier.
        /// </summary>
        public int UpgradeLevel { get; init; } = 3;

        /// <summary>
        /// The colour of the rod in the player's hands while casting and reeling, or <c>null</c> to
        /// use the grey-to-colour tint vanilla picks from <see cref="UpgradeLevel"/>. This is the
        /// colour that actually reaches the screen — see <see cref="RodPatches"/>.
        /// </summary>
        public Color? CastingTint { get; init; }

        /// <summary>
        /// Whether the rod exists at all, usually a config toggle. <c>null</c> means always on.
        /// Checked live, so flipping the setting mid-game takes effect on the next shop visit.
        /// </summary>
        public Func<ModConfig, bool>? IsEnabled { get; init; }

        /// <summary>Whether this rod is switched on under the given config.</summary>
        public bool Enabled(ModConfig config)
        {
            return this.IsEnabled?.Invoke(config) ?? true;
        }
    }
}
