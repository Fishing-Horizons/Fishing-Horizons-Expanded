using System;
using System.Collections.Generic;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

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

        /// <summary>The in-game day (<see cref="WorldDate.TotalDays"/>) when the last size record was set, or -1.</summary>
        public int RecordDay { get; set; } = -1;
    }

    /// <summary>One line in the catch diary.</summary>
    internal sealed class CatchLogEntry
    {
        /// <summary>The qualified item ID (like <c>(O)128</c>).</summary>
        public string QualifiedId { get; set; } = "";

        /// <summary>The in-game year of the catch.</summary>
        public int Year { get; set; }

        /// <summary>The in-game season of the catch (like "spring").</summary>
        public string Season { get; set; } = "";

        /// <summary>The in-game day of month of the catch.</summary>
        public int Day { get; set; }

        /// <summary>The caught size in inches (vanilla unit).</summary>
        public int SizeInches { get; set; }
    }

    /// <summary>The per-save journal progress data.</summary>
    internal sealed class ProgressData
    {
        /// <summary>Progress per fish, keyed by qualified item ID.</summary>
        public Dictionary<string, FishProgress> Fish { get; set; } = new();

        /// <summary>The catch diary, oldest first (capped at <see cref="ProgressTracker.MaxLogEntries"/>).</summary>
        public List<CatchLogEntry> Log { get; set; } = new();
    }

    /// <summary>Loads and saves journal progress that the game doesn't track (sold/gifted/best quality/catch diary).</summary>
    /// <remarks>
    /// The Harmony patches in <see cref="TrackingPatches"/> call the write methods here.
    /// Data is stored in the save via SMAPI's save data API, so it currently tracks
    /// the main player only (farmhand support can be added later).
    /// </remarks>
    internal sealed class ProgressTracker
    {
        /*********
        ** Constants
        *********/
        /// <summary>The maximum number of catch diary entries kept in the save.</summary>
        public const int MaxLogEntries = 100;


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
        ** Accessors
        *********/
        /// <summary>The catch diary entries, oldest first.</summary>
        public IReadOnlyList<CatchLogEntry> Log => this.Data.Log;


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

        /// <summary>Get the tracked progress for a fish, creating it if needed.</summary>
        /// <param name="qualifiedId">The qualified item ID (like <c>(O)128</c>).</param>
        public FishProgress GetOrCreate(string qualifiedId)
        {
            if (!this.Data.Fish.TryGetValue(qualifiedId, out FishProgress? progress))
                this.Data.Fish[qualifiedId] = progress = new FishProgress();
            return progress;
        }

        /// <summary>Record a catch in the diary.</summary>
        /// <param name="qualifiedId">The qualified item ID (like <c>(O)128</c>).</param>
        /// <param name="sizeInches">The caught size in inches (vanilla unit).</param>
        /// <param name="isSizeRecord">Whether this catch set a new size record.</param>
        public void AddCatch(string qualifiedId, int sizeInches, bool isSizeRecord)
        {
            WorldDate date = Game1.Date;
            this.Data.Log.Add(new CatchLogEntry
            {
                QualifiedId = qualifiedId,
                Year = date.Year,
                Season = date.SeasonKey,
                Day = date.DayOfMonth,
                SizeInches = sizeInches
            });
            while (this.Data.Log.Count > MaxLogEntries)
                this.Data.Log.RemoveAt(0);

            if (isSizeRecord)
                this.GetOrCreate(qualifiedId).RecordDay = date.TotalDays;
        }

        /// <summary>Record the quality of a catch, keeping the best one.</summary>
        /// <param name="qualifiedId">The qualified item ID (like <c>(O)128</c>).</param>
        /// <param name="quality">The catch quality: 0 normal, 1 silver, 2 gold, 4 iridium.</param>
        public void RecordQuality(string qualifiedId, int quality)
        {
            FishProgress progress = this.GetOrCreate(qualifiedId);
            if (quality > progress.BestQuality)
                progress.BestQuality = quality;
        }

        /// <summary>Mark a fish as sold at least once.</summary>
        /// <param name="qualifiedId">The qualified item ID (like <c>(O)128</c>).</param>
        public void MarkSold(string qualifiedId)
        {
            this.GetOrCreate(qualifiedId).Sold = true;
        }

        /// <summary>Mark a fish as gifted at least once.</summary>
        /// <param name="qualifiedId">The qualified item ID (like <c>(O)128</c>).</param>
        public void MarkGifted(string qualifiedId)
        {
            this.GetOrCreate(qualifiedId).Gifted = true;
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
