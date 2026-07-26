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
    /// Rendering uses custom backwindow sprites (<c>2-fish-backwindow-bp.png</c> and
    /// <c>3-fish-backwindow-bp.png</c>) to seamlessly extend the vanilla BobberBar panel
    /// to the right. The panel grows gradually — the 2-fish backwindow appears when the
    /// second fish spawns, then switches to the 3-fish backwindow when the third spawns.
    /// Progress bars use <see cref="Utility.getRedToGreenLerpColor"/> fill, matching vanilla.
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
        ** Backwindow panel layout
        *********/
        // The vanilla BobberBar draws its background from Game1.mouseCursors at
        // source (644, 1999, 38, 150), scale 4×, position (xPositionOnScreen + 64, yPositionOnScreen).
        // Our backwindow templates extend this panel to the right.
        //
        // Template alignment: template col 3 = vanilla panel col 0 (left border).
        // Template cols 0–2 are transparent padding. The interior starts at col ~6.
        //
        // Extension drawing: we draw the right portion of the template (starting from
        // col 38, which overlaps the last 3 vanilla panel cols to cover the right border),
        // seamlessly extending the panel with new bar column area.

        /// <summary>Vanilla panel x-offset from xPositionOnScreen.</summary>
        private const int VanillaPanelDrawX = 64;

        /// <summary>Vanilla panel source width in pixels (38 cols at source scale).</summary>
        private const int VanillaPanelSourceW = 38;

        /// <summary>Column offset: template col 3 = vanilla col 0.</summary>
        private const int TemplateColOffset = 3;

        /// <summary>How many vanilla cols we overlap to cover the right border seam.</summary>
        private const int ExtOverlapCols = 3;

        /// <summary>Source column where we start drawing the extension (= TemplateColOffset + VanillaPanelSourceW - ExtOverlapCols).</summary>
        private const int ExtSrcStartCol = TemplateColOffset + VanillaPanelSourceW - ExtOverlapCols; // 38

        /// <summary>Width of each extra bar slot in source pixels (from 3-fish-example analysis).</summary>
        private const int BarSlotSourceW = 7;

        /// <summary>Width of each extra bar slot at 4× scale.</summary>
        private const int BarSlotPx = BarSlotSourceW * 4; // 28

        /// <summary>Progress fill width (4 source cols × 4 = 16px, matches vanilla catch bar).</summary>
        private const int BarFillWidth = 16;

        /// <summary>Horizontal inset of fill from the left edge of a bar slot (centers 16px in 28px).</summary>
        private const int BarFillInset = (BarSlotPx - BarFillWidth) / 2; // 6

        /// <summary>Progress fill height in pixels.</summary>
        private const int BarFillHeight = 568;

        /// <summary>Progress fill y-offset from yPositionOnScreen.</summary>
        private const int BarFillYOffset = 16;


        /*********
        ** Texture asset names
        *********/
        public const string SecondFishTextureAsset = "Mods/waymeeNhaku.FishingHorizonsExpanded/SecondFish";
        public const string ThirdFishTextureAsset = "Mods/waymeeNhaku.FishingHorizonsExpanded/ThirdFish";
        public const string TwoFishBackwindowAsset = "Mods/waymeeNhaku.FishingHorizonsExpanded/TwoFishBackwindow";
        public const string ThreeFishBackwindowAsset = "Mods/waymeeNhaku.FishingHorizonsExpanded/ThreeFishBackwindow";


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
        private static Texture2D? CachedTwoFishBg;
        private static Texture2D? CachedThreeFishBg;

        // -- temporary draw state --
        private static float SavedBobberPosition;


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

                var rod = Game1.player.CurrentTool as FishingRod;
                bool hasFeederRod = rod?.QualifiedItemId == Rods.RodsModule.FeederRodQualifiedId;
                bool hasDoubleHook = __instance.bobbers?.Contains(TackleModule.DoubleHookQualifiedId) == true;

                if (!hasFeederRod && !hasDoubleHook)
                    return;

                float chance = hasFeederRod
                    ? Math.Clamp(GetFeederRodChance(), 0f, 1f)
                    : Math.Clamp(GetDoubleHookChance(), 0f, 1f);

                Armed = Game1.random.NextDouble() < (double)chance;
                CanSpawnThird = hasFeederRod && hasDoubleHook;

                // Pre-load textures
                try
                {
                    CachedSecondFishTexture = ContentHelper.Load<Texture2D>(SecondFishTextureAsset);
                    CachedThirdFishTexture = ContentHelper.Load<Texture2D>(ThirdFishTextureAsset);
                    CachedTwoFishBg = ContentHelper.Load<Texture2D>(TwoFishBackwindowAsset);
                    CachedThreeFishBg = ContentHelper.Load<Texture2D>(ThreeFishBackwindowAsset);
                }
                catch
                {
                    CachedSecondFishTexture = null;
                    CachedThirdFishTexture = null;
                    CachedTwoFishBg = null;
                    CachedThreeFishBg = null;
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
        /// </summary>
        private static void BeforeUpdate(BobberBar __instance)
        {
            try
            {
                if (!Armed || !FirstFishSecured)
                    return;
                if (!HasUnresolvedExtras())
                    return;

                __instance.bobberPosition = __instance.bobberBarPos + __instance.bobberBarHeight / 2f - 16f;
                __instance.distanceFromCatching = Math.Min(__instance.distanceFromCatching, 0.99f);
                __instance.fadeOut = false;
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed in {nameof(BeforeUpdate)}:\n{ex}", LogLevel.Error);
            }
        }


        /// <summary>
        /// POSTFIX: Spawn and simulate extra fish. Detect first-fish catch, keep
        /// minigame alive for remaining extras, end when all resolved.
        /// </summary>
        private static void AfterUpdate(BobberBar __instance)
        {
            try
            {
                if (!Armed || __instance.fadeIn)
                    return;

                // 1. Spawn second fish at 50%
                if (!SecondSpawned)
                {
                    if (__instance.distanceFromCatching >= SecondFishSpawnProgress)
                        SpawnFish(__instance, out SecondSpawned, out SecondPosition, out SecondSpeed, out SecondTarget, out SecondDistanceFromCatching);
                    return;
                }

                // 2. Detect first fish catch
                if (!FirstFishSecured && __instance.fadeOut && __instance.distanceFromCatching > 0.9f)
                {
                    if (HasUnresolvedExtras())
                    {
                        FirstFishSecured = true;
                        __instance.fadeOut = false;
                        __instance.distanceFromCatching = 0.99f;
                        __instance.scale = 1f;
                    }
                    return;
                }

                // 3. Keep minigame alive or let it end
                if (FirstFishSecured)
                {
                    if (HasUnresolvedExtras())
                    {
                        __instance.distanceFromCatching = Math.Min(__instance.distanceFromCatching, 0.99f);
                        __instance.fadeOut = false;
                    }
                    else
                    {
                        __instance.distanceFromCatching = 1f;
                    }
                }

                // 4. Update second fish
                if (SecondSpawned && !SecondLost && !SecondSecured)
                    UpdateFish(__instance, ref SecondPosition, ref SecondSpeed, ref SecondTarget, ref SecondDistanceFromCatching, ref SecondShake, out SecondLost, ref SecondSecured);

                // 5. Spawn third fish
                if (CanSpawnThird && !ThirdSpawned && SecondSpawned && !SecondLost)
                {
                    bool firstFull = FirstFishSecured || __instance.distanceFromCatching >= 0.99f;
                    bool secondHalf = SecondDistanceFromCatching >= ThirdFishSpawnProgress;
                    if (firstFull && secondHalf)
                        SpawnFish(__instance, out ThirdSpawned, out ThirdPosition, out ThirdSpeed, out ThirdTarget, out ThirdDistanceFromCatching);
                }

                // 6. Update third fish
                if (ThirdSpawned && !ThirdLost && !ThirdSecured)
                    UpdateFish(__instance, ref ThirdPosition, ref ThirdSpeed, ref ThirdTarget, ref ThirdDistanceFromCatching, ref ThirdShake, out ThirdLost, ref ThirdSecured);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed in {nameof(AfterUpdate)}:\n{ex}", LogLevel.Error);
                Armed = false;
            }
        }


        /// <summary>PREFIX: Hide the vanilla fish sprite when first fish is secured.</summary>
        private static void BeforeDraw(BobberBar __instance)
        {
            try
            {
                if (!Armed || !FirstFishSecured) return;
                SavedBobberPosition = __instance.bobberPosition;
                __instance.bobberPosition = -10000f;
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed in {nameof(BeforeDraw)}:\n{ex}", LogLevel.Error);
            }
        }


        /// <summary>
        /// POSTFIX: Restore vanilla fish position, draw backwindow panel extension,
        /// extra fish sprites, and progress bar fills.
        /// </summary>
        private static void AfterDraw(BobberBar __instance, SpriteBatch b)
        {
            try
            {
                if (FirstFishSecured)
                    __instance.bobberPosition = SavedBobberPosition;

                if (!Armed || __instance.scale != 1f)
                    return;

                Game1.StartWorldDrawInUI(b);

                // --- Backwindow panel extension (drawn first = behind bar fills) ---
                DrawBackwindowExtension(b, __instance);

                int barIndex = 0;

                // --- Second fish ---
                if (SecondSpawned && !SecondLost)
                {
                    if (!SecondSecured)
                        DrawExtraFish(b, __instance, SecondPosition, SecondShake, CachedSecondFishTexture, new Color(60, 140, 200));
                    DrawProgressBar(b, __instance, SecondSecured ? 1f : SecondDistanceFromCatching, barIndex);
                    barIndex++;
                }

                // --- Third fish ---
                if (ThirdSpawned && !ThirdLost)
                {
                    if (!ThirdSecured)
                        DrawExtraFish(b, __instance, ThirdPosition, ThirdShake, CachedThirdFishTexture, new Color(220, 140, 40));
                    DrawProgressBar(b, __instance, ThirdSecured ? 1f : ThirdDistanceFromCatching, barIndex);
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
                if (!Armed) return;
                if (SecondSecured) numCaught += 1;
                if (ThirdSecured) numCaught += 1;
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

        private static void SpawnFish(
            BobberBar bar, out bool spawned,
            out float position, out float speed, out float target,
            out float distanceFromCatching)
        {
            spawned = true;
            speed = 0f;
            target = -1f;
            distanceFromCatching = 0.5f;

            float barCenter = bar.bobberBarPos + bar.bobberBarHeight / 2f;
            position = barCenter < 274f
                ? Game1.random.Next(350, 500)
                : Game1.random.Next(30, 180);

            Game1.playSound("FishHit");
            bar.everythingShakeTimer = 300f;
        }


        private static void UpdateFish(
            BobberBar bar,
            ref float position, ref float speed, ref float target,
            ref float distanceFromCatching, ref Vector2 shake,
            out bool lost, ref bool secured)
        {
            lost = false;
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

            bool inBar = position + 12f <= bar.bobberBarPos - 32f + bar.bobberBarHeight
                      && position - 16f >= bar.bobberBarPos - 32f;

            if (inBar)
            {
                distanceFromCatching = Math.Min(1f, distanceFromCatching + CatchGainPerTick);
                shake = new Vector2(Game1.random.Next(-10, 11) / 10f, Game1.random.Next(-10, 11) / 10f);
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

            if (distanceFromCatching >= 1f)
            {
                distanceFromCatching = 1f;
                secured = true;
                Game1.playSound("newArtifact");
            }
        }


        private static bool HasUnresolvedExtras()
        {
            if (SecondSpawned && !SecondLost && !SecondSecured) return true;
            if (ThirdSpawned && !ThirdLost && !ThirdSecured) return true;
            if (CanSpawnThird && !ThirdSpawned && SecondSpawned && !SecondLost) return true;
            return false;
        }


        /*********
        ** Private methods — drawing
        *********/

        /// <summary>
        /// Draw the right extension of the backwindow template, seamlessly continuing
        /// the vanilla BobberBar panel to the right. Picks the 2-fish or 3-fish template
        /// depending on how many extra fish are currently spawned. Does nothing if no
        /// extra fish are active yet — the panel grows gradually.
        /// </summary>
        private static void DrawBackwindowExtension(SpriteBatch b, BobberBar bar)
        {
            if (!SecondSpawned)
                return;

            // Pick the wider template if the third fish is in play
            Texture2D? template = ThirdSpawned ? CachedThreeFishBg : CachedTwoFishBg;
            if (template == null)
                return;

            // Source rect: start from ExtSrcStartCol (col 38) to the template's right edge.
            // This overlaps the last 3 vanilla panel cols (covering the right border) and
            // continues into the extension area with the template's interior + right border.
            int srcW = template.Width - ExtSrcStartCol;
            int srcH = template.Height;

            // Draw position: align so template col 38 sits at vanilla panel col 35 (= col 38 - TemplateColOffset)
            float drawX = bar.xPositionOnScreen + VanillaPanelDrawX
                        + (VanillaPanelSourceW - ExtOverlapCols) * 4f;
            float drawY = bar.yPositionOnScreen;

            b.Draw(
                template,
                new Vector2(drawX, drawY),
                new Rectangle(ExtSrcStartCol, 0, srcW, srcH),
                Color.White,
                0f,
                Vector2.Zero,
                4f,
                SpriteEffects.None,
                0.89f // same depth as vanilla panel background
            );
        }


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
                b.Draw(customTexture, drawPos,
                    new Rectangle(0, 0, customTexture.Width, customTexture.Height),
                    Color.White, 0f,
                    new Vector2(customTexture.Width / 2f, customTexture.Height / 2f),
                    1.75f, SpriteEffects.None, 0.87f);
            }
            else
            {
                b.Draw(Game1.mouseCursors, drawPos,
                    new Rectangle(614, 1840, 20, 20),
                    fallbackTint * 0.9f, 0f, new Vector2(10f, 10f),
                    1.75f, SpriteEffects.None, 0.87f);
            }
        }


        /// <summary>
        /// Draw a progress bar fill inside the backwindow extension.
        /// Each bar slot is <see cref="BarSlotPx"/> wide (7 source cols × 4 = 28px).
        /// The fill is centered within the slot using <see cref="BarFillInset"/>.
        /// Uses <see cref="Utility.getRedToGreenLerpColor"/> — same as the vanilla catch bar.
        /// </summary>
        /// <param name="barIndex">0 = first extra bar (second fish), 1 = second extra bar (third fish).</param>
        private static void DrawProgressBar(
            SpriteBatch b, BobberBar bar,
            float progress, int barIndex)
        {
            // Bar slot starts right at the vanilla panel's right edge
            int slotX = bar.xPositionOnScreen + VanillaPanelDrawX + VanillaPanelSourceW * 4
                      + barIndex * BarSlotPx;
            int fillX = slotX + BarFillInset;
            int fillY = bar.yPositionOnScreen + BarFillYOffset;

            int fillH = (int)(progress * BarFillHeight);
            if (fillH > 0)
            {
                b.Draw(
                    Game1.staminaRect,
                    new Rectangle(fillX, fillY + BarFillHeight - fillH, BarFillWidth, fillH),
                    Utility.getRedToGreenLerpColor(progress)
                );
            }
        }


        /*********
        ** Private methods — state management
        *********/

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
            CachedTwoFishBg = null;
            CachedThreeFishBg = null;
            SavedBobberPosition = 0f;
        }
    }
}
