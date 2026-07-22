using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Objects;

namespace FishingHorizonsExpanded.Framework.CrabPots
{
    /// <summary>Harmony patches that extend where crab pots work and which trap fish they can catch.</summary>
    /// <remarks>
    /// Patched methods (Stardew Valley 1.6):
    ///
    /// <list type="bullet">
    /// <item><see cref="GameLocation.GetCrabPotFishForTile"/> (postfix) — vanilla only returns the water types
    /// from the location's <c>FishAreas.CrabPotFishTypes</c> (defaulting to <c>ocean</c>/<c>freshwater</c>).
    /// The postfix appends the location's <c>Name</c> as an extra water type (so content packs can gate a trap
    /// fish to one location by using the location name, e.g. <c>Woods</c> or <c>IslandWest</c>, in the water-type
    /// field of <c>Data/Fish</c>), appends <c>Ocean</c> next to vanilla <c>ocean</c> for case-tolerant matching,
    /// and appends <c>Cave</c> / <c>Lava</c> on mine floors.</item>
    ///
    /// <item><c>CrabPot.IsValidCrabPotLocationTile</c> (postfix, resolved by name) — allows placing crab pots on
    /// water tiles inside the mines. Resolved via reflection so the mod still compiles and runs (minus this
    /// feature, with a logged warning) if the method is renamed in a game update.</item>
    /// </list>
    ///
    /// All patches swallow their own exceptions, so a failure can never crash the game.
    /// </remarks>
    internal static class CrabPotPatches
    {
        /*********
        ** Fields
        *********/
        /// <summary>The mod monitor for error logging.</summary>
        private static IMonitor Monitor = null!;

        /// <summary>Whether location-name water types for trap fish are enabled.</summary>
        private static Func<bool> TrapTypesEnabled = () => false;

        /// <summary>Whether crab pots can be placed in the mines.</summary>
        private static Func<bool> CavePotsEnabled = () => false;

        /// <summary>Whether crab pots work in the lava mine area (80–119).</summary>
        private static Func<bool> LavaPotsEnabled = () => false;


        /*********
        ** Public methods
        *********/
        /// <summary>Apply the Harmony patches.</summary>
        /// <param name="modId">The unique mod ID (used as the Harmony ID).</param>
        /// <param name="monitor">The mod monitor for error logging.</param>
        /// <param name="trapTypesEnabled">Get whether location-name trap fish types are enabled.</param>
        /// <param name="cavePotsEnabled">Get whether cave crab pots are enabled.</param>
        /// <param name="lavaPotsEnabled">Get whether lava crab pots are enabled.</param>
        public static void Apply(string modId, IMonitor monitor, Func<bool> trapTypesEnabled, Func<bool> cavePotsEnabled, Func<bool> lavaPotsEnabled)
        {
            Monitor = monitor;
            TrapTypesEnabled = trapTypesEnabled;
            CavePotsEnabled = cavePotsEnabled;
            LavaPotsEnabled = lavaPotsEnabled;

            var harmony = new Harmony($"{modId}.crabpots");

            // trap fish water types
            MethodInfo? getFishTypes = AccessTools.Method(typeof(GameLocation), nameof(GameLocation.GetCrabPotFishForTile));
            if (getFishTypes is not null)
            {
                harmony.Patch(
                    original: getFishTypes,
                    postfix: new HarmonyMethod(typeof(CrabPotPatches), nameof(After_GetCrabPotFishForTile))
                );
            }
            else
                Monitor.Log("Couldn't find GameLocation.GetCrabPotFishForTile — custom trap fish water types disabled.", LogLevel.Warn);

            // crab pot placement in the mines (resolved by name: not guaranteed stable across game updates)
            MethodInfo? isValidTile = AccessTools.Method(typeof(CrabPot), "IsValidCrabPotLocationTile");
            if (isValidTile is not null)
            {
                harmony.Patch(
                    original: isValidTile,
                    postfix: new HarmonyMethod(typeof(CrabPotPatches), nameof(After_IsValidCrabPotLocationTile))
                );
            }
            else
                Monitor.Log("Couldn't find CrabPot.IsValidCrabPotLocationTile — placing crab pots in the mines disabled.", LogLevel.Warn);
        }

        /// <summary>Get the extra water types this mod adds for a tile in a location, if any.</summary>
        /// <param name="location">The location to check.</param>
        public static IEnumerable<string> GetExtraFishTypes(GameLocation location)
        {
            if (location is MineShaft mine)
            {
                bool isLava = mine.getMineArea() == 80;
                if (isLava && LavaPotsEnabled())
                    yield return "Lava";
                else if (!isLava && CavePotsEnabled())
                    yield return "Cave";
                yield break;
            }

            if (!TrapTypesEnabled())
                yield break;

            // location name as a water type, so packs can gate trap fish per location (e.g. Woods, Mountain, IslandWest)
            if (!string.IsNullOrWhiteSpace(location.Name))
                yield return location.Name;
        }


        /*********
        ** Private methods — patches
        *********/
        /// <summary>After the game collects the crab pot water types for a tile: append this mod's extra types.</summary>
        private static void After_GetCrabPotFishForTile(GameLocation __instance, ref IList<string> __result)
        {
            try
            {
                __result ??= new List<string>();
                foreach (string type in GetExtraFishTypes(__instance))
                {
                    if (!__result.Contains(type))
                        __result.Add(type);
                }

                // tolerate capitalized 'Ocean' in content pack data next to vanilla 'ocean'
                if (TrapTypesEnabled() && __result.Contains("ocean") && !__result.Contains("Ocean"))
                    __result.Add("Ocean");
            }
            catch (Exception ex)
            {
                Monitor.LogOnce($"Failed adding custom crab pot water types; falling back to vanilla behavior.\n{ex}", LogLevel.Warn);
            }
        }

        /// <summary>After the game validates a crab pot tile: also allow water tiles in the mines.</summary>
        /// <remarks>Original signature assumed: <c>bool IsValidCrabPotLocationTile(GameLocation location, int tileX, int tileY)</c>. Arguments are matched by index in case parameter names differ.</remarks>
        private static void After_IsValidCrabPotLocationTile(ref bool __result, object __0, int __1, int __2)
        {
            try
            {
                if (__result || __0 is not MineShaft mine)
                    return;

                if (!CavePotsEnabled())
                    return;

                if (mine.getMineArea() == 80 && !LavaPotsEnabled())
                    return;

                if (mine.isWaterTile(__1, __2) && !mine.objects.ContainsKey(new Vector2(__1, __2)))
                    __result = true;
            }
            catch (Exception ex)
            {
                Monitor.LogOnce($"Failed validating a mine crab pot tile; falling back to vanilla behavior.\n{ex}", LogLevel.Warn);
            }
        }
    }
}
