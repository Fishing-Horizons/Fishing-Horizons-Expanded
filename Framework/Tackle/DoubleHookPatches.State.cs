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
    /// <summary>Tuning, vanilla draw geometry, asset names and the per-minigame state.</summary>
    internal static partial class DoubleHookPatches
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

        // -- second fish --
        /// <summary>Chance that an extra fish is a different species rather than a copy of the first.</summary>
        private const double DifferentSpeciesChance = 0.5;

        /// <summary>How many times to reroll the fish table before settling for a duplicate.</summary>
        private const int SpeciesRollAttempts = 6;


        /*********
        ** Vanilla draw geometry (see the class remarks)
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

        /// <summary>
        /// Vertical step between sonar slots, in screen px. The sonar templates stack a slot every
        /// 21 source px (24 → 45 → 66 tall), which is 84px at the sonar's 4× draw scale.
        /// </summary>
        private const int SonarSlotPitch = 84;

        /// <summary>Vanilla source rects that the mod swaps out.</summary>
        /// <remarks>The first two live in <c>Game1.mouseCursors</c>, the sonar in <c>Game1.mouseCursors_1_6</c>.</remarks>
        private static readonly Rectangle VanillaBubbleSource = new Rectangle(652, 1685, 52, 157);

        private static readonly Rectangle VanillaPanelSource = new Rectangle(644, 1999, 38, 150);

        private static readonly Rectangle VanillaSonarSource = new Rectangle(227, 6, 29, 24);

        /// <summary>The challenge bait window's frame, and its filled and empty fish slots.</summary>
        /// <remarks>All three sit on <see cref="Game1.mouseCursors_1_6"/> and are drawn as one group,
        /// so they move together.</remarks>
        private static readonly Rectangle VanillaChallengeBaitFrameSource = new Rectangle(240, 31, 15, 38);
        private static readonly Rectangle VanillaChallengeBaitFilledSource = new Rectangle(236, 205, 19, 19);
        private static readonly Rectangle VanillaChallengeBaitEmptySource = new Rectangle(217, 205, 19, 19);


        /*********
        ** Texture asset names
        *********/
        public const string SecondFishTextureAsset = "Mods/waymeeNhaku.FishingHorizonsExpanded/SecondFish";
        public const string ThirdFishTextureAsset = "Mods/waymeeNhaku.FishingHorizonsExpanded/ThirdFish";
        public const string TwoFishBubbleAsset = "Mods/waymeeNhaku.FishingHorizonsExpanded/TwoFishBubble";
        public const string ThreeFishBubbleAsset = "Mods/waymeeNhaku.FishingHorizonsExpanded/ThreeFishBubble";
        public const string TwoFishFrameAsset = "Mods/waymeeNhaku.FishingHorizonsExpanded/TwoFishFrame";
        public const string ThreeFishFrameAsset = "Mods/waymeeNhaku.FishingHorizonsExpanded/ThreeFishFrame";
        public const string OneFishSonarAsset = "Mods/waymeeNhaku.FishingHorizonsExpanded/OneFishSonar";
        public const string TwoFishSonarAsset = "Mods/waymeeNhaku.FishingHorizonsExpanded/TwoFishSonar";
        public const string ThreeFishSonarAsset = "Mods/waymeeNhaku.FishingHorizonsExpanded/ThreeFishSonar";


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

        /// <summary>
        /// Qualified item id of each extra fish when it is a different species, else <c>null</c> for
        /// "same as the first fish", along with its own independently rolled size and quality.
        /// </summary>
        private static string? SecondFishId;
        private static int SecondFishSize;
        private static int SecondFishQuality;
        private static string? ThirdFishId;
        private static int ThirdFishSize;
        private static int ThirdFishQuality;

        /// <summary>Prebuilt icons for the extra fish, so the sonar shows what is really on the line.</summary>
        private static Item? SecondFishIcon;
        private static Item? ThirdFishIcon;

        /// <summary>Different-species fish that are landed and waiting to be handed to the player.</summary>
        private static readonly List<(string Id, int Size, int Quality)> PendingAwards = new();

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
        private static Texture2D? CachedOneFishSonar;
        private static Texture2D? CachedTwoFishSonar;
        private static Texture2D? CachedThreeFishSonar;

        /// <summary>Cached opaque bounding boxes, keyed by texture. See <see cref="GetContentBounds"/>.</summary>
        private static readonly Dictionary<Texture2D, Rectangle> ContentBoundsCache = new Dictionary<Texture2D, Rectangle>();

        // -- temporary draw state --
        private static float SavedBobberPosition;

        /// <summary>Whether the vanilla fish was genuinely inside the bar this tick, ignoring any override.</summary>
        private static bool VanillaFishInBar;

        /// <summary>Whether the first fish was inside the green bar on the previous tick.</summary>
        private static bool FirstFishWasInBar;

        /// <summary>Whether <see cref="AdjustBobberInBar"/> forced <c>bobberInBar</c> from false to true this tick.</summary>
        private static bool ForcedInBar;

        /// <summary>The first fish's state as it stood before the current update tick.</summary>
        private static float DistanceBeforeUpdate;
        private static bool PerfectBeforeUpdate;
        private static int FishSizeBeforeUpdate;
        private static int ChallengeBaitBeforeUpdate;

        /// <summary>The minigame currently being drawn, so draw helpers can read its position.</summary>
        private static BobberBar? CurrentBar;


        /*********
        ** Private methods — state management
        *********/
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

        private static void Reset()
        {
            Armed = false;
            CanSpawnThird = false;
            FirstFishSecured = false;

            SecondFishIcon = null;
            ThirdFishIcon = null;
            SecondFishId = null;
            SecondFishSize = 0;
            SecondFishQuality = 0;
            ThirdFishId = null;
            ThirdFishSize = 0;
            ThirdFishQuality = 0;
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
            CachedOneFishSonar = null;
            CachedTwoFishSonar = null;
            CachedThreeFishSonar = null;
            SavedBobberPosition = 0f;
            VanillaFishInBar = false;
            FirstFishWasInBar = false;
            ForcedInBar = false;
            DistanceBeforeUpdate = 0f;
            PerfectBeforeUpdate = false;
            FishSizeBeforeUpdate = 0;
            ChallengeBaitBeforeUpdate = 0;
            CurrentBar = null;
        }
    }
}
