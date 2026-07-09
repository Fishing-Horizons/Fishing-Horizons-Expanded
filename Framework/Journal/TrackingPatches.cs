using System;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Tools;
using SObject = StardewValley.Object;

namespace FishingHorizonsExpanded.Framework.Journal
{
    /// <summary>Harmony patches that record journal progress the game doesn't track by itself:
    /// the catch diary, best catch quality, and "sold"/"gifted" status.</summary>
    /// <remarks>
    /// Patched methods (Stardew Valley 1.6):
    /// <list type="bullet">
    ///   <item><see cref="Farmer.caughtFish"/> — catch diary entries + size records.</item>
    ///   <item><see cref="FishingRod.pullFishFromWater"/> — best catch quality.</item>
    ///   <item><see cref="NPC.receiveGift"/> — "gifted" status.</item>
    ///   <item><see cref="ShopMenu.receiveLeftClick"/> / <see cref="ShopMenu.receiveRightClick"/> — "sold" status.</item>
    /// </list>
    /// All patches are postfixes (plus passive prefixes for the shop diff) and swallow their own
    /// exceptions, so a failure can never crash the game.
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
            harmony.Patch(
                original: AccessTools.Method(typeof(NPC), nameof(NPC.receiveGift)),
                postfix: new HarmonyMethod(typeof(TrackingPatches), nameof(After_ReceiveGift))
            );
            harmony.Patch(
                original: AccessTools.Method(typeof(ShopMenu), nameof(ShopMenu.receiveLeftClick)),
                prefix: new HarmonyMethod(typeof(TrackingPatches), nameof(Before_ShopClick)),
                postfix: new HarmonyMethod(typeof(TrackingPatches), nameof(After_ShopClick))
            );
            harmony.Patch(
                original: AccessTools.Method(typeof(ShopMenu), nameof(ShopMenu.receiveRightClick)),
                prefix: new HarmonyMethod(typeof(TrackingPatches), nameof(Before_ShopClick)),
                postfix: new HarmonyMethod(typeof(TrackingPatches), nameof(After_ShopClick))
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

        /// <summary>After an NPC receives a gift: mark gifted fish.</summary>
        private static void After_ReceiveGift(SObject o, Farmer giver)
        {
            try
            {
                if (giver?.IsLocalPlayer != true || o is null)
                    return;

                if (o.Category == SObject.FishCategory)
                    Progress.MarkGifted(o.QualifiedItemId);
            }
            catch (Exception ex)
            {
                Monitor.LogOnce($"Failed to record a gifted fish in the journal:\n{ex}", LogLevel.Warn);
            }
        }

        /// <summary>Before a shop click: remember which fish (if any) is about to be sold.</summary>
        private static void Before_ShopClick(ShopMenu __instance, int x, int y, bool ____isStorageShop, ref (string Id, int Count)? __state)
        {
            __state = null;
            try
            {
                if (____isStorageShop || Game1.player is null)
                    return;

                // the clicked inventory item is sold instantly if the shop buys it
                if (__instance.inventory?.getItemAt(x, y) is SObject obj
                    && obj.Category == SObject.FishCategory
                    && __instance.highlightItemToSell(obj))
                {
                    __state = (obj.QualifiedItemId, CountInInventory(obj.QualifiedItemId));
                }
            }
            catch (Exception ex)
            {
                Monitor.LogOnce($"Failed to snapshot the shop inventory for sale tracking:\n{ex}", LogLevel.Warn);
            }
        }

        /// <summary>After a shop click: if the remembered fish count decreased, it was sold.</summary>
        private static void After_ShopClick((string Id, int Count)? __state)
        {
            try
            {
                if (__state is null)
                    return;

                (string id, int before) = __state.Value;
                if (CountInInventory(id) < before)
                    Progress.MarkSold(id);
            }
            catch (Exception ex)
            {
                Monitor.LogOnce($"Failed to record a sold fish in the journal:\n{ex}", LogLevel.Warn);
            }
        }


        /*********
        ** Private methods — helpers
        *********/
        /// <summary>Count how many items with the given qualified ID the player has in their inventory.</summary>
        private static int CountInInventory(string qualifiedId)
        {
            int count = 0;
            foreach (Item? item in Game1.player.Items)
            {
                if (item?.QualifiedItemId == qualifiedId)
                    count += item.Stack;
            }
            return count;
        }
    }
}
