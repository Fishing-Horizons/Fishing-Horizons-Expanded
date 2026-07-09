using System.Collections.Generic;
using StardewModdingAPI;
using StardewModdingAPI.Events;

namespace FishingHorizonsExpanded.Framework.Journal
{
    /// <summary>Journal progress for one fish that the game doesn't track by itself.</summary>
    /// <remarks>Tracking starts when the mod is installed; earlier sales/gifts aren't known.</remarks>
    internal sealed class FishProgress
    {
        /// <summary>Whether the player ever sold this fish.</summary>
        public bool Sold { get; set; }

        /// <summary>Whether the player ever gifted this fish to an NPC.</summary>
        public bool Gifted { get; set; }

        /// <summary>The best quality caught: -1 unknown, 0 normal, 1 silver, 2 gold, 4 iridium.</summary>
        public int BestQuality { get; set; } = -1;
    }

    /// <summary>The per-save journal progress data.</summary>
    internal sealed class ProgressData
    {
        /// <summary>Progress per fish, keyed by qualified item ID.</summary>
        public Dictionary<string, FishProgress> Fish { get; set; } = new();
    }

    /// <summary>Loads and saves journal progress that the game doesn't track (sold/gifted/best quality).</summary>
    /// <remarks>
    /// Milestone 3 adds the storage and UI; milestone 4 adds the Harmony patches that actually
    /// record sales, gifts and catch quality. Data is stored in the save via SMAPI's save data API,
    /// so it currently tracks the main player only (farmhand support can be added later).
    /// </remarks>
    internal sealed class ProgressTracker
    {
        /*********
        ** Fields
        *********/
        /// <summary>The SMAPI save data key.</summary>
        private const string DataKey = "fish-progress";

        /// <summary>The mod helper.</summary>
        private readonly IModHelper Helper;

        /// <summary>The loaded progress data for the current save.</summary>
        private ProgressData Data = new();


        /*********
        ** Public methods
        *********/
        public ProgressTracker(IModHelper helper)
        {
            this.Helper = helper;
            helper.Events.GameLoop.SaveLoaded += this.OnSaveLoaded;
            helper.Events.GameLoop.Saving += this.OnSaving;
            helper.Events.GameLoop.ReturnedToTitle += this.OnReturnedToTitle;
        }

        /// <summary>Get the tracked progress for a fish (read-only usage).</summary>
        /// <param name="qualifiedId">The qualified item ID (like <c>(O)128</c>).</param>
        public FishProgress Get(string qualifiedId)
        {
            return this.Data.Fish.TryGetValue(qualifiedId, out FishProgress? progress)
                ? progress
                : new FishProgress();
        }

        /// <summary>Get the tracked progress for a fish, creating it if needed (for milestone 4 writers).</summary>
        /// <param name="qualifiedId">The qualified item ID (like <c>(O)128</c>).</param>
        public FishProgress GetOrCreate(string qualifiedId)
        {
            if (!this.Data.Fish.TryGetValue(qualifiedId, out FishProgress? progress))
                this.Data.Fish[qualifiedId] = progress = new FishProgress();
            return progress;
        }


        /*********
        ** Private methods
        *********/
        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            this.Data = Context.IsMainPlayer
                ? this.Helper.Data.ReadSaveData<ProgressData>(DataKey) ?? new ProgressData()
                : new ProgressData();
        }

        private void OnSaving(object? sender, SavingEventArgs e)
        {
            if (Context.IsMainPlayer)
                this.Helper.Data.WriteSaveData(DataKey, this.Data);
        }

        private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
        {
            this.Data = new ProgressData();
        }
    }
}
