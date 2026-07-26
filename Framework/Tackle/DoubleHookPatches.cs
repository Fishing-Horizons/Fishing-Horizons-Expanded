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
    /// Gets its own full-size progress bar to the right of the vanilla one.</item>
    /// <item><b>Third fish</b> — only possible with the <em>Feeder Rod + Double Hook</em> combo.
    /// Spawns when the second fish's bar reaches 50% AND the first fish is fully caught.
    /// Gets its own progress bar further to the right.</item>
    /// </list>
    ///
    /// Lifecycle of the first fish:
    /// When the vanilla distanceFromCatching hits 1.0, the first fish is "secured" — its catch sound
    /// plays once, the vanilla fish sprite disappears from the track, and the minigame continues for
    /// the remaining extra fish. A <c>BeforeUpdate</c> prefix prevents the vanilla code from
    /// re-triggering the fadeOut every frame (which caused the "bugged sounds" loop). Once all extra
    /// fish are resolved (secured or lost), we let distanceFromCatching reach 1.0 again so the vanilla
    /// fadeOut proceeds naturally.
    ///
    /// Rendering: extra fish use colored placeholder textures (no horizontal flip). Progress bars use
    /// <see cref="IClickableMenu.drawTextureBox"/> for a native Stardew Valley frame, with the
    /// standard <see cref="Utility.getRedToGreenLerpColor"/> fill — matching the vanilla catch bar.
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


        /*********
        ** Progress bar layout
        *********/
        /// <summary>Width of each extra catch progress bar fill area (matches vanilla).</summary>
        private const int BarFillWidth = 16;

        /// <summary>Height of each extra catch progress bar fill area.</summary>
        private const int BarFillHeight = 548;

        /// <summary>Border thickness from drawTextureBox at scale 4 with 6×6 source (corner = 2 × 4 = 8).</summary>
        private const int BoxBorder = 8;

        /// <summary>Horizontal gap between extra progress bar frames.</summary>
        private const int BarSpacing = 8;

        /// <summary>X offset from xPositionOnScreen to the first extra bar frame (right of the vanilla panel).</summary>
        private const int FirstBarFrameX = 224;

        /// <summary>Y offset from yPositionOnScreen for the top of each extra bar frame.</summary>
        private const int BarFrameY = 16;


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
        private static bool FirstFishSecured;

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

        // -- temporary draw state --
        private static float SavedBobberPosition;


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
                prefix: new HarmonyMethod(typeof(DoubleHookPatches), nameof(BeforeUpdate)),
                postfix: new HarmonyMethod(typeof(DoubleHookPatches), nameof(AfterUpdate))
            );
            harmony.Patch(
                original: AccessTools.Method(typeof(BobberBar), nameof(BobberBar.draw), new[] { typeof(SpriteBatch) }),
                prefix: new HarmonyMethod(typeof(DoubleHookPatches), nameof(BeforeDraw)),
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
                    chance = Math.Clamp(GetFeederRodChance(), 0f, 1f);
                else
                    chance = Math.Clamp(GetDoubleHookChance(), 0f, 1f);

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


        /// <summary>
        /// PREFIX: When the first fish is already secured and extras are still active,
        /// prevent the vanilla update from re-triggering fadeOut every frame.
        /// Forces the vanilla fish inside the player's bar (so distanceFromCatching
        /// only increases, avoiding drain sounds) and clamps distanceFromCatching below 1.0.
        /// </summary>
        private static void BeforeUpdate(BobberBar __instance)
        {
            try
            {
                if (!Armed || !FirstFishSecured)
                    return;
                if (!HasUnresolvedExtras())
                    return;

                // Force the vanilla fish inside the player's green bar so vanilla
                // code treats it as "in bar" → distanceFromCatching only increases,
                // no drain sounds or escape triggers.
                __instance.bobberPosition = __instance.bobberBarPos + __instance.bobberBarHeight / 2f - 16f;

                // Clamp below 1.0 so vanilla doesn't trigger the fadeOut/catch sequence again
                __instance.distanceFromCatching = Math.Min(__instance.distanceFromCatching, 0.99f);

                // Ensure fadeOut stays off
                __instance.fadeOut = false;
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed in {nameof(BeforeUpdate)}:\n{ex}", LogLevel.Error);
            }
        }


        /// <summary>
        /// POSTFIX: Spawn and simulate extra fish. Detect when the first fish is caught
        /// (vanilla set fadeOut = true) and keep the minigame alive for remaining extras.
        /// When all extras are resolved, let distanceFromCatching reach 1.0 so vanilla ends normally.
        /// </summary>
        private static void AfterUpdate(BobberBar __instance)
        {
            try
            {
                if (!Armed || __instance.fadeIn)
                    return;

                // ---------------------------------------------------------------
                // 1. Spawn second fish at 50% of first bar
                // ---------------------------------------------------------------
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
                    return; // nothing else to do until second fish exists
                }

                // ---------------------------------------------------------------
                // 2. Detect first fish catch (vanilla just set fadeOut = true)
                //    This fires ONCE on the frame vanilla first triggers fadeOut.
                // ---------------------------------------------------------------
                if (!FirstFishSecured && __instance.fadeOut && __instance.distanceFromCatching > 0.9f)
                {
                    if (HasUnresolvedExtras())
                    {
                        FirstFishSecured = true;

                        // Undo vanilla's fadeOut so the minigame stays open.
                        // Vanilla already played the catch sound ("jingle1") this frame — perfect.
                        __instance.fadeOut = false;
                        __instance.distanceFromCatching = 0.99f;

                        // Restore scale in case vanilla's fadeOut processing decreased it
                        __instance.scale = 1f;
                    }
                    // If no unresolved extras, let vanilla proceed (fadeOut stays true).
                    return;
                }

                // ---------------------------------------------------------------
                // 3. While first fish is secured: keep minigame alive
                // ---------------------------------------------------------------
                if (FirstFishSecured)
                {
                    if (HasUnresolvedExtras())
                    {
                        // Safety clamp (prefix should handle this, but belt-and-suspenders)
                        __instance.distanceFromCatching = Math.Min(__instance.distanceFromCatching, 0.99f);
                        __instance.fadeOut = false;
                    }
                    else
                    {
                        // All extras resolved — let the vanilla fadeOut proceed naturally
                        __instance.distanceFromCatching = 1f;
                        // Vanilla will set fadeOut on the next frame's update
                    }
                }

                // ---------------------------------------------------------------
                // 4. Update second fish
                // ---------------------------------------------------------------
                if (SecondSpawned && !SecondLost && !SecondSecured)
                {
                    UpdateFish(
                        __instance,
                        ref SecondPosition, ref SecondSpeed, ref SecondTarget,
                        ref SecondDistanceFromCatching, ref SecondShake,
                        out SecondLost, ref SecondSecured
                    );
                }

                // ---------------------------------------------------------------
                // 5. Spawn third fish (feeder rod + double hook only)
                // ---------------------------------------------------------------
                if (CanSpawnThird && !ThirdSpawned && SecondSpawned && !SecondLost)
                {
                    bool firstBarFull = FirstFishSecured || __instance.distanceFromCatching >= 0.99f;
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

                // ---------------------------------------------------------------
                // 6. Update third fish
                // ---------------------------------------------------------------
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


        /// <summary>
        /// PREFIX: Hide the vanilla fish sprite when the first fish is already secured.
        /// We move bobberPosition off-screen before the vanilla draw runs, then restore it after.
        /// </summary>
        private static void BeforeDraw(BobberBar __instance)
        {
            try
            {
                if (!Armed || !FirstFishSecured)
                    return;

                SavedBobberPosition = __instance.bobberPosition;
                __instance.bobberPosition = -10000f; // off-screen → vanilla draws it invisibly
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed in {nameof(BeforeDraw)}:\n{ex}", LogLevel.Error);
            }
        }


        /// <summary>
        /// POSTFIX: Restore vanilla fish position, then draw extra fish sprites and
        /// native-style progress bars.
        /// </summary>
        private static void AfterDraw(BobberBar __instance, SpriteBatch b)
        {
            try
            {
                // Restore vanilla fish position (even if we don't draw anything)
                if (FirstFishSecured)
                    __instance.bobberPosition = SavedBobberPosition;

                if (!Armed || __instance.scale != 1f)
                    return;

                Game1.StartWorldDrawInUI(b);

                int barIndex = 0;

                // --- Second fish ---
                if (SecondSpawned && !SecondLost)
                {
                    // Fish sprite on the track (only while still being caught)
                    if (!SecondSecured)
                    {
                        DrawExtraFish(b, __instance, SecondPosition, SecondShake,
                            CachedSecondFishTexture, new Color(60, 140, 200));
                    }

                    // Progress bar (always visible while fish is alive)
                    DrawProgressBar(b, __instance,
                        SecondSecured ? 1f : SecondDistanceFromCatching,
                        barIndex);
                    barIndex++;
                }

                // --- Third fish ---
                if (ThirdSpawned && !ThirdLost)
                {
                    if (!ThirdSecured)
                    {
                        DrawExtraFish(b, __instance, ThirdPosition, ThirdShake,
                            CachedThirdFishTexture, new Color(220, 140, 40));
                    }

                    DrawProgressBar(b, __instance,
                        ThirdSecured ? 1f : ThirdDistanceFromCatching,
                        barIndex);
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
            distanceFromCatching = 0.5f; // start at 50%

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


        /// <summary>Check whether any extra fish are still in play (not yet resolved).</summary>
        private static bool HasUnresolvedExtras()
        {
            // Second fish still being caught
            if (SecondSpawned && !SecondLost && !SecondSecured)
                return true;

            // Third fish still being caught
            if (ThirdSpawned && !ThirdLost && !ThirdSecured)
                return true;

            // Third fish hasn't spawned but conditions could still be met
            // (second is alive, so the third can still appear)
            if (CanSpawnThird && !ThirdSpawned && SecondSpawned && !SecondLost)
                return true;

            return false;
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
            Vector2 drawPos = new Vector2(
                bar.xPositionOnScreen + 64 + 18,
                bar.yPositionOnScreen + 12 + 24 + position)
                + shake + bar.everythingShake;

            if (customTexture != null)
            {
                b.Draw(
                    customTexture,
                    drawPos,
                    new Rectangle(0, 0, customTexture.Width, customTexture.Height),
                    Color.White,
                    0f,
                    new Vector2(customTexture.Width / 2f, customTexture.Height / 2f),
                    1.75f,
                    SpriteEffects.None,
                    0.87f
                );
            }
            else
            {
                // Fallback: vanilla fish sprite with color tint, no flip
                b.Draw(
                    Game1.mouseCursors,
                    drawPos,
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


        /// <summary>
        /// Draw a native-style progress bar to the right of the vanilla one.
        /// Uses <see cref="IClickableMenu.drawTextureBox"/> for the frame
        /// and <see cref="Utility.getRedToGreenLerpColor"/> for the fill —
        /// the same rendering the vanilla distanceFromCatching bar uses.
        /// </summary>
        /// <param name="barIndex">0 = first extra bar (second fish), 1 = second extra bar (third fish).</param>
        private static void DrawProgressBar(
            SpriteBatch b, BobberBar bar,
            float progress, int barIndex)
        {
            int frameWidth = BarFillWidth + BoxBorder * 2;   // 16 + 16 = 32
            int frameHeight = BarFillHeight + BoxBorder * 2; // 548 + 16 = 564

            int frameX = bar.xPositionOnScreen + FirstBarFrameX
                       + barIndex * (frameWidth + BarSpacing);
            int frameY = bar.yPositionOnScreen + BarFrameY;

            int fillX = frameX + BoxBorder;
            int fillY = frameY + BoxBorder;

            // Native Stardew Valley bordered box (same 9-slice used by menus/tooltips)
            // Source (403, 383, 6, 6) at scale 4 → 8px corners → matches BoxBorder
            IClickableMenu.drawTextureBox(
                b,
                Game1.mouseCursors,
                new Rectangle(403, 383, 6, 6),
                frameX, frameY,
                frameWidth, frameHeight,
                Color.White,
                4f,
                drawShadow: false,
                draw_layer: 0.88f
            );

            // Fill from bottom up — same color gradient as vanilla distanceFromCatching bar
            int fillHeight = (int)(progress * BarFillHeight);
            if (fillHeight > 0)
            {
                b.Draw(
                    Game1.staminaRect,
                    new Rectangle(fillX, fillY + BarFillHeight - fillHeight, BarFillWidth, fillHeight),
                    Utility.getRedToGreenLerpColor(progress)
                );
            }
        }


        /*********
        ** Private methods — state management
        *********/

        /// <summary>Reset all per-minigame state.</summary>
        private static void Reset()
        {
            Armed = false;
            CanSpawnThird = false;
            FirstFishSecured = false;

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

            SavedBobberPosition = 0f;
        }
    }
}
