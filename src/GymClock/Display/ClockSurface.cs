using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Dc = System.Drawing.Color;

namespace GymClock.Wpf
{
    /// <summary>
    /// Draws one <see cref="DisplayFrame"/>: the clock on the projector (build guide P3.1 to P3.3,
    /// P3.7, P3.9). It decides nothing about the session; every word and colour comes from the
    /// frame. Every size is a fraction of the drawing height H (build guide 3.4), so the clock
    /// scales to any screen, and every text is shrunk to fit its box rather than cut off.
    /// </summary>
    public sealed class ClockSurface : FrameworkElement
    {
        public const string StandardLayout = "standard";
        public const string BigNumberLayout = "bignumber";
        public const string StationsLayout = "stations";

        private static readonly FontFamily TextFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");
        private static readonly FontFamily NumberFamily = new FontFamily("Segoe UI");

        private static readonly Typeface Regular = new Typeface(TextFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        private static readonly Typeface Medium = new Typeface(TextFamily, FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);
        private static readonly Typeface SemiBold = new Typeface(TextFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        private static readonly Typeface Bold = new Typeface(TextFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        private static readonly Typeface ExtraBold = new Typeface(TextFamily, FontStyles.Normal, FontWeights.ExtraBold, FontStretches.Normal);
        private static readonly Typeface Number = new Typeface(NumberFamily, FontStyles.Normal, FontWeights.Black, FontStretches.Normal);

        private static readonly Color Ink = Colors.White;
        private static readonly Color InkSoft = Color.FromArgb(235, 255, 255, 255);
        private static readonly Color InkMuted = Color.FromArgb(200, 255, 255, 255);
        private static readonly Color InkFaint = Color.FromArgb(160, 255, 255, 255);
        private static readonly Color Amber = Color.FromArgb(235, 255, 235, 140);

        private const double FadeMilliseconds = 200;

        private readonly Dictionary<string, FormattedText> _fits = new Dictionary<string, FormattedText>();
        private readonly Stopwatch _time = Stopwatch.StartNew();

        private DisplayFrame _frame;
        private Color _fadeFrom;
        private Color _fadeTo;
        private double _fadeStartMs = double.NegativeInfinity;
        private bool _hasShown;

        public ClockSurface()
        {
            SnapsToDevicePixels = true;
        }

        /// <summary>The frame to draw. Setting it redraws.</summary>
        public DisplayFrame Frame
        {
            get { return _frame; }
            set
            {
                _frame = value;
                InvalidateVisual();
            }
        }

        /// <summary>standard, bignumber or stations (the displaylayout setting).</summary>
        public string Layout { get; set; } = StandardLayout;

        /// <summary>0 to 8: percent of the smaller screen side kept clear on every side (the safemargin setting).</summary>
        public int SafeMargin { get; set; }

        /// <summary>
        /// True: no cross-fade and no countdown pulse; the background flashes in the last three
        /// seconds as it always has. False: the background fades between phases and the countdown
        /// pulses instead of the flash.
        /// </summary>
        public bool ReduceMotion { get; set; }

        // ================================================================ render

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth;
            double h = ActualHeight;
            if (w < 40 || h < 40) return;

            DisplayFrame f = _frame;
            if (f == null)
            {
                dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, w, h));
                return;
            }

            Color background = ReduceMotion ? ToMedia(f.Background) : Faded(ToMedia(f.BaseBackground));
            dc.DrawRectangle(Fill(background), null, new Rect(0, 0, w, h));

            // The safe margin keeps everything but the background colour away from the edges.
            double margin = Math.Max(0, Math.Min(8, SafeMargin)) / 100.0 * Math.Min(w, h);
            Rect area = new Rect(margin, margin, Math.Max(10, w - (2 * margin)), Math.Max(10, h - (2 * margin)));
            double H = area.Height;

            Rect main;
            if (f.IsParallel) main = DrawParallel(dc, f, area, H);
            else main = DrawStandard(dc, f, area, H, background);

            DrawToast(dc, f, main, H);
        }

        // ================================================================ standard

        private Rect DrawStandard(DrawingContext dc, DisplayFrame f, Rect area, double H, Color background)
        {
            string layout = TimerSettings.ReadLayout(Layout);
            bool showPanel = f.Stations.Count > 0 && layout != BigNumberLayout;
            double panelWidth = showPanel ? area.Width * (layout == StationsLayout ? 0.5 : 0.234) : 0;

            if (showPanel)
                DrawStationPanel(dc, f, new Rect(area.X, area.Y, panelWidth, area.Height), H, background, layout == StationsLayout);

            double side = H * 0.056;
            Rect main = new Rect(area.X + panelWidth + side, area.Y + (H * 0.033),
                Math.Max(10, area.Width - panelWidth - (2 * side)), Math.Max(10, area.Height - (H * 0.033) - (H * 0.025)));

            double top = DrawTop(dc, f, main, H);
            double bottom = DrawBottom(dc, f, main, H, background, layout != BigNumberLayout, true);
            DrawCentreStack(dc, f, new Rect(main.X, top, main.Width, Math.Max(10, bottom - top)), H, layout);
            return main;
        }

        /// <summary>Program name, session time, licence and update lines (left); time of day (right). Returns the y below them.</summary>
        private double DrawTop(DrawingContext dc, DisplayFrame f, Rect main, double H)
        {
            double timeWidth = 0;
            if (!string.IsNullOrEmpty(f.TimeOfDay))
            {
                FormattedText time = Fit(f.TimeOfDay, Bold, H * 0.050, main.Width * 0.35, H * 0.075, InkSoft);
                dc.DrawText(time, new Point(main.Right - time.WidthIncludingTrailingWhitespace, main.Y));
                timeWidth = time.WidthIncludingTrailingWhitespace + (H * 0.03);
            }

            double leftWidth = Math.Max(10, main.Width - timeWidth);
            double y = main.Y;
            y = TopLine(dc, f.Summary, Bold, H * 0.033, InkSoft, main.X, y, leftWidth, H * 0.046);
            y = TopLine(dc, f.SessionElapsed, Medium, H * 0.022, InkMuted, main.X, y, leftWidth, H * 0.031);
            y = TopLine(dc, f.LicenceWarning, Regular, H * 0.020, f.LicenceWarningIsUrgent ? Amber : InkFaint, main.X, y, leftWidth, H * 0.028);
            y = TopLine(dc, f.UpdateNotice, Regular, H * 0.020, Amber, main.X, y, leftWidth, H * 0.028);
            return Math.Max(y, main.Y + (H * 0.075)) + (H * 0.008);
        }

        private double TopLine(DrawingContext dc, string text, Typeface face, double size, Color colour, double x, double y, double width, double height)
        {
            if (string.IsNullOrEmpty(text)) return y;
            dc.DrawText(Fit(text, face, size, width, height, colour), new Point(x, y));
            return y + height;
        }

        /// <summary>
        /// From the bottom up: footer, progress bar, key hints, next-up and round lines. Space is
        /// kept for the bar and the hints even when they are not showing, so the countdown never
        /// jumps when they come and go. Returns the y above them.
        /// </summary>
        private double DrawBottom(DrawingContext dc, DisplayFrame f, Rect main, double H, Color background, bool roundLines, bool progress)
        {
            double y = main.Bottom;

            double footerHeight = H * 0.018 * 1.45;
            DrawFooter(dc, f, new Rect(main.X, y - footerHeight, main.Width, footerHeight), H);
            y -= footerHeight + (H * 0.017);

            if (progress)
            {
                double barHeight = Math.Max(3, H * 0.0195);
                if (f.ShowProgress)
                    DrawBar(dc, new Rect(main.X, y - barHeight, main.Width, barHeight), f.ProgressFraction, background);
                y -= barHeight + (H * 0.017);
            }

            double hintHeight = H * 0.022 * 1.45;
            if (!string.IsNullOrEmpty(f.Hint))
                DrawCentred(dc, Fit(f.Hint, Regular, H * 0.022, main.Width, hintHeight, InkFaint), new Rect(main.X, y - hintHeight, main.Width, hintHeight));
            y -= hintHeight + (H * 0.008);

            if (roundLines)
            {
                double nextHeight = H * 0.025 * 1.4;
                if (!string.IsNullOrEmpty(f.NextUp))
                    DrawCentred(dc, Fit(f.NextUp, Medium, H * 0.025, main.Width, nextHeight, InkSoft), new Rect(main.X, y - nextHeight, main.Width, nextHeight));
                y -= nextHeight;

                double roundHeight = H * 0.033 * 1.4;
                if (!string.IsNullOrEmpty(f.RoundLine))
                    DrawCentred(dc, Fit(f.RoundLine, Bold, H * 0.033, main.Width, roundHeight, Ink), new Rect(main.X, y - roundHeight, main.Width, roundHeight));
                y -= roundHeight + (H * 0.012);
            }
            return y;
        }

        /// <summary>The middle of the screen: station or announcement, the phase word, the countdown and the tip.</summary>
        private void DrawCentreStack(DrawingContext dc, DisplayFrame f, Rect r, double H, string layout)
        {
            double secondary = 0.042, phase = 0.086, big = 0.367, tip = 0.039;
            if (layout == BigNumberLayout) { secondary = 0.045; phase = 0.11; big = 0.55; tip = 0.045; }
            else if (layout == StationsLayout) { secondary = 0.036; phase = 0.07; big = 0.28; tip = 0.034; }

            // Fixed slots, so the countdown stays put when a line above or below it comes and goes.
            double secondaryHeight = H * secondary * 1.35;
            double phaseHeight = H * phase * 1.25;
            double bigHeight = H * big * 0.80;
            double tipHeight = H * tip * 1.4;
            double gap = H * 0.008;
            double total = secondaryHeight + phaseHeight + bigHeight + tipHeight + (3 * gap);
            double k = Math.Min(1, r.Height / total);
            double y = r.Y + ((r.Height - (total * k)) / 2);
            double width = r.Width;

            Rect slot = new Rect(r.X, y, width, secondaryHeight * k);
            if (!string.IsNullOrEmpty(f.Secondary))
                DrawCentred(dc, Fit(f.Secondary, Bold, H * secondary * k, width * 0.96, slot.Height, InkSoft), slot);
            y += (secondaryHeight + gap) * k;

            slot = new Rect(r.X, y, width, phaseHeight * k);
            if (!string.IsNullOrEmpty(f.PhaseLabel))
                DrawCentred(dc, Fit(f.PhaseLabel, ExtraBold, H * phase * k, width * 0.96, slot.Height, Ink), slot);
            y += (phaseHeight + gap) * k;

            slot = new Rect(r.X, y, width, bigHeight * k);
            if (!string.IsNullOrEmpty(f.BigNumber))
            {
                FormattedText number = FitInk(f.BigNumber, Number, H * big * k / 0.74, width * 0.96, slot.Height, Ink);
                DrawInkCentred(dc, number, slot, PulseScale(f.FinalSeconds, f.RemainingSeconds));
            }
            y += (bigHeight + gap) * k;

            slot = new Rect(r.X, y, width, tipHeight * k);
            if (!string.IsNullOrEmpty(f.Tip))
                DrawCentred(dc, Fit(f.Tip, SemiBold, H * tip * k, width * 0.96, slot.Height, InkSoft), slot);
        }

        // ================================================================ station panel

        private void DrawStationPanel(DrawingContext dc, DisplayFrame f, Rect panel, double H, Color background, bool wide)
        {
            Color panelColour = Blend(background, Colors.Black, 0.26);
            Color cardColour = Blend(background, Colors.Black, 0.40);
            Color darkInk = Blend(background, Colors.Black, 0.45);
            dc.DrawRectangle(Fill(panelColour), null, panel);

            double padTop = H * 0.033, padSide = H * 0.025;
            Rect inner = new Rect(panel.X + padSide, panel.Y + padTop, Math.Max(10, panel.Width - (2 * padSide)), Math.Max(10, panel.Height - (2 * padTop)));

            double nameSize = H * (wide ? 0.045 : 0.030);
            double noteSize = H * (wide ? 0.030 : 0.021);
            double padV = H * 0.019, padH = H * 0.022, gap = H * 0.017, radius = H * 0.019;

            // Natural height of every card, then one scale for all of them so they always fit. When
            // that would make the text too small to read across a gym, the work and rest notes go
            // onto one line, and if even that is too small, only the names are shown.
            int detail = 0;
            double k = 1;
            for (detail = 0; detail <= 2; detail++)
            {
                double total = 0;
                foreach (StationRow row in f.Stations) total += CardHeight(row, nameSize, noteSize, padV, detail);
                total += gap * Math.Max(0, f.Stations.Count - 1);
                k = Math.Min(1, inner.Height / Math.Max(1, total));
                if (k >= 0.7 || detail == 2) break;
            }

            double y = inner.Y;
            foreach (StationRow row in f.Stations)
            {
                double cardHeight = CardHeight(row, nameSize, noteSize, padV, detail) * k;
                Rect card = new Rect(inner.X, y, inner.Width, cardHeight);

                Color fill, ink;
                Pen outline = null;
                if (!row.Colour.IsEmpty && row.Colour.A > 0)
                {
                    fill = ToMedia(row.Colour);
                    ink = ToMedia(DisplayModel.ReadableInkFor(row.Colour));
                    if (row.IsCurrent) outline = new Pen(Fill(Colors.White), Math.Max(2, H * 0.004));
                }
                else if (row.IsCurrent)
                {
                    fill = Colors.White;
                    ink = darkInk;
                }
                else
                {
                    fill = cardColour;
                    ink = Ink;
                }

                double r = Math.Min(radius * k, cardHeight / 2);
                dc.DrawRoundedRectangle(Fill(fill), outline, card, r, r);

                double textX = card.X + (padH * k);
                double textWidth = Math.Max(10, card.Width - (2 * padH * k));
                double ty = card.Y + (padV * k);
                double lineName = nameSize * 1.3 * k;
                FormattedText name = FitLine(row.Number + ". " + row.Name, row.IsCurrent ? ExtraBold : Bold,
                    nameSize * k * (row.IsCurrent ? 1.08 : 1.0), textWidth, lineName, ink);
                dc.DrawText(name, new Point(textX, ty));
                ty += lineName;

                Color noteInk = Color.FromArgb(225, ink.R, ink.G, ink.B);
                double lineNote = noteSize * 1.35 * k;
                if (detail == 0)
                {
                    if (row.WorkLine.Length > 0)
                    {
                        dc.DrawText(FitLine(row.WorkLine, row.IsCurrent ? SemiBold : Regular, noteSize * k, textWidth, lineNote, noteInk), new Point(textX, ty));
                        ty += lineNote;
                    }
                    if (row.RestLine.Length > 0)
                        dc.DrawText(FitLine(row.RestLine, Regular, noteSize * k, textWidth, lineNote, noteInk), new Point(textX, ty));
                }
                else if (detail == 1 && (row.WorkLine.Length > 0 || row.RestLine.Length > 0))
                {
                    string both = row.WorkLine.Length > 0 && row.RestLine.Length > 0 ? row.WorkLine + "   " + row.RestLine : row.WorkLine + row.RestLine;
                    dc.DrawText(FitLine(both, Regular, noteSize * k, textWidth, lineNote, noteInk), new Point(textX, ty));
                }

                y += cardHeight + (gap * k);
            }
        }

        /// <summary>A card's height: both note lines (detail 0), one combined line (1) or the name only (2).</summary>
        private static double CardHeight(StationRow row, double nameSize, double noteSize, double padV, int detail)
        {
            int notes = (row.WorkLine.Length > 0 ? 1 : 0) + (row.RestLine.Length > 0 ? 1 : 0);
            if (detail == 1) notes = Math.Min(1, notes);
            if (detail == 2) notes = 0;
            return (2 * padV) + (nameSize * 1.3) + (notes * noteSize * 1.35);
        }

        // ================================================================ parallel

        private Rect DrawParallel(DrawingContext dc, DisplayFrame f, Rect area, double H)
        {
            Rect main = new Rect(area.X + (H * 0.044), area.Y + (H * 0.031),
                Math.Max(10, area.Width - (2 * H * 0.044)), Math.Max(10, area.Height - (H * 0.031) - (H * 0.019)));

            double y = DrawTop(dc, f, main, H);

            double headerHeight = H * 0.042 * 1.35;
            if (!string.IsNullOrEmpty(f.RotationHeader))
                DrawCentred(dc, Fit(f.RotationHeader, ExtraBold, H * 0.042, main.Width, headerHeight, Ink), new Rect(main.X, y, main.Width, headerHeight));
            y += headerHeight + (H * 0.012);

            double bottom = DrawBottom(dc, f, main, H, Colors.Black, false, false);
            Rect tilesArea = new Rect(main.X, y, main.Width, Math.Max(10, bottom - y));

            int n = f.Tiles.Count;
            if (n == 0) return main;

            // The old dashboard's rule: more columns than rows, since projectors are wide.
            double aspect = tilesArea.Width / Math.Max(1, tilesArea.Height);
            int columns = (int)Math.Ceiling(Math.Sqrt(n * Math.Max(0.3, aspect)));
            columns = Math.Max(1, Math.Min(columns, n));
            int rows = (int)Math.Ceiling(n / (double)columns);

            double gap = H * 0.022;
            double tileWidth = (tilesArea.Width - (gap * (columns - 1))) / columns;
            double tileHeight = (tilesArea.Height - (gap * (rows - 1))) / rows;

            for (int i = 0; i < n; i++)
            {
                int col = i % columns;
                int row = i / columns;
                Rect tile = new Rect(tilesArea.X + (col * (tileWidth + gap)), tilesArea.Y + (row * (tileHeight + gap)),
                    Math.Max(10, tileWidth), Math.Max(10, tileHeight));
                DrawTile(dc, f.Tiles[i], tile, H);
            }
            return main;
        }

        private void DrawTile(DrawingContext dc, TileFrame t, Rect tile, double H)
        {
            Color background = ReduceMotion ? ToMedia(t.Background) : ToMedia(t.BaseBackground);
            double radius = Math.Min(H * 0.028, Math.Min(tile.Width, tile.Height) / 4);
            dc.DrawRoundedRectangle(Fill(background), null, tile, radius, radius);

            bool hasColon = t.Countdown.IndexOf(':') >= 0;
            double pad = H * 0.028, name = H * 0.036, phase = H * 0.047, note = H * 0.026, bar = H * 0.017;
            double countdown = H * (hasColon ? 0.153 : 0.183);

            // Shrink everything together when the tile is short (many stations). The same for every
            // tile, whatever its countdown looks like, so names and words line up across the row.
            double natural = (2 * pad) + (name * 1.3) + (phase * 1.35) + (note * 1.3 * 2) + (H * 0.183 * 0.80) + bar + (H * 0.02);
            double k = Math.Min(1, tile.Height / natural);
            pad *= k; name *= k; phase *= k; note *= k; bar *= k; countdown *= k;

            Rect inner = new Rect(tile.X + pad, tile.Y + pad, Math.Max(10, tile.Width - (2 * pad)), Math.Max(10, tile.Height - (2 * pad)));
            double y = inner.Y;

            // Name, with the station's colour as a dot before it.
            double nameHeight = name * 1.3;
            double nameX = inner.X;
            if (!t.Swatch.IsEmpty && t.Swatch.A > 0)
            {
                double dot = name * 0.7;
                dc.DrawEllipse(Fill(ToMedia(t.Swatch)), new Pen(Fill(Color.FromArgb(200, 255, 255, 255)), Math.Max(1, H * 0.002)),
                    new Point(nameX + (dot / 2), y + (nameHeight / 2)), dot / 2, dot / 2);
                nameX += dot + (pad * 0.4);
            }
            dc.DrawText(FitLine(t.Name, ExtraBold, name, Math.Max(10, inner.Right - nameX), nameHeight, Ink), new Point(nameX, y));
            y += nameHeight;

            double phaseHeight = phase * 1.35;
            DrawCentred(dc, Fit(t.PhaseLabel, ExtraBold, phase, inner.Width, phaseHeight, InkSoft), new Rect(inner.X, y, inner.Width, phaseHeight));
            y += phaseHeight;

            double noteHeight = note * 1.3 * 2;
            if (!string.IsNullOrEmpty(t.Note))
                DrawCentred(dc, Wrapped(t.Note, SemiBold, note, inner.Width, noteHeight, Ink), new Rect(inner.X, y, inner.Width, noteHeight));
            y += noteHeight;

            if (t.Finished || string.IsNullOrEmpty(t.Countdown)) return;

            double barTop = inner.Bottom - bar;
            Rect numberSlot = new Rect(inner.X, y, inner.Width, Math.Max(10, barTop - y - (H * 0.01 * k)));
            FormattedText number = FitInk(t.Countdown, Number, countdown / 0.74, inner.Width, numberSlot.Height, Ink);
            DrawInkCentred(dc, number, numberSlot, PulseScale(t.FinalSeconds, t.RemainingSeconds));

            DrawBar(dc, new Rect(inner.X, barTop, inner.Width, Math.Max(3, bar)), t.ProgressFraction, background);
        }

        // ================================================================ footer, bar, toast

        /// <summary>Three columns: who the copy is licensed to, the link status, the copyright.</summary>
        private void DrawFooter(DrawingContext dc, DisplayFrame f, Rect r, double H)
        {
            double size = H * 0.018;
            double third = r.Width / 3;
            if (!string.IsNullOrEmpty(f.Licensee))
                dc.DrawText(FitLine(f.Licensee, SemiBold, size, third - (H * 0.01), r.Height, InkFaint), new Point(r.X, r.Y));
            if (!string.IsNullOrEmpty(f.LinkBadge))
                DrawCentred(dc, FitLine(f.LinkBadge, SemiBold, size, third + (H * 0.06), r.Height, f.LinkBadgeIsWarning ? Amber : InkMuted),
                    new Rect(r.X + third - (H * 0.03), r.Y, third + (H * 0.06), r.Height));
            if (!string.IsNullOrEmpty(f.Copyright))
            {
                FormattedText c = FitLine(f.Copyright, SemiBold, size, third - (H * 0.01), r.Height, InkFaint);
                dc.DrawText(c, new Point(r.Right - c.WidthIncludingTrailingWhitespace, r.Y));
            }
        }

        /// <summary>A fully rounded bar: the track is the background darkened, the fill white.</summary>
        private static void DrawBar(DrawingContext dc, Rect r, double fraction, Color background)
        {
            double radius = r.Height / 2;
            Color track = background == Colors.Black ? Color.FromArgb(70, 255, 255, 255) : Blend(background, Colors.Black, 0.26);
            dc.DrawRoundedRectangle(Fill(track), null, r, radius, radius);
            double width = r.Width * Math.Max(0, Math.Min(1, fraction));
            if (width >= 1)
            {
                double fillRadius = Math.Min(radius, width / 2);
                dc.DrawRoundedRectangle(Fill(Colors.White), null, new Rect(r.X, r.Y, width, r.Height), fillRadius, fillRadius);
            }
        }

        /// <summary>A short confirmation ("PROGRAM READY: ...") in a rounded card, centred, on top of everything.</summary>
        private void DrawToast(DrawingContext dc, DisplayFrame f, Rect main, double H)
        {
            if (string.IsNullOrEmpty(f.Toast)) return;
            Rect card = new Rect(main.X + (main.Width * 0.10), main.Y + (main.Height * 0.38), main.Width * 0.80, H * 0.13);
            double radius = H * 0.022;
            dc.DrawRoundedRectangle(Fill(Color.FromArgb(232, 15, 20, 26)), new Pen(Fill(Color.FromArgb(235, 76, 195, 138)), Math.Max(2, H * 0.004)),
                card, radius, radius);
            Rect text = new Rect(card.X + (H * 0.03), card.Y, card.Width - (H * 0.06), card.Height);
            DrawCentred(dc, Fit(f.Toast, Bold, H * 0.050, text.Width, text.Height, Color.FromRgb(127, 224, 174)), text);
        }

        // ================================================================ motion

        /// <summary>The background colour, faded over 200 ms from the last one when the phase colour changes.</summary>
        private Color Faded(Color target)
        {
            double now = _time.Elapsed.TotalMilliseconds;
            if (!_hasShown)
            {
                _hasShown = true;
                _fadeFrom = target;
                _fadeTo = target;
                return target;
            }

            if (target != _fadeTo)
            {
                _fadeFrom = Current(now);
                _fadeTo = target;
                _fadeStartMs = now;
            }
            return Current(now);
        }

        private Color Current(double now)
        {
            double t = (now - _fadeStartMs) / FadeMilliseconds;
            if (t >= 1) return _fadeTo;
            if (t <= 0) return _fadeFrom;
            return Lerp(_fadeFrom, _fadeTo, t);
        }

        /// <summary>The countdown's beat in the last three seconds: 1.00 to 1.04 and back, once a second.</summary>
        private double PulseScale(bool finalSeconds, double remaining)
        {
            if (ReduceMotion || !finalSeconds) return 1;
            double intoSecond = 1 - (remaining - Math.Floor(remaining));
            return 1 + (0.04 * Math.Sin(Math.PI * Math.Max(0, Math.Min(1, intoSecond))));
        }

        // ================================================================ text

        private double PixelsPerDip
        {
            get
            {
                try { return VisualTreeHelper.GetDpi(this).PixelsPerDip; }
                catch { return 1; }
            }
        }

        private FormattedText Make(string text, Typeface face, double size, Color colour)
        {
            FormattedText ft = new FormattedText(text ?? string.Empty, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                face, Math.Max(1, size), Fill(colour), PixelsPerDip);
            return ft;
        }

        /// <summary>One line, as large as possible up to maxSize, fitting width and height (layout box).</summary>
        private FormattedText Fit(string text, Typeface face, double maxSize, double width, double height, Color colour)
        {
            return Cached("F", text, face, maxSize, width, height, colour, () => Search(text, face, maxSize, colour,
                ft => ft.WidthIncludingTrailingWhitespace <= width && ft.Height <= height + 0.5));
        }

        /// <summary>
        /// Like <see cref="Fit"/> but the height test uses the ink (the visible glyphs), so the big
        /// digits can fill their slot instead of leaving room for the font's line spacing.
        /// </summary>
        private FormattedText FitInk(string text, Typeface face, double maxSize, double width, double height, Color colour)
        {
            return Cached("I", text, face, maxSize, width, height, colour, () => Search(text, face, maxSize, colour,
                ft => ft.Width <= width && ft.Extent <= height + 0.5));
        }

        /// <summary>One line, shrunk to fit the width down to 70% of the size, then cut with an ellipsis.</summary>
        private FormattedText FitLine(string text, Typeface face, double size, double width, double height, Color colour)
        {
            return Cached("L", text, face, size, width, height, colour, () =>
            {
                double min = size * 0.7;
                FormattedText best = Search(text, face, size, colour,
                    ft => ft.WidthIncludingTrailingWhitespace <= width && ft.Height <= height + 0.5, min);
                if (best.WidthIncludingTrailingWhitespace > width)
                {
                    best.MaxTextWidth = Math.Max(1, width);
                    best.MaxLineCount = 1;
                    best.Trimming = TextTrimming.CharacterEllipsis;
                }
                return best;
            });
        }

        /// <summary>Up to two centred lines (a tile's note), shrunk until they fit, then cut at a word.</summary>
        private FormattedText Wrapped(string text, Typeface face, double size, double width, double height, Color colour)
        {
            return Cached("W", text, face, size, width, height, colour, () =>
            {
                Func<double, FormattedText> make = s =>
                {
                    FormattedText ft = Make(text, face, s, colour);
                    ft.MaxTextWidth = Math.Max(1, width);
                    ft.TextAlignment = TextAlignment.Center;
                    ft.MaxLineCount = 2;
                    ft.Trimming = TextTrimming.WordEllipsis;
                    return ft;
                };
                double lo = Math.Max(7, size * 0.6), hi = Math.Max(lo, size);
                FormattedText best = make(hi);
                if (best.Height <= height + 0.5) return best;
                best = make(lo);
                while (hi - lo > 0.5)
                {
                    double mid = (lo + hi) / 2;
                    FormattedText t = make(mid);
                    if (t.Height <= height + 0.5) { lo = mid; best = t; } else hi = mid;
                }
                return best;
            });
        }

        /// <summary>Binary search (to half a point) for the largest size that passes the test (build guide 5.7).</summary>
        private FormattedText Search(string text, Typeface face, double maxSize, Color colour, Func<FormattedText, bool> fits, double minSize = 7)
        {
            FormattedText best = Make(text, face, maxSize, colour);
            if (fits(best)) return best;
            double lo = Math.Min(minSize, maxSize), hi = maxSize;
            best = Make(text, face, lo, colour);
            while (hi - lo > 0.5)
            {
                double mid = (lo + hi) / 2;
                FormattedText t = Make(text, face, mid, colour);
                if (fits(t)) { lo = mid; best = t; } else hi = mid;
            }
            return best;
        }

        /// <summary>
        /// Most frames repeat the last frame's text (the countdown changes once a second), so each
        /// fitted text is kept by text, face, size, box and colour.
        /// </summary>
        private FormattedText Cached(string kind, string text, Typeface face, double size, double width, double height, Color colour, Func<FormattedText> build)
        {
            string key = kind + "|" + text + "|" + face.Weight.ToOpenTypeWeight() + face.FontFamily.Source + "|"
                + Math.Round(size * 2) + "|" + Math.Round(width) + "|" + Math.Round(height) + "|" + colour;
            FormattedText ft;
            if (_fits.TryGetValue(key, out ft)) return ft;
            if (_fits.Count > 600) _fits.Clear();
            ft = build();
            _fits[key] = ft;
            return ft;
        }

        private static void DrawCentred(DrawingContext dc, FormattedText ft, Rect box)
        {
            double x = box.X + ((box.Width - ft.WidthIncludingTrailingWhitespace) / 2);
            if (ft.MaxTextWidth > 0 && ft.TextAlignment == TextAlignment.Center) x = box.X + ((box.Width - ft.MaxTextWidth) / 2);
            double y = box.Y + ((box.Height - ft.Height) / 2);
            dc.DrawText(ft, new Point(x, y));
        }

        /// <summary>Centres the visible glyphs (not the line box) in the slot, optionally scaled about the centre.</summary>
        private static void DrawInkCentred(DrawingContext dc, FormattedText ft, Rect box, double scale)
        {
            double inkTop = ft.Height + ft.OverhangAfter - ft.Extent;     // from the top of the line box to the top of the ink
            double inkLeft = ft.OverhangLeading;
            double inkWidth = ft.Width - ft.OverhangLeading - ft.OverhangTrailing;
            double x = box.X + ((box.Width - inkWidth) / 2) - inkLeft;
            double y = box.Y + ((box.Height - ft.Extent) / 2) - inkTop;

            bool scaled = Math.Abs(scale - 1) > 0.0005;
            if (scaled) dc.PushTransform(new ScaleTransform(scale, scale, box.X + (box.Width / 2), box.Y + (box.Height / 2)));
            dc.DrawText(ft, new Point(x, y));
            if (scaled) dc.Pop();
        }

        // ================================================================ colour

        private static SolidColorBrush Fill(Color colour)
        {
            SolidColorBrush brush = new SolidColorBrush(colour);
            brush.Freeze();
            return brush;
        }

        private static Color ToMedia(Dc colour)
        {
            if (colour.IsEmpty) return Colors.Black;
            return Color.FromArgb(colour.A, colour.R, colour.G, colour.B);
        }

        private static Color Blend(Color from, Color to, double amount)
        {
            return Lerp(from, to, amount);
        }

        private static Color Lerp(Color a, Color b, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb(255,
                (byte)(a.R + ((b.R - a.R) * t)),
                (byte)(a.G + ((b.G - a.G) * t)),
                (byte)(a.B + ((b.B - a.B) * t)));
        }
    }
}
