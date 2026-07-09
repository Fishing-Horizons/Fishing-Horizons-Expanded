using System;
using System.Collections.Generic;
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
    /// Clicking a fish opens its detail page (milestone 3 — not implemented yet).
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

        /// <summary>The flat list of cells: section headers and fish entries.</summary>
        private readonly List<Cell> Cells = new();

        /// <summary>The current spread index (0 = title spread).</summary>
        private int Spread;

        /// <summary>The total number of spreads.</summary>
        private readonly int SpreadCount;

        /// <summary>The previous-page arrow.</summary>
        private readonly ClickableTextureComponent PrevArrow;

        /// <summary>The next-page arrow.</summary>
        private readonly ClickableTextureComponent NextArrow;

        /// <summary>A cell in the fish list: either a section header or a fish entry.</summary>
        private readonly record struct Cell(string? Header, FishEntry? Fish);


        /*********
        ** Public methods
        *********/
        public JournalMenu(ITranslationHelper i18n, IModRegistry modRegistry)
            : base(
                x: (Game1.uiViewport.Width - MenuWidth) / 2,
                y: (Game1.uiViewport.Height - MenuHeight) / 2,
                width: MenuWidth,
                height: MenuHeight,
                showUpperRightCloseButton: true)
        {
            this.I18n = i18n;

            // build the fish list
            foreach (JournalSection section in FishRegistry.BuildSections(modRegistry, i18n.Get("menu.journal.section.vanilla")))
            {
                this.Cells.Add(new Cell(section.Title, null));
                foreach (FishEntry fish in section.Fish)
                    this.Cells.Add(new Cell(null, fish));
            }

            int listPages = (int)Math.Ceiling(this.Cells.Count / (double)CellsPerPage);
            this.SpreadCount = 1 + (int)Math.Ceiling(listPages / 2.0);

            // pagination arrows
            this.PrevArrow = new ClickableTextureComponent(
                new Rectangle(this.xPositionOnScreen - 64, this.yPositionOnScreen + this.height - 60, 48, 44),
                Game1.mouseCursors, new Rectangle(352, 495, 12, 11), 4f);
            this.NextArrow = new ClickableTextureComponent(
                new Rectangle(this.xPositionOnScreen + this.width + 16, this.yPositionOnScreen + this.height - 60, 48, 44),
                Game1.mouseCursors, new Rectangle(365, 495, 12, 11), 4f);
        }

        /// <inheritdoc/>
        public override void receiveLeftClick(int x, int y, bool playSound = true)
        {
            base.receiveLeftClick(x, y, playSound);

            if (this.PrevArrow.containsPoint(x, y) && this.Spread > 0)
            {
                this.Spread--;
                Game1.playSound("shwip");
            }
            else if (this.NextArrow.containsPoint(x, y) && this.Spread < this.SpreadCount - 1)
            {
                this.Spread++;
                Game1.playSound("shwip");
            }

            // TODO milestone 3: clicking a caught fish opens its detail spread
        }

        /// <inheritdoc/>
        public override void performHoverAction(int x, int y)
        {
            base.performHoverAction(x, y);
            this.PrevArrow.tryHover(x, y, 0.2f);
            this.NextArrow.tryHover(x, y, 0.2f);
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

            if (this.Spread == 0)
                this.DrawTitleSpread(b, pageWidth);
            else
            {
                this.DrawListPage(b, this.xPositionOnScreen, pageWidth, pageIndex: (this.Spread - 1) * 2);
                this.DrawListPage(b, this.xPositionOnScreen + pageWidth, pageWidth, pageIndex: (this.Spread - 1) * 2 + 1);
            }

            // pagination arrows
            if (this.Spread > 0)
                this.PrevArrow.draw(b);
            if (this.Spread < this.SpreadCount - 1)
                this.NextArrow.draw(b);

            base.draw(b);
            this.drawMouse(b);
        }


        /*********
        ** Private methods
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

        /// <summary>Draw one list page (up to <see cref="CellsPerPage"/> cells).</summary>
        private void DrawListPage(SpriteBatch b, int pageX, int pageWidth, int pageIndex)
        {
            const int topMargin = 40;
            int cellHeight = (this.height - topMargin - 32) / CellsPerPage;

            for (int slot = 0; slot < CellsPerPage; slot++)
            {
                int cellIndex = pageIndex * CellsPerPage + slot;
                if (cellIndex >= this.Cells.Count)
                    break;

                Cell cell = this.Cells[cellIndex];
                int cellY = this.yPositionOnScreen + topMargin + slot * cellHeight;

                if (cell.Header is not null)
                    this.DrawHeaderCell(b, cell.Header, pageX, pageWidth, cellY, cellHeight);
                else if (cell.Fish is not null)
                    this.DrawFishCell(b, cell.Fish, pageX, pageWidth, cellY, cellHeight);
            }
        }

        /// <summary>Draw a section header cell.</summary>
        private void DrawHeaderCell(SpriteBatch b, string title, int pageX, int pageWidth, int cellY, int cellHeight)
        {
            Vector2 size = Game1.dialogueFont.MeasureString(title);
            Vector2 position = new(pageX + (pageWidth - size.X) / 2, cellY + (cellHeight - size.Y) / 2);
            b.DrawString(Game1.dialogueFont, title, position, Game1.textColor);

            // divider line under the title
            int lineY = (int)(position.Y + size.Y + 4);
            b.Draw(Game1.staminaRect, new Rectangle(pageX + 48, lineY, pageWidth - 96, 2), Game1.textColor * 0.35f);
        }

        /// <summary>Draw a fish cell: sprite on top, name and brief info below.</summary>
        private void DrawFishCell(SpriteBatch b, FishEntry fish, int pageX, int pageWidth, int cellY, int cellHeight)
        {
            bool caught = fish.IsCaught();
            int spriteSize = (int)(16 * SpriteScale);

            // sprite (black silhouette if not caught yet)
            Texture2D texture = fish.Data.GetTexture();
            Rectangle sourceRect = fish.Data.GetSourceRect();
            Vector2 spritePos = new(pageX + (pageWidth - spriteSize) / 2f, cellY);
            b.Draw(texture, spritePos, sourceRect, caught ? Color.White : Color.Black, 0f, Vector2.Zero, SpriteScale, SpriteEffects.None, 1f);

            // name (hidden until caught)
            string name = caught ? fish.Data.DisplayName : this.I18n.Get("menu.journal.unknown-fish");
            Vector2 nameSize = Game1.smallFont.MeasureString(name);
            b.DrawString(Game1.smallFont, name, new Vector2(pageX + (pageWidth - nameSize.X) / 2, cellY + spriteSize + 2), Game1.textColor);

            // brief info
            string info = caught
                ? this.I18n.Get("menu.journal.caught-count", new { count = fish.TimesCaught() })
                : this.I18n.Get("menu.journal.not-caught-hint");
            Vector2 infoSize = Game1.smallFont.MeasureString(info);
            b.DrawString(Game1.smallFont, info, new Vector2(pageX + (pageWidth - infoSize.X) / 2, cellY + spriteSize + 2 + nameSize.Y), Game1.textColor * 0.6f);
        }
    }
}
