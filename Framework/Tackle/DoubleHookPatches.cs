using System;
using System.Collections.Generic;
using System.Reflection.Emit;
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
    /// or by the <em>Double Hook</em> tackle on any rod. Spawns once the first catch bar reaches 50%.</item>
    /// <item><b>Third fish</b> — only with the <em>Feeder Rod + Double Hook</em> combo. Spawns when the
    /// second fish's bar reaches 50% AND the first fish is fully caught.</item>
    /// </list>
    ///
    /// <para><b>Rendering</b> — matches vanilla exactly. Vanilla's <c>BobberBar.draw</c> renders the
    /// catch progress bar as a plain <see cref="Game1.staminaRect"/> rectangle:</para>
    /// <code>
    /// b.Draw(Game1.staminaRect,
    ///     new Rectangle(xPositionOnScreen + 124,
    ///                   yPositionOnScreen + 4 + (int)(580f * (1f - distanceFromCatching)),
    ///                   16, (int)(580f * distanceFromCatching)),
    ///     Utility.getRedToGreenLerpColor(distanceFromCatching));
    /// </code>
    /// <para>Each extra fish gets an identical bar shifted right by <see cref="BarSpacing"/> px.</para>
    ///
    /// <para>The translucent bubble behind the minigame is vanilla's first draw call —
    /// <c>mouseCursors</c> source <c>(652, 1685, 52, 157)</c> at <c>Color.White * 0.6f * scale</c>,
    /// and the wooden frame is the second — source <c>(644, 1999, 38, 150)</c>. A transpiler routes
    /// both through <see cref="DrawSprite"/>, which substitutes wider variants of the same artwork.
    /// Only the texture and source rect change, so position/origin/scale/alpha/flip stay vanilla and
    /// both sprites simply extend further to the right.</para>
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
        ** Vanilla draw geometry (from BobberBar.draw)
        *********/
        /// <summary>X offset of the vanilla catch bar from xPositionOnScreen.</summary>
        private const int VanillaBarX = 124;

        /// <summary>Y offset of the vanilla catch bar from yPositionOnScreen.</summary>
        private const int VanillaBarY = 4;

        /// <summary>Width of the vanilla catch bar in pixels.</summary>
        private const int BarWidth = 16;

        /// <summary>Full height of the vanilla catch bar in pixels.</summary>
        private const int BarHeight = 580;

        /// <summary>
        /// Horizontal step between catch bars, in screen px.
        /// <para>Derived from the frame artwork, which is the authority on where a bar may sit: the
        /// wooden frame repeats a 7px unit (3 wood columns + a 4px dark bar channel). At 4× scale
        /// that is 28px. The frame's first channel sits at source cols 32-35, which maps to screen
        /// <c>x+124 .. x+140</c> — exactly vanilla's bar — confirming the alignment.</para>
        /// </summary>
        private const int BarSpacing = 28;

        /// <summary>Vanilla source rects in <c>Game1.mouseCursors</c> that the mod swaps out.</summary>
        private static readonly Rectangle VanillaBubbleSource = new Rectangle(652, 1685, 52, 157);
        private static readonly Rectangle VanillaPanelSource = new Rectangle(644, 1999, 38, 150);


        /*********
        ** Texture asset names
        *********/
        public const string SecondFishTextureAsset = "Mods/waymeeNhaku.FishingHorizonsExpanded/SecondFish";
        public const string ThirdFishTextureAsset = "Mods/waymeeNhaku.FishingHorizonsExpanded/ThirdFish";
        public const string TwoFishBubbleAsset = "Mods/waymeeNhaku.FishingHorizonsExpanded/TwoFishBubble";
        public const string ThreeFishBubbleAsset = "Mods/waymeeNhaku.FishingHorizonsExpanded/ThreeFishBubble";
        public const string TwoFishFrameAsset = "Mods/waymeeNhaku.FishingHorizonsExpanded/TwoFishFrame";
        public const string ThreeFishFrameAsset = "Mods/waymeeNhaku.FishingHorizonsExpanded/ThreeFishFrame";


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
        private static Texture2D? CachedTwoFishBubble;
        private static Texture2D? CachedThreeFishBubble;
        private static Texture2D? CachedTwoFishFrame;
        private static Texture2D? CachedThreeFishFrame;

        /// <summary>Cached opaque bounding boxes, keyed by texture. See <see cref="GetContentBounds"/>.</summary>
        private static readonly Dictionary<Texture2D, Rectangle> ContentBoundsCache = new Dictionary<Texture2D, Rectangle>();

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
                postfix: new HarmonyMethod(typeof(DoubleHookPatches), nameof(AfterDraw)),
                transpiler: new HarmonyMethod(typeof(DoubleHookPatches), nameof(TranspileDraw))
            );
            harmony.Patch(
                original: AccessTools.Method(typeof(FishingRod), nameof(FishingRod.pullFishFromWater)),
                prefix: new HarmonyMethod(typeof(DoubleHookPatches), nameof(BeforePullFishFromWater))
            );
        }


        /*********
        ** Transpiler — widen the translucent bubble
        *********/

        /// <summary>
        /// Route every <c>SpriteBatch.Draw</c> call in <c>BobberBar.draw</c> that uses the
        /// nine-argument float-scale overload through <see cref="DrawSprite"/>, which swaps in wider
        /// artwork for the translucent bubble and the wooden frame when extra fish are in play.
        /// </summary>
        /// <remarks>
        /// Every call site is redirected rather than a specific one by index, so the patch does not
        /// break if the game reorders its draw calls. <see cref="DrawSprite"/> decides what to do
        /// based on the source rect and forwards anything it does not recognise untouched.
        /// </remarks>
        private static IEnumerable<CodeInstruction> TranspileDraw(IEnumerable<CodeInstruction> instructions)
        {
            var target = AccessTools.Method(typeof(SpriteBatch), nameof(SpriteBatch.Draw), new[]
            {
                typeof(Texture2D), typeof(Vector2), typeof(Rectangle?), typeof(Color),
                typeof(float), typeof(Vector2), typeof(float), typeof(SpriteEffects), typeof(float)
            });
            var replacement = AccessTools.Method(typeof(DoubleHookPatches), nameof(DrawSprite));

            int patched = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(target))
                {
                    patched++;
                    // Stack layout is identical (the SpriteBatch is already on the stack as the
                    // instance), so we only swap the callvirt for a call to our static method.
                    yield return new CodeInstruction(OpCodes.Call, replacement)
                    {
                        labels = instruction.labels,
                        blocks = instruction.blocks
                    };
                    continue;
                }

                yield return instruction;
            }

            if (patched == 0)
                Monitor.Log("Could not find any draw calls to patch in BobberBar.draw — the bubble and frame will not widen for extra fish.", LogLevel.Warn);
        }


        /// <summary>
        /// Stand-in for vanilla's sprite draw calls in <c>BobberBar.draw</c>. When extra fish are in
        /// play, swaps the translucent bubble and the wooden frame for their wider variants;
        /// everything else is forwarded to vanilla unchanged.
        /// </summary>
        /// <remarks>
        /// Only the texture and source rect change — position, origin, scale, colour, rotation, flip
        /// and depth all stay exactly as vanilla passed them. Because every template has the same
        /// content height as the sprite it replaces and is anchored on its left edge, the artwork
        /// simply extends further to the right.
        /// </remarks>
        internal static void DrawSprite(
            SpriteBatch b, Texture2D texture, Vector2 position, Rectangle? sourceRectangle,
            Color color, float rotation, Vector2 origin, float scale, SpriteEffects effects, float layerDepth)
        {
            try
            {
                int extras = Armed ? SpawnedExtraCount() : 0;
                if (extras > 0)
                {
                    Texture2D? swap = null;

                    if (sourceRectangle == VanillaBubbleSource)
                        swap = extras == 1 ? CachedTwoFishBubble : CachedThreeFishBubble;
                    else if (sourceRectangle == VanillaPanelSource)
                        swap = extras == 1 ? CachedTwoFishFrame : CachedThreeFishFrame;

                    if (swap != null)
                    {
                        b.Draw(swap, position, GetContentBounds(swap), color, rotation, origin, scale, effects, layerDepth);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed in {nameof(DrawSprite)}:\n{ex}", LogLevel.Error);
            }

            b.Draw(texture, position, sourceRectangle, color, rotation, origin, scale, effects, layerDepth);
        }


        /// <summary>
        /// Get the opaque bounding box of a texture, ignoring transparent padding around the artwork.
        /// </summary>
        /// <remarks>
        /// Using the measured content box rather than the whole canvas means the templates can be
        /// exported with any amount of padding and still line up perfectly — no code change needed
        /// when the art is re-exported. Results are cached; the scan runs once per texture.
        /// </remarks>
        private static Rectangle GetContentBounds(Texture2D texture)
        {
            if (ContentBoundsCache.TryGetValue(texture, out Rectangle cached))
                return cached;

            Rectangle bounds;
            try
            {
                Color[] data = new Color[texture.Width * texture.Height];
                texture.GetData(data);

                int minX = texture.Width, minY = texture.Height, maxX = -1, maxY = -1;
                for (int y = 0; y < texture.Height; y++)
                {
                    for (int x = 0; x < texture.Width; x++)
                    {
                        if (data[y * texture.Width + x].A == 0)
                            continue;
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }

                bounds = maxX < 0
                    ? new Rectangle(0, 0, texture.Width, texture.Height)
                    : new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Could not measure texture bounds, falling back to the full canvas:\n{ex}", LogLevel.Warn);
                bounds = new Rectangle(0, 0, texture.Width, texture.Height);
            }

            ContentBoundsCache[texture] = bounds;
            return bounds;
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

                try
                {
                    CachedSecondFishTexture = ContentHelper.Load<Texture2D>(SecondFishTextureAsset);
                    CachedThirdFishTexture = ContentHelper.Load<Texture2D>(ThirdFishTextureAsset);
                    CachedTwoFishBubble = ContentHelper.Load<Texture2D>(TwoFishBubbleAsset);
                    CachedThreeFishBubble = ContentHelper.Load<Texture2D>(ThreeFishBubbleAsset);
                    CachedTwoFishFrame = ContentHelper.Load<Texture2D>(TwoFishFrameAsset);
                    CachedThreeFishFrame = ContentHelper.Load<Texture2D>(ThreeFishFrameAsset);
                }
                catch (Exception ex)
                {
                    Monitor.Log($"Could not load the multi-fish UI textures, falling back to vanilla art:\n{ex}", LogLevel.Warn);
                    CachedSecondFishTexture = null;
                    CachedThirdFishTexture = null;
                    CachedTwoFishBubble = null;
                    CachedThreeFishBubble = null;
                    CachedTwoFishFrame = null;
                    CachedThreeFishFrame = null;
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


        /// <summary>POSTFIX: Spawn and simulate extra fish; keep the minigame alive until all resolve.</summary>
        private static void AfterUpdate(BobberBar __instance)
        {
            try
            {
                if (!Armed || __instance.fadeIn)
                    return;

                // 1. Spawn second fish at 50% of the first bar
                if (!SecondSpawned)
                {
                    if (__instance.distanceFromCatching >= SecondFishSpawnProgress)
                        SpawnFish(__instance, out SecondSpawned, out SecondPosition, out SecondSpeed, out SecondTarget, out SecondDistanceFromCatching);
                    return;
                }

                // 2. Detect the first fish being caught (vanilla just set fadeOut)
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

                // 3. Keep the minigame alive, or let it end
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

                // 5. Spawn third fish (feeder rod + double hook only)
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


        /// <summary>PREFIX: Hide the vanilla fish sprite once the first fish is secured.</summary>
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


        /// <summary>POSTFIX: Restore the vanilla fish position, then draw extra fish and their catch bars.</summary>
        private static void AfterDraw(BobberBar __instance, SpriteBatch b)
        {
            try
            {
                if (FirstFishSecured)
                    __instance.bobberPosition = SavedBobberPosition;

                if (!Armed || __instance.scale != 1f)
                    return;

                Game1.StartWorldDrawInUI(b);

                // --- Second fish: bar slot 1 ---
                if (SecondSpawned)
                {
                    if (!SecondLost && !SecondSecured)
                        DrawExtraFish(b, __instance, SecondPosition, SecondShake, CachedSecondFishTexture);

                    DrawCatchBar(b, __instance, SecondSecured ? 1f : SecondDistanceFromCatching, slot: 1);
                }

                // --- Third fish: bar slot 2 ---
                if (ThirdSpawned)
                {
                    if (!ThirdLost && !ThirdSecured)
                        DrawExtraFish(b, __instance, ThirdPosition, ThirdShake, CachedThirdFishTexture);

                    DrawCatchBar(b, __instance, ThirdSecured ? 1f : ThirdDistanceFromCatching, slot: 2);
                }

                Game1.EndWorldDrawInUI(b);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed in {nameof(AfterDraw)}:\n{ex}", LogLevel.Error);
            }
        }


        /// <summary>Award extra fish that were secured.</summary>
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
        ** Private methods — drawing
        *********/

        /// <summary>
        /// Draw an extra catch progress bar. Pixel-for-pixel the same as vanilla's bar
        /// (<see cref="Game1.staminaRect"/> + <see cref="Utility.getRedToGreenLerpColor"/>),
        /// just shifted right by <paramref name="slot"/> × <see cref="BarSpacing"/>.
        /// </summary>
        /// <param name="slot">1 for the second fish, 2 for the third.</param>
        private static void DrawCatchBar(SpriteBatch b, BobberBar bar, float progress, int slot)
        {
            b.Draw(
                Game1.staminaRect,
                new Rectangle(
                    bar.xPositionOnScreen + VanillaBarX + slot * BarSpacing,
                    bar.yPositionOnScreen + VanillaBarY + (int)(BarHeight * (1f - progress)),
                    BarWidth,
                    (int)(BarHeight * progress)),
                Utility.getRedToGreenLerpColor(progress)
            );
        }


        /// <summary>
        /// Draw an extra fish on the track, matching vanilla's fish draw call
        /// (origin 10,10 at scale 2, depth 0.88) but without the horizontal flip.
        /// </summary>
        private static void DrawExtraFish(
            SpriteBatch b, BobberBar bar, float position, Vector2 shake, Texture2D? texture)
        {
            Vector2 drawPos = new Vector2(
                bar.xPositionOnScreen + 64 + 18,
                bar.yPositionOnScreen + 12 + 24 + position)
                + shake + bar.everythingShake;

            if (texture != null)
            {
                b.Draw(texture, drawPos, new Rectangle(0, 0, texture.Width, texture.Height),
                    Color.White, 0f, new Vector2(texture.Width / 2f, texture.Height / 2f),
                    2f, SpriteEffects.None, 0.88f);
            }
            else
            {
                // Fallback: the vanilla fish sprite
                b.Draw(Game1.mouseCursors, drawPos, new Rectangle(614, 1840, 20, 20),
                    Color.White, 0f, new Vector2(10f, 10f),
                    2f, SpriteEffects.None, 0.88f);
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


        /// <summary>How many extra fish have spawned (0–2). Drives the bubble width.</summary>
        private static int SpawnedExtraCount()
        {
            int count = 0;
            if (SecondSpawned) count++;
            if (ThirdSpawned) count++;
            return count;
        }


        /// <summary>Check whether any extra fish are still in play.</summary>
        private static bool HasUnresolvedExtras()
        {
            if (SecondSpawned && !SecondLost && !SecondSecured) return true;
            if (ThirdSpawned && !ThirdLost && !ThirdSecured) return true;
            if (CanSpawnThird && !ThirdSpawned && SecondSpawned && !SecondLost) return true;
            return false;
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
            CachedTwoFishBubble = null;
            CachedThreeFishBubble = null;
            CachedTwoFishFrame = null;
            CachedThreeFishFrame = null;
            SavedBobberPosition = 0f;
        }
    }
}
