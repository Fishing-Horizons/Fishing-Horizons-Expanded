using System;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Tools;

namespace FishingHorizonsExpanded.Framework.Journal
{
    /// <summary>Harmony patches that record journal progress the game doesn't track by itself:
    /// the catch diary and best catch quality.</summary>
    /// <remarks>
    /// Patched methods (Stardew Valley 1.6):
    /// <list type="bullet">
    ///   <item><see cref="Farmer.caughtFish"/> — catch diary entries + size records.</item>
    ///   <item><see cref="FishingRod.pullFishFromWater"/> — best catch quality.</item>
    /// </list>
    /// All patches are postfixes and swallow their own exceptions, so a failure can never crash the game.
    /// </remarks>
    internal static class TrackingPatches
    {
        /*********
        ** Fields
        *********/
        /// <summary>The mod monitor for error logging.</summary>
        private static IMonitor Monitor = null!;

        /// <summary>The progress tracker to write to.</summary>
        private static ProgressTracker Progress = null!;


        /*********
        ** Public methods
        *********/
        /// <summary>Apply the Harmony patches.</summary>
        /// <param name="modId">The unique mod ID (used as the Harmony ID).</param>
        /// <param name="monitor">The mod monitor for error logging.</param>
        /// <param name="progress">The progress tracker to write to.</param>
        public static void Apply(string modId, IMonitor monitor, ProgressTracker progress)
        {
            Monitor = monitor;
            Progress = progress;

            var harmony = new Harmony(modId);

            harmony.Patch(
                original: AccessTools.Method(typeof(Farmer), nameof(Farmer.caughtFish)),
                postfix: new HarmonyMethod(typeof(TrackingPatches), nameof(After_CaughtFish))
            );
            harmony.Patch(
                original: AccessTools.Method(typeof(FishingRod), nameof(FishingRod.pullFishFromWater)),
                postfix: new HarmonyMethod(typeof(TrackingPatches), nameof(After_PullFishFromWater))
            );
        }


        /*********
        ** Private methods — patches
        *********/
        /// <summary>After a fish is recorded as caught: add a diary entry and remember size records.</summary>
        private static void After_CaughtFish(Farmer __instance, string itemId, int size, bool from_fish_pond, bool __result)
        {
            try
            {
                if (from_fish_pond || !__instance.IsLocalPlayer)
                    return;

                string qualifiedId = ItemRegistry.QualifyItemId(itemId) ?? itemId;

                // the game only records real fish (not trash/seaweed) into fishCaught — mirror that filter
                if (!__instance.fishCaught.ContainsKey(qualifiedId))
                    return;

                Progress.AddCatch(qualifiedId, size, isSizeRecord: __result);
            }
            catch (Exception ex)
            {
                Monitor.LogOnce($"Failed to record a catch in the journal diary:\n{ex}", LogLevel.Warn);
            }
        }

        /// <summary>After a fish is pulled from the water: remember the best catch quality.</summary>
        private static void After_PullFishFromWater(FishingRod __instance, string fishId, int fishQuality, bool wasPerfect, bool fromFishPond)
        {
            try
            {
                if (fromFishPond)
                    return;

                Farmer? who = __instance.getLastFarmerToUse();
                if (who?.IsLocalPlayer != true)
                    return;

                // mirror the game's perfect-catch quality boost
                int quality = fishQuality;
                if (quality >= 2 && wasPerfect)
                    quality = 4;
                else if (quality >= 1 && wasPerfect)
                    quality = 2;
                if (quality < 0)
                    quality = 0;

                string qualifiedId = ItemRegistry.QualifyItemId(fishId) ?? fishId;
                Progress.RecordQuality(qualifiedId, quality);
            }
            catch (Exception ex)
            {
                Monitor.LogOnce($"Failed to record catch quality in the journal:\n{ex}", LogLevel.Warn);
            }
        }

    }
}
