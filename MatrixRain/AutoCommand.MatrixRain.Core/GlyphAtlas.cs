using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using FlowDirection = System.Windows.FlowDirection;
using FontFamily = System.Windows.Media.FontFamily;
using Point = System.Windows.Point;

namespace AutoCommand.Matrix
{
    /// <summary>
    /// The rain's type case: the configured font (an installed family, or
    /// the user's own .ttf/.otf file) rasterized once per color role into
    /// small Pbgra32 cell bitmaps, so a frame of rain is nothing but
    /// buffer copies — no text layout on the hot path.
    ///
    /// Monospace fonts (Consolas, Cascadia Mono, a bitmap terminal font…)
    /// look the most Matrix; proportional fonts work, each glyph centered
    /// in its cell.
    /// </summary>
    public sealed class GlyphAtlas
    {
        public string ResolvedFont;      // what actually loaded, for the tray balloon
        public int CellW, CellH;         // rain glyph cell, device px
        public int LabelCellW, LabelCellH; // PID-label cell (smaller), device px

        /// <summary>Rain glyph cells per role, indexed by position in
        /// <see cref="GlyphChars"/>.</summary>
        public byte[][] Trail, Head, Blocked, BlockedHead, Rx;

        /// <summary>Backdrop glyph cells (the steady ambient layer, never
        /// touched by the trail fade).</summary>
        public byte[][] Ambient;

        /// <summary>Falling-glyph cells at the zLift scale — slightly
        /// larger than the grid cell and drawn centered on it, so a
        /// falling stream overlaps its neighbors and pops off the
        /// backdrop.</summary>
        public int LiftCellW, LiftCellH;
        public bool HasLift;

        public byte[][] TrailLift, HeadLift, BlockedLift, BlockedHeadLift, RxLift;

        /// <summary>The same lifted glyphs in translucent black — the
        /// offset shadow that sells the elevation.</summary>
        public byte[][] ShadowLift;

        /// <summary>Bloom: blurred, enlarged copies of the LEAD glyphs —
        /// the glow halo. Only leads ever draw from these.</summary>
        public bool HasBloom;
        public int BloomCellW, BloomCellH;
        public byte[][] HeadBloom, BlockedHeadBloom, RxBloom;

        /// <summary>Digits 0–9 for the column PID labels, per role
        /// (normal / blocked), indexed by digit value.</summary>
        public byte[][] LabelDigits, LabelDigitsBlocked;

        public string GlyphChars;

        /// <summary>The DPI scale the cells were rasterized at — the
        /// renderer rebuilds the atlas when the surface's scale differs.</summary>
        public double BuiltWithScale = 1.0;

        // ------------------------------------------------------------------

        public static GlyphAtlas Build(MatrixRainConfig cfg, double dpiScale)
        {
            var atlas = new GlyphAtlas { BuiltWithScale = dpiScale };
            var typeface = ResolveTypeface(cfg, out atlas.ResolvedFont);

            string chars = CharsetFor(cfg.Charset);
            atlas.GlyphChars = chars;

            double em = cfg.FontSizePx * dpiScale;
            double labelEm = Math.Max(7.0, cfg.FontSizePx * 0.62) * dpiScale;

            // Cell metrics from the widest glyph in the chosen set.
            double maxW = 1;
            foreach (var ch in chars)
                maxW = Math.Max(maxW, Measure(typeface, em, ch.ToString()));
            atlas.CellW = Math.Max(4, (int)Math.Ceiling(maxW) + 2);
            atlas.CellH = Math.Max(6, (int)Math.Ceiling(em * 1.12));   // movie-tight vertical stack

            atlas.LabelCellW = Math.Max(4, (int)Math.Ceiling(Measure(typeface, labelEm, "0")) + 2);
            atlas.LabelCellH = Math.Max(5, (int)Math.Ceiling(labelEm * 1.45));

            var c = cfg.Colors;
            atlas.Trail = chars.Select(ch => RenderGlyph(typeface, em, ch.ToString(), c.Trail, atlas.CellW, atlas.CellH)).ToArray();
            atlas.Head = chars.Select(ch => RenderGlyph(typeface, em, ch.ToString(), c.Head, atlas.CellW, atlas.CellH)).ToArray();
            atlas.Blocked = chars.Select(ch => RenderGlyph(typeface, em, ch.ToString(), c.Blocked, atlas.CellW, atlas.CellH)).ToArray();
            atlas.BlockedHead = chars.Select(ch => RenderGlyph(typeface, em, ch.ToString(), c.BlockedHead, atlas.CellW, atlas.CellH)).ToArray();
            atlas.Rx = chars.Select(ch => RenderGlyph(typeface, em, ch.ToString(), c.Rx, atlas.CellW, atlas.CellH)).ToArray();

            atlas.LabelDigits = Enumerable.Range(0, 10)
                .Select(d => RenderGlyph(typeface, labelEm, d.ToString(), c.Label, atlas.LabelCellW, atlas.LabelCellH)).ToArray();
            atlas.LabelDigitsBlocked = Enumerable.Range(0, 10)
                .Select(d => RenderGlyph(typeface, labelEm, d.ToString(), c.Blocked, atlas.LabelCellW, atlas.LabelCellH)).ToArray();

            // The elevated plane: the same falling glyphs, scaled by zLift
            // (the affine projection of their elevation) plus a translucent
            // black copy for the drop shadow. At 1.0 the lifted cells are
            // identical to the grid cells and the shadow is skipped.
            double lift = Math.Clamp(cfg.ZLift, 1.0, 1.6);
            atlas.HasLift = lift > 1.001;
            atlas.LiftCellW = Math.Max(atlas.CellW, (int)Math.Ceiling(atlas.CellW * lift));
            atlas.LiftCellH = Math.Max(atlas.CellH, (int)Math.Ceiling(atlas.CellH * lift));
            double emLift = em * lift;

            atlas.TrailLift = chars.Select(ch => RenderGlyph(typeface, emLift, ch.ToString(), c.Trail, atlas.LiftCellW, atlas.LiftCellH)).ToArray();
            atlas.HeadLift = chars.Select(ch => RenderGlyph(typeface, emLift, ch.ToString(), c.Head, atlas.LiftCellW, atlas.LiftCellH)).ToArray();
            atlas.BlockedLift = chars.Select(ch => RenderGlyph(typeface, emLift, ch.ToString(), c.Blocked, atlas.LiftCellW, atlas.LiftCellH)).ToArray();
            atlas.BlockedHeadLift = chars.Select(ch => RenderGlyph(typeface, emLift, ch.ToString(), c.BlockedHead, atlas.LiftCellW, atlas.LiftCellH)).ToArray();
            atlas.RxLift = chars.Select(ch => RenderGlyph(typeface, emLift, ch.ToString(), c.Rx, atlas.LiftCellW, atlas.LiftCellH)).ToArray();
            atlas.ShadowLift = chars.Select(ch => RenderGlyph(typeface, emLift, ch.ToString(), "#000000", atlas.LiftCellW, atlas.LiftCellH, alpha: 90)).ToArray();

            // Bloom — only the lead glows: blurred, enlarged copies of the
            // head glyphs, dimmed to a soft halo strength.
            atlas.HasBloom = cfg.Bloom;
            if (cfg.Bloom)
            {
                atlas.BloomCellW = (int)Math.Ceiling(atlas.LiftCellW * 1.3);
                atlas.BloomCellH = (int)Math.Ceiling(atlas.LiftCellH * 1.3);
                atlas.HeadBloom = chars.Select(ch => RenderHalo(typeface, emLift, ch.ToString(), c.Head, atlas.BloomCellW, atlas.BloomCellH)).ToArray();
                atlas.BlockedHeadBloom = chars.Select(ch => RenderHalo(typeface, emLift, ch.ToString(), c.BlockedHead, atlas.BloomCellW, atlas.BloomCellH)).ToArray();
                atlas.RxBloom = chars.Select(ch => RenderHalo(typeface, emLift, ch.ToString(), c.Rx, atlas.BloomCellW, atlas.BloomCellH)).ToArray();
            }

            // The backdrop field, at the same em as the wake glyphs — the
            // lead (a hair larger via zLift) reads same-height at a glance.
            atlas.Ambient = chars.Select(ch => RenderGlyph(typeface, em, ch.ToString(), c.Ambient, atlas.CellW, atlas.CellH)).ToArray();

            return atlas;
        }

        /// <summary>
        /// Font resolution order: the configured font file (its internal
        /// family name is read straight out of the file via GlyphTypeface,
        /// so users never have to guess it), then the configured installed
        /// family, then Consolas — the rain always has a face.
        /// </summary>
        private static Typeface ResolveTypeface(MatrixRainConfig cfg, out string resolved)
        {
            if (!string.IsNullOrWhiteSpace(cfg.FontFile) && File.Exists(cfg.FontFile))
            {
                try
                {
                    var fileUri = new Uri("file:///" + Path.GetFullPath(cfg.FontFile).Replace('\\', '/'));
                    var gt = new GlyphTypeface(fileUri);
                    string name = null;
                    if (gt.FamilyNames != null)
                    {
                        if (!gt.FamilyNames.TryGetValue(CultureInfo.GetCultureInfo("en-us"), out name))
                            name = gt.FamilyNames.Values.FirstOrDefault();
                    }
                    name ??= Path.GetFileNameWithoutExtension(cfg.FontFile);

                    // FontFamily resolves a folder of fonts plus a family
                    // name — the documented loose-font-file form.
                    var folderUri = new Uri(fileUri, ".");
                    var ff = new FontFamily(folderUri, "./#" + name);
                    var tf = new Typeface(ff, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
                    if (tf.TryGetGlyphTypeface(out _))
                    {
                        resolved = $"{name} ({Path.GetFileName(cfg.FontFile)})";
                        return tf;
                    }
                }
                catch { /* unreadable font file — fall through to installed */ }
            }

            var family = new FontFamily(cfg.FontFamily);
            var face = new Typeface(family, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            if (!face.TryGetGlyphTypeface(out _))
            {
                family = new FontFamily("Consolas");
                face = new Typeface(family, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
                resolved = $"Consolas (fallback — '{cfg.FontFamily}' not installed)";
            }
            else
            {
                resolved = family.Source + " (installed)";
            }
            return face;
        }

        private static double Measure(Typeface tf, double em, string text)
        {
            var ft = MakeText(tf, em, text, Colors.White);
            return ft.Width;
        }

        private static FormattedText MakeText(Typeface tf, double em, string text, Color color)
        {
            return new FormattedText(
                text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                tf, em, new SolidColorBrush(color), 1.0);
        }

        /// <summary>Renders one glyph centered in a transparent Pbgra32 cell
        /// and copies it out to a raw buffer the frame loop can blit. Alpha
        /// below 255 premultiplies the color (the shadow set uses this).</summary>
        private static byte[] RenderGlyph(Typeface tf, double em, string text, string hexColor, int w, int h, byte alpha = 255)
        {
            var opaque = (Color)ColorConverter.ConvertFromString(hexColor);
            var color = alpha >= 255 ? opaque : Color.FromArgb(alpha, opaque.R, opaque.G, opaque.B);
            var ft = MakeText(tf, em, text, color);

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
                dc.DrawText(ft, new Point((w - ft.Width) / 2.0, (h - ft.Height) / 2.0));

            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();

            var buf = new byte[w * h * 4];
            rtb.CopyPixels(buf, w * 4, 0);
            return buf;
        }

        /// <summary>Soft glow copy of a glyph: rendered slightly larger,
         /// blurred (separable box passes over the premultiplied bytes —
         /// the correct way to filter Pbgra32), then dimmed to halo
         /// strength.</summary>
        private static byte[] RenderHalo(Typeface tf, double em, string text, string hexColor, int w, int h)
        {
            var color = (Color)ColorConverter.ConvertFromString(hexColor);
            var ft = MakeText(tf, em * 1.1, text, color);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
                dc.DrawText(ft, new Point((w - ft.Width) / 2.0, (h - ft.Height) / 2.0));
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();
            var buf = new byte[w * h * 4];
            rtb.CopyPixels(buf, w * 4, 0);

            BoxBlur(buf, w, h, 2);
            BoxBlur(buf, w, h, 2);
            BoxBlur(buf, w, h, 2);

            for (int i = 0; i < buf.Length; i += 4)
            {
                buf[i] = (byte)(buf[i] * 2 / 8);       // B
                buf[i + 1] = (byte)(buf[i + 1] * 2 / 8); // G
                buf[i + 2] = (byte)(buf[i + 2] * 2 / 8); // R
                buf[i + 3] = (byte)(buf[i + 3] * 4 / 8); // A
            }
            return buf;
        }

        /// <summary>In-place separable box blur over premultiplied Pbgra32
        /// bytes (build-time only — a few dozen small cells).</summary>
        private static void BoxBlur(byte[] px, int w, int h, int r)
        {
            int stride = w * 4;
            var tmp = new byte[px.Length];

            for (int y = 0; y < h; y++)
            {
                int row = y * stride;
                for (int x = 0; x < w; x++)
                {
                    int s0 = Math.Max(0, x - r), s1 = Math.Min(w - 1, x + r);
                    int b0 = 0, b1 = 0, b2 = 0, b3 = 0, n = 0;
                    for (int s = s0; s <= s1; s++)
                    {
                        int p = row + s * 4;
                        b0 += px[p]; b1 += px[p + 1]; b2 += px[p + 2]; b3 += px[p + 3]; n++;
                    }
                    int t = row + x * 4;
                    tmp[t] = (byte)(b0 / n); tmp[t + 1] = (byte)(b1 / n);
                    tmp[t + 2] = (byte)(b2 / n); tmp[t + 3] = (byte)(b3 / n);
                }
            }
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    int s0 = Math.Max(0, y - r), s1 = Math.Min(h - 1, y + r);
                    int b0 = 0, b1 = 0, b2 = 0, b3 = 0, n = 0;
                    for (int s = s0; s <= s1; s++)
                    {
                        int p = s * stride + x * 4;
                        b0 += tmp[p]; b1 += tmp[p + 1]; b2 += tmp[p + 2]; b3 += tmp[p + 3]; n++;
                    }
                    int t = y * stride + x * 4;
                    px[t] = (byte)(b0 / n); px[t + 1] = (byte)(b1 / n);
                    px[t + 2] = (byte)(b2 / n); px[t + 3] = (byte)(b3 / n);
                }
            }
        }

        /// <summary>The falling alphabet. The default is katakana plus a
        /// few machine symbols — no roman letters, no digits: the field
        /// should read as something not made for human eyes. Named sets or
        /// a literal string of characters (≥4, no spaces) for a custom
        /// alphabet.</summary>
        private static string CharsetFor(string spec)
        {
            const string latin = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
            const string digits = "0123456789";
            const string symbols = "*+-/<>=|";
            // Half-width katakana — the movie's glyph set.
            string katakana = new string(Enumerable.Range(0xFF66, 0xFF9D - 0xFF66 + 1).Select(i => (char)i).ToArray());

            switch ((spec ?? "").Trim().ToLowerInvariant())
            {
                case "katakana-latin": return Dedupe(katakana + latin + digits + symbols);
                case "latin": return Dedupe(latin + digits + symbols);
                case "digits": return digits;
                case "":
                case "katakana":
                default:
                    string trimmed = (spec ?? "").Trim();
                    if (trimmed.Length >= 4 && !trimmed.Contains(' ') &&
                        trimmed.ToLowerInvariant() != "katakana-latin")
                        return Dedupe(trimmed);
                    return Dedupe(katakana + symbols);   // the alien default
            }
        }

        private static string Dedupe(string s)
        {
            var seen = new HashSet<char>();
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (var ch in s)
                if (seen.Add(ch)) sb.Append(ch);
            return sb.ToString();
        }
    }
}
