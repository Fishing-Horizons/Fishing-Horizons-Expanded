namespace FishingHorizonsExpanded.Framework.CrabPots
{
    /// <summary>A crab pot placed on a mine floor, persisted across floor regeneration.</summary>
    /// <remarks>
    /// Mine floors are transient: the <c>MineShaft</c> instance (and everything in it) is discarded when the
    /// player leaves, and regular floor layouts are re-randomized on every visit. So cave crab pots live in
    /// the mod's per-save data and are re-materialized when the player enters their floor.
    /// </remarks>
    public sealed class SavedCavePot
    {
        /// <summary>The mine level the pot was placed on (<c>MineShaft.mineLevel</c>).</summary>
        public int MineLevel { get; set; }

        /// <summary>The tile X coordinate where the pot was placed.</summary>
        public int TileX { get; set; }

        /// <summary>The tile Y coordinate where the pot was placed.</summary>
        public int TileY { get; set; }

        /// <summary>The unique multiplayer ID of the player who placed the pot.</summary>
        public long OwnerId { get; set; }

        /// <summary>The qualified item ID of the bait in the pot, if any.</summary>
        public string? BaitId { get; set; }

        /// <summary>The qualified item ID of the caught item waiting to be collected, if any.</summary>
        public string? HeldId { get; set; }

        /// <summary>The quality of the caught item.</summary>
        public int HeldQuality { get; set; }
    }
}
