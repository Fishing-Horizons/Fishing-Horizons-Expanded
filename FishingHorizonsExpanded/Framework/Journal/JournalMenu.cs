using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Menus;

namespace FishingHorizonsExpanded.Framework.Journal
{
    /// <summary>The Fisherman's Journal book menu.</summary>
    /// <remarks>
    /// Layout: a two-page spread. Spread 0 is the title page; the following spreads list
    /// all fish (4 per page, 8 per spread) grouped into sections by source mod.
    /// Uncaught fish are shown as black silhouettes with their name hidden.
    /// Clicking a fish opens its detail spread: stats on the left page, where/when info on the right.
    /// </remarks>
    internal sealed class JournalMenu : IClickableMenu
    {
        /*********
        ** Constants
        *********/
        /// <summary>The menu width in UI pixels.</summary>
        private const int MenuWidth = 1100;

        /// <summary>The menu height in UI pixels.</summary>
        private const int MenuHeight = 660;

        /// <summary>The number of cells (fish or section headers) per single page.</summary>
        private const int CellsPerPage = 4;

        /// <summary>The sprite draw scale (16px sprites → 64px).</summary>
        private const float SpriteScale = 4f;


        /*********
        ** Fields
        *********/
        /// <summary>The mod translations.</summary>
        private readonly ITranslationHelper I18n;

        /// <summary>The tracked journal progress (sold/gifted/best quality).</summary>
        private readonly ProgressTracker Progress;

        /// <summary>The flat list of cells: section headers and fish entries.</summary>
        private readonly List<Cell> Cells = new();

        /// <summary>The current spread index (0 = title spread).</summary>
        private int Spread;

        /// <summary>The total number of spreads.</summary>
        private readonly int SpreadCount;

        /// <summary>The fish whose detail spread is open, if any.</summary>
        private FishEntry? DetailFish;

        /// <summary>The previous-page arrow.</summary>
        private readonly ClickableTextureComponent PrevArrow;

        /// <summary>The next-page arrow.</summary>
        private readonly ClickableTextureComponent NextArrow;

        /// <summary>The back button shown on the detail spread.</summary>
        private readonly ClickableTextureComponent BackButton;

        /// <summary>A cell in the fish list: either a section header or a fish entry.</summary>
        private readonly record struct Cell(string? Header, FishEntry? Fish);


        /*********
        ** Public methods
        *********/
        public JournalMenu(ITranslationHelper i18n, IModRegistry modRegistry, ProgressTracker progress)
            : base(
                x: (Game1.uiViewport.Width - MenuWidth) / 2,
                y: (Game1.uiViewport.Height - MenuHeight) / 2,
                width: MenuWidth,
                height: MenuHeight,
                showUpperRightCloseButton: true)
        {
            this.I18n = i18n;
            this.Progress = progress;

            // build the fish list
            foreach (JournalSection section in FishRegistry.BuildSections(modRegistry, i18n.Get("menu.journal.section.vanilla")))
            {
                this.Cells.Add(new Cell(section.Title, null));
                foreach (FishEntry fish in section.Fish)
                    this.Cells.Add(new Cell(null, fish));
            }

            int listPages = (int)Math.Ceiling(this.Cells.Count / (double)CellsPerPage);
            this.SpreadCount = 1 + (int)Math.Ceiling(listPages / 2.0);

            // navigation buttons
            this.PrevArrow = new ClickableTextureComponent(
                new Rectangle(this.xPositionOnScreen - 64, this.yPositionOnScreen + this.height - 60, 48, 44),
                Game1.mouseCursors, new Rectangle(352, 495, 12, 11), 4f);
            this.NextArrow = new ClickableTextureComponent(
                new Rectangle(this.xPositionOnScreen + this.width + 16, this.yPositionOnScreen + this.height - 60, 48, 44),
                Game1.mouseCursors, new Rectangle(365, 495, 12, 11), 4f);
            this.BackButton = new ClickableTextureComponent(
                new Rectangle(this.xPositionOnScreen - 64, this.yPositionOnScreen + this.height - 60, 48, 44),
                Game1.mouseCursors, new Rectangle(352, 495, 12, 11), 4f);
        }

        /// <inheritdoc/>
        public override void receiveLeftClick(int x, int y, bool playSound = true)
        {
            // close button always closes the menu
            if (this.upperRightCloseButton?.containsPoint(x, y) == true)
            {
                base.receiveLeftClick(x, y, playSound);
                return;
            }
            base.receiveLeftClick(x, y, playSound);

            // detail spread: any click goes back to the list
            if (this.DetailFish is not null)
            {
                this.DetailFish = null;
                Game1.playSound("shwip");
                return;
            }

            // pagination
            if (this.PrevArrow.containsPoint(x, y) && this.Spread > 0)
            {
                this.Spread--;
                Game1.playSound("shwip");
                return;
            }
            if (this.NextArrow.containsPoint(x, y) && this.Spread < this.SpreadCount - 1)
            {
                this.Spread++;
                Game1.playSound("shwip");
                return;
            }

            // clicking a fish opens its detail spread
            if (this.Spread > 0)
            {
                FishEntry? clicked = this.GetFishAt(x, y);
                if (clicked is not null)
                {
                    this.DetailFish = clicked;
                    Game1.playSound("shwip");
                }
            }
        }

        /// <inheritdoc/>
        public override void receiveKeyPress(Microsoft.Xna.Framework.Input.Keys key)
        {
            // Escape/menu key from the detail spread goes back to the list instead of closing
            if (this.DetailFish is not null && (key == Microsoft.Xna.Framework.Input.Keys.Escape || Game1.options.doesInputListContain(Game1.options.menuButton, key)))
            {
                this.DetailFish = null;
                Game1.playSound("shwip");
                return;
            }
            base.receiveKeyPress(key);
        }

        /// <inheritdoc/>
        public override void performHoverAction(int x, int y)
        {
            base.performHoverAction(x, y);
            this.PrevArrow.tryHover(x, y, 0.2f);
            this.NextArrow.tryHover(x, y, 0.2f);
            this.BackButton.tryHover(x, y, 0.2f);
        }

        /// <inheritdoc/>
        public override void draw(SpriteBatch b)
        {
            // dim the game behind the menu
            b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.5f);

            // book background (placeholder: standard menu texture box split into two pages)
            int pageWidth = this.width / 2;
            drawTextureBox(b, this.xPositionOnScreen, this.yPositionOnScreen, pageWidth, this.height, Color.White);
            drawTextureBox(b, this.xPositionOnScreen + pageWidth, this.yPositionOnScreen, pageWidth, this.height, Color.White);

            if (this.DetailFish is not null)
            {
                this.DrawDetailSpread(b, this.DetailFish, pageWidth);
                this.BackButton.draw(b);
            }
            else if (this.Spread == 0)
            {
                this.DrawTitleSpread(b, pageWidth);
                if (this.Spread < this.SpreadCount - 1)
                    this.NextArrow.draw(b);
            }
            else
            {
                this.DrawListPage(b, this.xPositionOnScreen, pageWidth, pageIndex: (this.Spread - 1) * 2);
                this.DrawListPage(b, this.xPositionOnScreen + pageWidth, pageWidth, pageIndex: (this.Spread - 1) * 2 + 1);
                this.PrevArrow.draw(b);
                if (this.Spread < this.SpreadCount - 1)
                    this.NextArrow.draw(b);
            }

            base.draw(b);
            this.drawMouse(b);
        }


        /*********
        ** Private methods — list view
        *********/
        /// <summary>Draw the title spread (spread 0).</summary>
        private void DrawTitleSpread(SpriteBatch b, int pageWidth)
        {
            string title = this.I18n.Get("menu.journal.title");
            SpriteText.drawStringHorizontallyCenteredAt(
                b,
                title,
                this.xPositionOnScreen + pageWidth / 2,
                this.yPositionOnScreen + 96
            );

            string hint = this.I18n.Get("menu.journal.turn-page-hint");
            Vector2 hintSize = Game1.smallFont.MeasureString(hint);
            b.DrawString(
                Game1.smallFont,
                hint,
                new Vector2(this.xPositionOnScreen + (pageWidth - hintSize.X) / 2, this.yPositionOnScreen + this.height / 2f),
                Game1.textColor * 0.75f
            );
        }

        /// <summary>Get the bounds of a cell slot on a page.</summary>
        private Rectangle GetCellBounds(int pageX, int pageWidth, int slot)
        {
            const int topMargin = 40;
            int cellHeight = (this.height - topMargin - 32) / CellsPerPage;
            return new Rectangle(pageX + 32, this.yPositionOnScreen + topMargin + slot * cellHeight, pageWidth - 64, cellHeight);
        }

        /// <summary>Get the fish cell at the given screen position, if any.</summary>
        private FishEntry? GetFishAt(int x, int y)
        {
            int pageWidth = this.width / 2;
            foreach ((int pageX, int pageIndex) in new[] { (this.xPositionOnScreen, (this.Spread - 1) * 2), (this.xPositionOnScreen + pageWidth, (this.Spread - 1) * 2 + 1) })
            {
                for (int slot = 0; slot < CellsPerPage; slot++)
                {
                    int cellIndex = pageIndex * CellsPerPage + slot;
                    if (cellIndex >= this.Cells.Count)
                        break;
                    if (this.Cells[cellIndex].Fish is FishEntry fish && this.GetCellBounds(pageX, pageWidth, slot).Contains(x, y))
                        return fish;
                }
            }
            return null;
        }

        /// <summary>Draw one list page (up to <see cref="CellsPerPage"/> cells).</summary>
        private void DrawListPage(SpriteBatch b, int pageX, int pageWidth, int pageIndex)
        {
            for (int slot = 0; slot < CellsPerPage; slot++)
            {
                int cellIndex = pageIndex * CellsPerPage + slot;
                if (cellIndex >= this.Cells.Count)
                    break;

                Cell cell = this.Cells[cellIndex];
                Rectangle bounds = this.GetCellBounds(pageX, pageWidth, slot);

                if (cell.Header is not null)
                    this.DrawHeaderCell(b, cell.Header, bounds);
                else if (cell.Fish is not null)
                    this.DrawFishCell(b, cell.Fish, bounds);
            }
        }

        /// <summary>Draw a section header cell.</summary>
        private void DrawHeaderCell(SpriteBatch b, string title, Rectangle bounds)
        {
            Vector2 size = Game1.dialogueFont.MeasureString(title);
            Vector2 position = new(bounds.X + (bounds.Width - size.X) / 2, bounds.Y + (bounds.Height - size.Y) / 2);
            b.DrawString(Game1.dialogueFont, title, position, Game1.textColor);

            // divider line under the title
            int lineY = (int)(position.Y + size.Y + 4);
            b.Draw(Game1.staminaRect, new Rectangle(bounds.X + 16, lineY, bounds.Width - 32, 2), Game1.textColor * 0.35f);
        }

        /// <summary>Draw a fish cell: sprite on top, name and brief info below.</summary>
        private void DrawFishCell(SpriteBatch b, FishEntry fish, Rectangle bounds)
        {
            bool caught = fish.IsCaught();
            int spriteSize = (int)(16 * SpriteScale);

            // sprite (black silhouette if not caught yet)
            Texture2D texture = fish.Data.GetTexture();
            Rectangle sourceRect = fish.Data.GetSourceRect();
            Vector2 spritePos = new(bounds.X + (bounds.Width - spriteSize) / 2f, bounds.Y);
            b.Draw(texture, spritePos, sourceRect, caught ? Color.White : Color.Black, 0f, Vector2.Zero, SpriteScale, SpriteEffects.None, 1f);

            // name (hidden until caught)
            string name = caught ? fish.Data.DisplayName : this.I18n.Get("menu.journal.unknown-fish");
            Vector2 nameSize = Game1.smallFont.MeasureString(name);
            b.DrawString(Game1.smallFont, name, new Vector2(bounds.X + (bounds.Width - nameSize.X) / 2, bounds.Y + spriteSize + 2), Game1.textColor);

            // brief info
            string info = caught
                ? this.I18n.Get("menu.journal.caught-count", new { count = fish.TimesCaught() })
                : this.I18n.Get("menu.journal.not-caught-hint");
            Vector2 infoSize = Game1.smallFont.MeasureString(info);
            b.DrawString(Game1.smallFont, info, new Vector2(bounds.X + (bounds.Width - infoSize.X) / 2, bounds.Y + spriteSize + 2 + nameSize.Y), Game1.textColor * 0.6f);
        }


        /*********
        ** Private methods — detail view
        *********/
        /// <summary>Draw the detail spread for one fish.</summary>
        private void DrawDetailSpread(SpriteBatch b, FishEntry fish, int pageWidth)
        {
            bool caught = fish.IsCaught();
            int leftX = this.xPositionOnScreen;
            int rightX = this.xPositionOnScreen + pageWidth;
            int contentWidth = pageWidth - 96;

            // === left page: name, sprite, description, stats ===
            int y = this.yPositionOnScreen + 40;

            // name
            string name = caught ? fish.Data.DisplayName : this.I18n.Get("menu.journal.unknown-fish");
            SpriteText.drawStringHorizontallyCenteredAt(b, name, leftX + pageWidth / 2, y);
            y += 64;

            // sprite
            int spriteSize = (int)(16 * SpriteScale);
            b.Draw(
                fish.Data.GetTexture(),
                new Vector2(leftX + (pageWidth - spriteSize) / 2f, y),
                fish.Data.GetSourceRect(),
                caught ? Color.White : Color.Black,
                0f, Vector2.Zero, SpriteScale, SpriteEffects.None, 1f);
            y += spriteSize + 12;

            // description (hidden until caught)
            string description = caught ? (fish.Data.Description ?? "") : this.I18n.Get("menu.journal.detail.hidden-description");
            string wrapped = Game1.parseText(description, Game1.smallFont, contentWidth);
            b.DrawString(Game1.smallFont, wrapped, new Vector2(leftX + 48, y), Game1.textColor * 0.9f);
            y += (int)Game1.smallFont.MeasureString(wrapped).Y + 16;

            // stats
            FishProgress progress = this.Progress.Get(fish.QualifiedId);
            var lines = new List<(bool? Check, string Text)>
            {
                (caught, this.I18n.Get("menu.journal.detail.caught")),
            };
            if (caught)
            {
                lines.Add((null, this.I18n.Get("menu.journal.caught-count", new { count = fish.TimesCaught() })));
                lines.Add((null, this.I18n.Get("menu.journal.detail.max-size", new { cm = (int)Math.Round(fish.MaxCaughtSize() * 2.54) })));
                lines.Add((fish.HasBigSpecimen(), this.I18n.Get("menu.journal.detail.big-specimen")));
                lines.Add((progress.Sold, this.I18n.Get("menu.journal.detail.sold")));
                lines.Add((progress.Gifted, this.I18n.Get("menu.journal.detail.gifted")));
                lines.Add((null, this.I18n.Get("menu.journal.detail.best-quality", new { quality = this.GetQualityName(progress.BestQuality) })));
            }

            foreach ((bool? check, string text) in lines)
            {
                int textX = leftX + 48;
                if (check.HasValue)
                {
                    b.Draw(Game1.mouseCursors, new Vector2(textX, y), new Rectangle(check.Value ? 236 : 227, 425, 9, 9), Color.White, 0f, Vector2.Zero, 3f, SpriteEffects.None, 1f);
                    textX += 36;
                }
                b.DrawString(Game1.smallFont, text, new Vector2(textX, y), Game1.textColor);
                y += 34;
            }

            // === right page: where & when ===
            int ry = this.yPositionOnScreen + 48;
            string whereTitle = this.I18n.Get("menu.journal.detail.where-when-title");
            Vector2 whereTitleSize = Game1.dialogueFont.MeasureString(whereTitle);
            b.DrawString(Game1.dialogueFont, whereTitle, new Vector2(rightX + (pageWidth - whereTitleSize.X) / 2, ry), Game1.textColor);
            ry += (int)whereTitleSize.Y + 16;

            string habitat = Game1.parseText(this.BuildHabitatText(fish), Game1.smallFont, contentWidth);
            b.DrawString(Game1.smallFont, habitat, new Vector2(rightX + 48, ry), Game1.textColor * 0.9f);
        }

        /// <summary>Build the translated "where and when to catch" text for a fish.</summary>
        private string BuildHabitatText(FishEntry fish)
        {
            var text = new StringBuilder();

            if (fish.IsTrapFish)
            {
                string water = this.I18n.Get(fish.TrapWaterType == "ocean" ? "menu.journal.detail.water.ocean" : "menu.journal.detail.water.freshwater");
                text.AppendLine(this.I18n.Get("menu.journal.detail.trap-fish", new { water }));
                return text.ToString().TrimEnd();
            }

            // locations (from Data/Locations)
            if (fish.Locations.Count > 0)
            {
                foreach ((string location, HashSet<string> seasons) in fish.Locations.OrderBy(p => p.Key, StringComparer.CurrentCultureIgnoreCase))
                {
                    string seasonText = seasons.Contains("*") || seasons.Count == 0 || seasons.Count >= 4
                        ? this.I18n.Get("menu.journal.detail.season.any")
                        : string.Join(", ", seasons
                            .OrderBy(s => Array.IndexOf(new[] { "spring", "summer", "fall", "winter" }, s))
                            .Select(s => (string)this.I18n.Get($"menu.journal.detail.season.{s}")));
                    text.AppendLine($"• {location} — {seasonText}");
                }
            }
            else
            {
                // fallback: seasons from Data/Fish (mostly for custom fish spawned via conditions we can't parse)
                string seasonText = fish.Seasons.Count is 0 or >= 4
                    ? this.I18n.Get("menu.journal.detail.season.any")
                    : string.Join(", ", fish.Seasons
                        .OrderBy(s => Array.IndexOf(new[] { "spring", "summer", "fall", "winter" }, s))
                        .Select(s => (string)this.I18n.Get($"menu.journal.detail.season.{s}")));
                text.AppendLine(this.I18n.Get("menu.journal.detail.seasons-line", new { seasons = seasonText }));
            }

            // time
            if (fish.TimeRanges.Count > 0)
            {
                bool allDay = fish.TimeRanges.Any(r => r.X <= 600 && r.Y >= 2600);
                string time = allDay
                    ? this.I18n.Get("menu.journal.detail.time.all-day")
                    : string.Join(", ", fish.TimeRanges.Select(r => $"{FormatTime(r.X)}–{FormatTime(r.Y)}"));
                text.AppendLine(this.I18n.Get("menu.journal.detail.time-line", new { time }));
            }

            // weather
            if (fish.Weather is "sunny" or "rainy")
                text.AppendLine(this.I18n.Get($"menu.journal.detail.weather.{fish.Weather}"));

            return text.ToString().TrimEnd();
        }

        /// <summary>Get the translated name for a quality level.</summary>
        private string GetQualityName(int quality)
        {
            return quality switch
            {
                0 => this.I18n.Get("menu.journal.detail.quality.normal"),
                1 => this.I18n.Get("menu.journal.detail.quality.silver"),
                2 => this.I18n.Get("menu.journal.detail.quality.gold"),
                4 => this.I18n.Get("menu.journal.detail.quality.iridium"),
                _ => this.I18n.Get("menu.journal.detail.quality.unknown"),
            };
        }

        /// <summary>Format a game time value (like 1830) as a clock string (like "18:30").</summary>
        private static string FormatTime(int time)
        {
            int hours = time / 100;
            int minutes = time % 100;
            if (hours >= 24)
                hours -= 24;
            return $"{hours}:{minutes:00}";
        }
    }
}
