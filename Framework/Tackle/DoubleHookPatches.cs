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
    /// <summary>Harmony patches implementing the multi-fish minigame mechanic.</summary>
    /// <remarks>
    /// Supports up to three fish in a single catch:
    /// <list type="bullet">
    /// <item><b>Second fish</b> — triggered by the <em>Feeder Rod</em> (inherent chance, no tackle needed)
    /// or by the <em>Double Hook</em> tackle on any rod. Spawns once the first catch bar reaches 50%.
    /// Gets its own full-size progress bar to the right of the original.</item>
    /// <item><b>Third fish</b> — only possible with the <em>Feeder Rod + Double Hook</em> combo.
    /// Spawns when the second fish's bar reaches 50% AND the first fish is fully caught.
    /// Gets its own progress bar further to the right.</item>
    /// </list>
    ///
    /// Each additional fish bounces on the same bobber track as the main fish, driven by difficulty.
    /// Its progress bar fills while the fish is inside the player's green bar and drains while outside.
    /// If the bar empties the fish escapes; if still hooked when the minigame ends, it's caught.
    ///
    /// The minigame is kept alive (fadeOut delayed) until all additional fish are resolved (secured or lost).
    ///
    /// Rendering: each extra fish uses its own colored placeholder texture (no horizontal flip).
    /// Two progress bars appear to the right of the vanilla catch bar, matching its visual style.
    ///
    /// State is static because the bobber bar minigame only ever runs for the local player.
    /// All patches swallow their own exceptions, so a failure can never crash the game.
    /// </remarks>
    internal static class DoubleHookPatches
    {
        /*********
        ** Tuning constants
        *********/
        /// <summary>How full the first catch bar must be before the second fish bites.</summary>
        private const float SecondFishSpawnProgress = 0.5f;

        /// <summary>How full the second fish's bar must be (AND first bar full) to spawn the third.</summary>
        private const float ThirdFishSpawnProgress = 0.5f;

        /// <summary>Progress gain per tick while an extra fish is inside the player's bar.</summary>
        private const float CatchGainPerTick = 0.002f;

        /// <summary>Progress loss per tick while an extra fish is outside the player's bar.</summary>
        private const float CatchLossPerTick = 0.003f;

        /// <summary>Width of each extra catch progress bar (pixels).</summary>
        private const int BarWidth = 16;

        /// <summary>Horizontal gap between progress bars.</summary>
        private const int BarGap = 6;

        /// <summary>X offset from xPositionOnScreen to the first extra bar.</summary>
        private const int FirstBarXOffset = 172;

        /// <summary>Height of each extra catch progress bar (matches vanilla track).</summary>
        private const int BarTrackHeight = 548;


        /*********
        ** Fields — config callbacks
        *********/
        private static IMonitor Monitor = null!;
        private static IGameContentHelper ContentHelper = null!;
        private static Func<bool> IsEnabled = () => false;
        private static Func<float> GetDoubleHookChance = () => 0f;
        private static Func<float> GetFeederRodChance = () => 0f;


        /*********
        ** Fields — per-minigame state
        *********/
        // -- arming --
        private static bool Armed;
        private static bool CanSpawnThird;

        // -- second fish --
        private static bool SecondSpawned;
        private static bool SecondLost;
        private static bool SecondSecured;
        private static float SecondPosition;
        private static float SecondSpeed;
        private static float SecondTarget = -1f;
        private static float SecondDistanceFromCatching;
        private static Vector2 SecondShake = Vector2.Zero;

        // -- third fish --
        private static bool ThirdSpawned;
        private static bool ThirdLost;
        private static bool ThirdSecured;
        private static float ThirdPosition;
        private static float ThirdSpeed;
        private static float ThirdTarget = -1f;
        private static float ThirdDistanceFromCatching;
        private static Vector2 ThirdShake = Vector2.Zero;

        // -- cached textures --
        private static Texture2D? CachedSecondFishTexture;
        private static Texture2D? CachedThirdFishTexture;


        /*********
        ** Constants — texture asset names
        *********/
        public const string SecondFishTextureAsset = "Mods/waymeeNhaku.FishingHorizonsExpanded/SecondFish";
        public const string ThirdFishTextureAsset = "Mods/waymeeNhaku.FishingHorizonsExpanded/ThirdFish";


        /*********
        ** Public methods
        *********/
        /// <summary>Apply the Harmony patches.</summary>
        public static void Apply(
            string modId,
            IMonitor monitor,
            Func<bool> isEnabled,
            Func<float> getDoubleHookChance,
            Func<float> getFeederRodChance,
            IGameContentHelper contentHelper)
        {
            Monitor = monitor;
            IsEnabled = isEnabled;
            GetDoubleHookChance = getDoubleHookChance;
            GetFeederRodChance = getFeederRodChance;
            ContentHelper = contentHelper;

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

        /// <summary>Arm the mechanic when a new minigame starts.</summary>
        private static void AfterConstructor(BobberBar __instance)
        {
            Reset();

            try
            {
                if (!IsEnabled())
                    return;
                if (__instance.bossFish || __instance.fromFishPond)
                    return;

                // Detect equipment
                var rod = Game1.player.CurrentTool as FishingRod;
                bool hasFeederRod = rod?.QualifiedItemId == Rods.RodsModule.FeederRodQualifiedId;
                bool hasDoubleHook = __instance.bobbers?.Contains(TackleModule.DoubleHookQualifiedId) == true;

                if (!hasFeederRod && !hasDoubleHook)
                    return;

                // Determine second fish chance
                float chance;
                if (hasFeederRod)
                {
                    // Feeder rod has its own inherent chance
                    chance = Math.Clamp(GetFeederRodChance(), 0f, 1f);
                }
                else
                {
                    // Double hook alone on a non-feeder rod
                    chance = Math.Clamp(GetDoubleHookChance(), 0f, 1f);
                }

                Armed = Game1.random.NextDouble() < (double)chance;

                // Third fish only possible with feeder rod + double hook combo
                CanSpawnThird = hasFeederRod && hasDoubleHook;

                // Pre-load textures
                try
                {
                    CachedSecondFishTexture = ContentHelper.Load<Texture2D>(SecondFishTextureAsset);
                    CachedThirdFishTexture = ContentHelper.Load<Texture2D>(ThirdFishTextureAsset);
                }
                catch
                {
                    // Textures not available — we'll fall back to vanilla sprite with tint
                    CachedSecondFishTexture = null;
                    CachedThirdFishTexture = null;
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed in {nameof(AfterConstructor)}:\n{ex}", LogLevel.Error);
                Armed = false;
            }
        }


        /// <summary>Spawn and simulate extra fish, delay fadeOut while fish are unresolved.</summary>
        private static void AfterUpdate(BobberBar __instance)
        {
            try
            {
                if (!Armed || __instance.fadeIn)
                    return;

                // --- Delay fadeOut while there are unresolved extra fish ---
                if (__instance.fadeOut && __instance.distanceFromCatching > 0.9f)
                {
                    bool hasUnresolved = (SecondSpawned && !SecondLost && !SecondSecured)
                                      || (ThirdSpawned && !ThirdLost && !ThirdSecured);

                    if (hasUnresolved)
                    {
                        // Keep the minigame alive — the first fish is caught, but extras need resolution
                        __instance.fadeOut = false;
                        return;
                    }
                    else
                    {
                        // All extra fish resolved — secure any remaining
                        if (SecondSpawned && !SecondLost && !SecondSecured)
                            SecondSecured = true;
                        if (ThirdSpawned && !ThirdLost && !ThirdSecured)
                            ThirdSecured = true;

                        // Let the fadeOut proceed normally
                        __instance.fadeOut = true;
                        return;
                    }
                }

                if (__instance.fadeOut)
                    return;

                // --- Spawn second fish at 50% of first bar ---
                if (!SecondSpawned)
                {
                    if (__instance.distanceFromCatching >= SecondFishSpawnProgress)
                    {
                        SpawnFish(
                            __instance,
                            out SecondSpawned,
                            out SecondPosition, out SecondSpeed, out SecondTarget,
                            out SecondDistanceFromCatching
                        );
                    }
                    return;
                }

                // --- Update second fish ---
                if (!SecondLost && !SecondSecured)
                {
                    UpdateFish(
                        __instance,
                        ref SecondPosition, ref SecondSpeed, ref SecondTarget,
                        ref SecondDistanceFromCatching, ref SecondShake,
                        out SecondLost, ref SecondSecured
                    );
                }

                // --- Spawn third fish (feeder rod + double hook only) ---
                if (CanSpawnThird && !ThirdSpawned && SecondSpawned && !SecondLost)
                {
                    bool firstBarFull = __instance.distanceFromCatching >= 0.99f;
                    bool secondBarHalf = SecondDistanceFromCatching >= ThirdFishSpawnProgress;

                    if (firstBarFull && secondBarHalf)
                    {
                        SpawnFish(
                            __instance,
                            out ThirdSpawned,
                            out ThirdPosition, out ThirdSpeed, out ThirdTarget,
                            out ThirdDistanceFromCatching
                        );
                    }
                }

                // --- Update third fish ---
                if (ThirdSpawned && !ThirdLost && !ThirdSecured)
                {
                    UpdateFish(
                        __instance,
                        ref ThirdPosition, ref ThirdSpeed, ref ThirdTarget,
                        ref ThirdDistanceFromCatching, ref ThirdShake,
                        out ThirdLost, ref ThirdSecured
                    );
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed in {nameof(AfterUpdate)}:\n{ex}", LogLevel.Error);
                Armed = false;
            }
        }

        /// <summary>Draw extra fish and their progress bars.</summary>
        private static void AfterDraw(BobberBar __instance, SpriteBatch b)
        {
            try
            {
                if (!Armed || __instance.scale != 1f)
                    return;

                Game1.StartWorldDrawInUI(b);

                int barIndex = 0;

                // --- Second fish ---
                if (SecondSpawned && !SecondLost)
                {
                    bool showFish = !SecondSecured && !__instance.fadeOut;

                    if (showFish)
                    {
                        DrawExtraFish(b, __instance, SecondPosition, SecondShake,
                            CachedSecondFishTexture, new Color(60, 140, 200));
                    }

                    // Progress bar (always visible while fish is alive, even during fadeOut)
                    if (!SecondSecured || __instance.fadeOut)
                    {
                        DrawProgressBar(b, __instance, SecondDistanceFromCatching, barIndex);
                    }
                    else
                    {
                        // Fish secured — show full green bar
                        DrawProgressBar(b, __instance, 1f, barIndex);
                    }
                    barIndex++;
                }

                // --- Third fish ---
                if (ThirdSpawned && !ThirdLost)
                {
                    bool showFish = !ThirdSecured && !__instance.fadeOut;

                    if (showFish)
                    {
                        DrawExtraFish(b, __instance, ThirdPosition, ThirdShake,
                            CachedThirdFishTexture, new Color(220, 140, 40));
                    }

                    if (!ThirdSecured || __instance.fadeOut)
                    {
                        DrawProgressBar(b, __instance, ThirdDistanceFromCatching, barIndex);
                    }
                    else
                    {
                        DrawProgressBar(b, __instance, 1f, barIndex);
                    }
                }

                Game1.EndWorldDrawInUI(b);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed in {nameof(AfterDraw)}:\n{ex}", LogLevel.Error);
            }
        }


        /// <summary>Award extra fish when they were secured.</summary>
        private static void BeforePullFishFromWater(ref int numCaught)
        {
            try
            {
                if (!Armed)
                    return;

                if (SecondSecured)
                    numCaught += 1;
                if (ThirdSecured)
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
        ** Private methods — fish simulation
        *********/

        /// <summary>Spawn an extra fish at the far side of the track from the player's bar.</summary>
        private static void SpawnFish(
            BobberBar bar,
            out bool spawned,
            out float position, out float speed, out float target,
            out float distanceFromCatching)
        {
            spawned = true;
            speed = 0f;
            target = -1f;
            distanceFromCatching = 0.5f; // start at 50% so the player has a fighting chance

            // Bite on the far side of the track from the player's bar
            float barCenter = bar.bobberBarPos + bar.bobberBarHeight / 2f;
            position = barCenter < 274f
                ? Game1.random.Next(350, 500)
                : Game1.random.Next(30, 180);

            Game1.playSound("FishHit");
            bar.everythingShakeTimer = 300f;
        }


        /// <summary>Simulate one tick of an extra fish's movement and catch progress.</summary>
        private static void UpdateFish(
            BobberBar bar,
            ref float position, ref float speed, ref float target,
            ref float distanceFromCatching, ref Vector2 shake,
            out bool lost, ref bool secured)
        {
            lost = false;

            // --- Fish movement (same algorithm as the main fish, driven by difficulty) ---
            float difficulty = bar.difficulty;

            if (target < 0f || Math.Abs(position - target) <= 3f)
            {
                if (Game1.random.NextDouble() < (double)(difficulty / 3000f) || target < 0f)
                {
                    float spaceBelow = 532f - position;
                    float spaceAbove = position;
                    float percent = Math.Min(99f, difficulty + Game1.random.Next(10, 45)) / 100f;
                    target = position + Game1.random.Next(
                        (int)Math.Min(0f - spaceAbove, spaceBelow),
                        (int)spaceBelow) * percent;
                    target = Math.Max(0f, Math.Min(532f, target));
                }
            }
            else
            {
                float acceleration = (target - position)
                    / (Game1.random.Next(10, 30) + (100f - Math.Min(100f, difficulty)));
                speed += (acceleration - speed) / 5f;
            }
            position = Math.Max(0f, Math.Min(532f, position + speed));

            // --- Catch progress: fills inside the green bar, drains outside ---
            bool inBar = position + 12f <= bar.bobberBarPos - 32f + bar.bobberBarHeight
                      && position - 16f >= bar.bobberBarPos - 32f;

            if (inBar)
            {
                distanceFromCatching = Math.Min(1f, distanceFromCatching + CatchGainPerTick);
                shake = new Vector2(
                    Game1.random.Next(-10, 11) / 10f,
                    Game1.random.Next(-10, 11) / 10f);
            }
            else
            {
                distanceFromCatching -= CatchLossPerTick;
                shake = Vector2.Zero;

                if (distanceFromCatching <= 0f)
                {
                    distanceFromCatching = 0f;
                    lost = true;
                    Game1.playSound("fishEscape");
                    bar.everythingShakeTimer = 300f;
                }
            }

            // Auto-secure when bar is full
            if (distanceFromCatching >= 1f)
            {
                distanceFromCatching = 1f;
                secured = true;
                Game1.playSound("newArtifact");
            }
        }


        /*********
        ** Private methods — drawing
        *********/

        /// <summary>Draw an extra fish sprite on the bobber track (no horizontal flip).</summary>
        private static void DrawExtraFish(
            SpriteBatch b, BobberBar bar,
            float position, Vector2 shake,
            Texture2D? customTexture, Color fallbackTint)
        {
            if (customTexture != null)
            {
                // Use the custom placeholder texture
                b.Draw(
                    customTexture,
                    new Vector2(
                        bar.xPositionOnScreen + 64 + 18,
                        bar.yPositionOnScreen + 12 + 24 + position)
                    + shake + bar.everythingShake,
                    new Rectangle(0, 0, customTexture.Width, customTexture.Height),
                    Color.White,
                    0f,
                    new Vector2(customTexture.Width / 2f, customTexture.Height / 2f),
                    1.75f,
                    SpriteEffects.None, // No flip — color differentiates fish
                    0.87f
                );
            }
            else
            {
                // Fallback: vanilla fish sprite with color tint, no flip
                b.Draw(
                    Game1.mouseCursors,
                    new Vector2(
                        bar.xPositionOnScreen + 64 + 18,
                        bar.yPositionOnScreen + 12 + 24 + position)
                    + shake + bar.everythingShake,
                    new Rectangle(614, 1840, 20, 20),
                    fallbackTint * 0.9f,
                    0f,
                    new Vector2(10f, 10f),
                    1.75f,
                    SpriteEffects.None,
                    0.87f
                );
            }
        }


        /// <summary>Draw a full-size catch progress bar to the right of the vanilla one.</summary>
        /// <param name="barIndex">0 = first extra bar, 1 = second extra bar.</param>
        private static void DrawProgressBar(
            SpriteBatch b, BobberBar bar,
            float progress, int barIndex)
        {
            int barX = bar.xPositionOnScreen + FirstBarXOffset + barIndex * (BarWidth + BarGap);
            int barY = bar.yPositionOnScreen + 36;
            int trackHeight = BarTrackHeight;

            // Dark background
            b.Draw(Game1.staminaRect,
                new Rectangle(barX - 2, barY - 2, BarWidth + 4, trackHeight + 4),
                Color.Black * 0.6f);

            // Empty track
            b.Draw(Game1.staminaRect,
                new Rectangle(barX, barY, BarWidth, trackHeight),
                Color.DimGray * 0.3f);

            // Fill (from bottom up, red-to-green gradient)
            int fillHeight = (int)(progress * trackHeight);
            if (fillHeight > 0)
            {
                b.Draw(Game1.staminaRect,
                    new Rectangle(barX, barY + trackHeight - fillHeight, BarWidth, fillHeight),
                    Utility.getRedToGreenLerpColor(progress));
            }

            // Border lines (subtle)
            b.Draw(Game1.staminaRect, new Rectangle(barX - 1, barY - 1, 1, trackHeight + 2), Color.Black * 0.4f);
            b.Draw(Game1.staminaRect, new Rectangle(barX + BarWidth, barY - 1, 1, trackHeight + 2), Color.Black * 0.4f);
            b.Draw(Game1.staminaRect, new Rectangle(barX - 1, barY - 1, BarWidth + 2, 1), Color.Black * 0.4f);
            b.Draw(Game1.staminaRect, new Rectangle(barX - 1, barY + trackHeight, BarWidth + 2, 1), Color.Black * 0.4f);
        }


        /*********
        ** Private methods — state management
        *********/

        /// <summary>Reset all per-minigame state.</summary>
        private static void Reset()
        {
            Armed = false;
            CanSpawnThird = false;

            SecondSpawned = false;
            SecondLost = false;
            SecondSecured = false;
            SecondPosition = 0f;
            SecondSpeed = 0f;
            SecondTarget = -1f;
            SecondDistanceFromCatching = 0f;
            SecondShake = Vector2.Zero;

            ThirdSpawned = false;
            ThirdLost = false;
            ThirdSecured = false;
            ThirdPosition = 0f;
            ThirdSpeed = 0f;
            ThirdTarget = -1f;
            ThirdDistanceFromCatching = 0f;
            ThirdShake = Vector2.Zero;

            CachedSecondFishTexture = null;
            CachedThirdFishTexture = null;
        }
    }
}
