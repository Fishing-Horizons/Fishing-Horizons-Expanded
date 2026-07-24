using System;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Tools;

namespace FishingHorizonsExpanded.Framework.Tackle
{
    /// <summary>Harmony patches implementing the Double Hook minigame mechanic.</summary>
    /// <remarks>
    /// How it works (all on <see cref="BobberBar"/>, Stardew Valley 1.6):
    /// <list type="number">
    /// <item>Constructor postfix: if the double hook is equipped, roll the configured chance and arm the mechanic.</item>
    /// <item><c>update</c> postfix: once the catch bar is half full, a second fish bites: it moves through the same
    /// track with its own simple motion (driven by the same difficulty). It has a "hold" meter that fills while it's
    /// inside the player's green bar and drains while it isn't. If the meter empties, the second fish escapes
    /// (the first catch continues normally). If it's still hooked at the moment the first fish is landed, both are caught.</item>
    /// <item><see cref="FishingRod.pullFishFromWater"/> prefix: bumps <c>numCaught</c> by one when the second
    /// fish was secured, so the game itself awards two fish of the same species/size/quality (the same plumbing
    /// challenge bait uses).</item>
    /// <item><c>draw</c> postfix: renders the second fish and its hold meter.</item>
    /// </list>
    /// State is static because the bobber bar minigame only ever runs for the local player.
    /// All patches swallow their own exceptions, so a failure can never crash the game.
    /// </remarks>
    internal static class DoubleHookPatches
    {
        /*********
        ** Tuning constants
        *********/
        /// <summary>How full the catch bar must be before the second fish bites.</summary>
        private const float SpawnAtProgress = 0.5f;

        /// <summary>Hold meter gain per tick while the second fish is inside the player's bar.</summary>
        private const float HoldGainPerTick = 0.002f;

        /// <summary>Hold meter loss per tick while the second fish is outside the player's bar.</summary>
        private const float HoldLossPerTick = 0.0035f;


        /*********
        ** Fields
        *********/
        /// <summary>The mod monitor for error logging.</summary>
        private static IMonitor Monitor = null!;

        /// <summary>Get whether the module is currently enabled (checked per call so GMCM toggling works mid-game).</summary>
        private static Func<bool> IsEnabled = () => false;

        /// <summary>Get the configured chance (0–1) that a second fish bites during a catch.</summary>
        private static Func<float> GetChance = () => 0f;

        /// <summary>Whether the mechanic is armed for the current minigame (double hook equipped + chance roll passed).</summary>
        private static bool Armed;

        /// <summary>Whether the second fish has already bitten.</summary>
        private static bool SecondSpawned;

        /// <summary>Whether the second fish escaped (hold meter emptied).</summary>
        private static bool SecondLost;

        /// <summary>Whether the second fish was still hooked when the first fish was landed.</summary>
        private static bool SecondSecured;

        /// <summary>The second fish's position on the 0–548 bobber track.</summary>
        private static float SecondPosition;

        /// <summary>The second fish's current speed.</summary>
        private static float SecondSpeed;

        /// <summary>The second fish's target position on the track (-1 for none).</summary>
        private static float SecondTarget = -1f;

        /// <summary>The second fish's hold meter (0–1); it escapes at 0.</summary>
        private static float SecondHold;

        /// <summary>The second fish's shake offset while it's being held.</summary>
        private static Vector2 SecondShake = Vector2.Zero;


        /*********
        ** Public methods
        *********/
        /// <summary>Apply the Harmony patches.</summary>
        /// <param name="modId">The unique mod ID (used as the Harmony ID).</param>
        /// <param name="monitor">The mod monitor for error logging.</param>
        /// <param name="isEnabled">Get whether the module is currently enabled.</param>
        /// <param name="getChance">Get the configured chance (0–1) that a second fish bites.</param>
        public static void Apply(string modId, IMonitor monitor, Func<bool> isEnabled, Func<float> getChance)
        {
            Monitor = monitor;
            IsEnabled = isEnabled;
            GetChance = getChance;

            var harmony = new Harmony($"{modId}.tackle");

            harmony.Patch(
                original: AccessTools.FirstConstructor(typeof(BobberBar), c => !c.IsStatic && c.GetParameters().Length > 0),
                postfix: new HarmonyMethod(typeof(DoubleHookPatches), nameof(AfterConstructor))
            );
            harmony.Patch(
                original: AccessTools.Method(typeof(BobberBar), nameof(BobberBar.update)),
                postfix: new HarmonyMethod(typeof(DoubleHookPatches), nameof(AfterUpdate))
            );
            harmony.Patch(
                original: AccessTools.Method(typeof(BobberBar), nameof(BobberBar.draw), new[] { typeof(SpriteBatch) }),
                postfix: new HarmonyMethod(typeof(DoubleHookPatches), nameof(AfterDraw))
            );
            harmony.Patch(
                original: AccessTools.Method(typeof(FishingRod), nameof(FishingRod.pullFishFromWater)),
                prefix: new HarmonyMethod(typeof(DoubleHookPatches), nameof(BeforePullFishFromWater))
            );
        }


        /*********
        ** Patches
        *********/
        /// <summary>Arm the mechanic when a new minigame starts with the double hook equipped.</summary>
        private static void AfterConstructor(BobberBar __instance)
        {
            Reset();

            try
            {
                if (!IsEnabled())
                    return;
                if (__instance.bossFish || __instance.fromFishPond)
                    return;
                if (__instance.bobbers?.Contains(TackleModule.DoubleHookQualifiedId) != true)
                    return;

                Armed = Game1.random.NextDouble() < (double)Math.Clamp(GetChance(), 0f, 1f);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed in {nameof(AfterConstructor)}:\n{ex}", LogLevel.Error);
                Armed = false;
            }
        }

        /// <summary>Spawn and simulate the second fish.</summary>
        private static void AfterUpdate(BobberBar __instance)
        {
            try
            {
                if (!Armed || __instance.fadeIn)
                    return;

                // the first fish was landed: lock in the outcome for the second one
                if (__instance.fadeOut)
                {
                    if (SecondSpawned && !SecondLost && !SecondSecured && __instance.distanceFromCatching > 0.9f)
                    {
                        SecondSecured = true;
                        Game1.playSound("newArtifact");
                    }
                    return;
                }

                // second fish bites once the catch bar is half full
                if (!SecondSpawned)
                {
                    if (__instance.distanceFromCatching >= SpawnAtProgress)
                    {
                        SecondSpawned = true;
                        SecondHold = 1f;
                        SecondTarget = -1f;
                        SecondSpeed = 0f;

                        // bite on the far side of the track from the player's bar
                        float barCenter = __instance.bobberBarPos + __instance.bobberBarHeight / 2f;
                        SecondPosition = barCenter < 274f
                            ? Game1.random.Next(350, 500)
                            : Game1.random.Next(30, 180);

                        Game1.playSound("FishHit");
                        __instance.everythingShakeTimer = 300f;
                    }
                    return;
                }

                if (SecondLost)
                    return;

                // simple motion driven by the same difficulty as the main fish
                float difficulty = __instance.difficulty;
                if (SecondTarget < 0f || Math.Abs(SecondPosition - SecondTarget) <= 3f)
                {
                    if (Game1.random.NextDouble() < (double)(difficulty / 3000f) || SecondTarget < 0f)
                    {
                        float spaceBelow = 532f - SecondPosition;
                        float spaceAbove = SecondPosition;
                        float percent = Math.Min(99f, difficulty + Game1.random.Next(10, 45)) / 100f;
                        SecondTarget = SecondPosition + Game1.random.Next((int)Math.Min(0f - spaceAbove, spaceBelow), (int)spaceBelow) * percent;
                        SecondTarget = Math.Max(0f, Math.Min(532f, SecondTarget));
                    }
                }
                else
                {
                    float acceleration = (SecondTarget - SecondPosition) / (Game1.random.Next(10, 30) + (100f - Math.Min(100f, difficulty)));
                    SecondSpeed += (acceleration - SecondSpeed) / 5f;
                }
                SecondPosition = Math.Max(0f, Math.Min(532f, SecondPosition + SecondSpeed));

                // hold meter: fills inside the player's bar, drains outside
                bool inBar = SecondPosition + 12f <= __instance.bobberBarPos - 32f + __instance.bobberBarHeight
                    && SecondPosition - 16f >= __instance.bobberBarPos - 32f;
                if (inBar)
                {
                    SecondHold = Math.Min(1f, SecondHold + HoldGainPerTick);
                    SecondShake = new Vector2(Game1.random.Next(-10, 11) / 10f, Game1.random.Next(-10, 11) / 10f);
                }
                else
                {
                    SecondHold -= HoldLossPerTick;
                    SecondShake = Vector2.Zero;
                    if (SecondHold <= 0f)
                    {
                        SecondLost = true;
                        Game1.playSound("fishEscape");
                        __instance.everythingShakeTimer = 300f;
                    }
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed in {nameof(AfterUpdate)}:\n{ex}", LogLevel.Error);
                Armed = false;
            }
        }

        /// <summary>Draw the second fish and its hold meter.</summary>
        private static void AfterDraw(BobberBar __instance, SpriteBatch b)
        {
            try
            {
                if (!Armed || !SecondSpawned || SecondLost || SecondSecured || __instance.scale != 1f || __instance.fadeOut)
                    return;

                Game1.StartWorldDrawInUI(b);

                // second fish (same sprite as the main fish, slightly transparent so the pair reads at a glance)
                b.Draw(
                    Game1.mouseCursors,
                    new Vector2(__instance.xPositionOnScreen + 64 + 18, __instance.yPositionOnScreen + 12 + 24 + SecondPosition) + SecondShake + __instance.everythingShake,
                    new Rectangle(614, 1840, 20, 20),
                    Color.White * 0.85f,
                    0f,
                    new Vector2(10f, 10f),
                    1.75f,
                    SpriteEffects.FlipHorizontally,
                    0.87f
                );

                // hold meter next to the second fish (same style as the treasure meter)
                b.Draw(Game1.staminaRect, new Rectangle(__instance.xPositionOnScreen + 64, __instance.yPositionOnScreen + 12 + (int)SecondPosition, 40, 8), Color.DimGray * 0.5f);
                b.Draw(Game1.staminaRect, new Rectangle(__instance.xPositionOnScreen + 64, __instance.yPositionOnScreen + 12 + (int)SecondPosition, (int)(SecondHold * 40f), 8), Utility.getRedToGreenLerpColor(SecondHold));

                Game1.EndWorldDrawInUI(b);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed in {nameof(AfterDraw)}:\n{ex}", LogLevel.Error);
            }
        }

        /// <summary>Award the second fish when it was secured.</summary>
        private static void BeforePullFishFromWater(ref int numCaught)
        {
            try
            {
                if (Armed && SecondSecured)
                    numCaught += 1;
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed in {nameof(BeforePullFishFromWater)}:\n{ex}", LogLevel.Error);
            }
            finally
            {
                Reset();
            }
        }


        /*********
        ** Private methods
        *********/
        /// <summary>Reset all minigame state.</summary>
        private static void Reset()
        {
            Armed = false;
            SecondSpawned = false;
            SecondLost = false;
            SecondSecured = false;
            SecondPosition = 0f;
            SecondSpeed = 0f;
            SecondTarget = -1f;
            SecondHold = 0f;
            SecondShake = Vector2.Zero;
        }
    }
}
