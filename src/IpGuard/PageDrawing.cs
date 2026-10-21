using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace IpGuard;

public sealed class PageFonts : IDisposable
{
    public PageFonts(float scale)
    {
        Scale = scale;
        Title = new Font("Segoe UI Semibold", 15f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        Label = new Font("Segoe UI", 14f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        Small = new Font("Segoe UI", 12f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        Section = new Font("Segoe UI Semibold", 11f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        Symbol = new Font("Segoe UI Symbol", 14f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
    }

    public float Scale { get; }

    public Font Title { get; }

    public Font Label { get; }

    public Font Small { get; }

    public Font Section { get; }

    public Font Symbol { get; }

    public void Dispose()
    {
        Title.Dispose();
        Label.Dispose();
        Small.Dispose();
        Section.Dispose();
        Symbol.Dispose();
    }
}

public static class PageDrawing
{
    private static readonly Color Ink = Color.FromArgb(238, 240, 246);
    private static readonly Color Dim = Color.FromArgb(160, 170, 190);

    public static void Render(Graphics g, Page page, PageHost host, Color accent, bool caretOn, float sx, float sy, PageFonts fonts)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        var frame = host.Frame;
        var area = frame.Area;
        DrawHeader(g, page, host, sx, sy, fonts);
        var vp = frame.Viewport(page);
        var viewport = new RectangleF(0, vp.Y - area.Y, area.Width * sx, vp.Height * sy);
        viewport = new RectangleF(0, (vp.Y - area.Y) * sy, area.Width * sx, vp.Height * sy);
        var state = g.Save();
        g.SetClip(viewport);
        var rows = page.Rows;
        for (var i = 0; i < rows.Rows.Count; i++)
        {
            var top = (vp.Y - area.Y) + rows.TopOf(i) - rows.ScrollY;
            var h = rows.HeightOf(i);
            if (top + h < vp.Y - area.Y || top > vp.Y - area.Y + vp.Height)
            {
                continue;
            }

            DrawRow(g, rows.Rows[i], i, rows, top, area.Width, h, accent, caretOn, sx, sy, fonts);
        }

        DrawScrollbar(g, rows, vp.Y - area.Y, vp.Height, area.Width, sx, sy);
        g.Restore(state);
        DrawFooter(g, page, host, accent, sx, sy, fonts);
    }

    public static int CaretIndexAt(string text, float xFromStart, float fontPx = 14f)
    {
        if (text.Length == 0 || xFromStart <= 0)
        {
            return 0;
        }

        using var bitmap = new Bitmap(1, 1);
        using var g = Graphics.FromImage(bitmap);
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var font = new Font("Segoe UI", fontPx, FontStyle.Regular, GraphicsUnit.Pixel);
        var previous = 0f;
        for (var i = 1; i <= text.Length; i++)
        {
            var width = Measure(g, text[..i], font);
            if (xFromStart < (previous + width) / 2)
            {
                return i - 1;
            }

            previous = width;
        }

        return text.Length;
    }

    private static float Measure(Graphics g, string text, Font font)
    {
        if (text.Length == 0)
        {
            return 0f;
        }

        using var format = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap };
        return g.MeasureString(text, font, int.MaxValue, format).Width;
    }

    private static RectangleF R(float x, float y, float w, float h, float sx, float sy) => new(x * sx, y * sy, w * sx, h * sy);

    private static Color A(Color c, int alpha) => Color.FromArgb(Math.Clamp(alpha, 0, 255), c.R, c.G, c.B);

    private static void DrawHeader(Graphics g, Page page, PageHost host, float sx, float sy, PageFonts f)
    {
        var frame = host.Frame;
        var area = frame.Area;
        var titleLeft = 14f;
        if (host.CanGoBack)
        {
            var back = frame.Back;
            var tile = R(back.X - area.X, back.Y - area.Y, back.Width, back.Height, sx, sy);
            using (var path = Rounded(tile, 8 * sx))
            using (var fill = new SolidBrush(A(Color.White, host.BackPressed ? 56 : host.BackHover ? 40 : 18)))
            {
                g.FillPath(fill, path);
            }

            using var pen = new Pen(A(Ink, 230), Math.Max(1.5f, 1.8f * sx)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            var cx = tile.X + tile.Width / 2;
            var cy = tile.Y + tile.Height / 2;
            g.DrawLines(pen, new[] { new PointF(cx + 3 * sx, cy - 7 * sy), new PointF(cx - 3 * sx, cy), new PointF(cx + 3 * sx, cy + 7 * sy) });
            titleLeft = 44f;
        }

        var titleRight = 12f;
        if (host.ShowClose)
        {
            var close = frame.Close;
            var tile = R(close.X - area.X, close.Y - area.Y, close.Width, close.Height, sx, sy);
            using (var path = Rounded(tile, 8 * sx))
            using (var fill = new SolidBrush(A(Color.White, host.ClosePressed ? 56 : host.CloseHover ? 40 : 18)))
            {
                g.FillPath(fill, path);
            }

            using var pen = new Pen(A(Ink, 230), Math.Max(1.5f, 1.7f * sx)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            var cx = tile.X + tile.Width / 2;
            var cy = tile.Y + tile.Height / 2;
            var d = 5f * sx;
            g.DrawLine(pen, cx - d, cy - d, cx + d, cy + d);
            g.DrawLine(pen, cx - d, cy + d, cx + d, cy - d);
            titleRight = 48f;
        }

        using var format = new StringFormat(StringFormatFlags.NoWrap) { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
        using var text = new SolidBrush(Ink);
        g.DrawString(page.Title, f.Title, text, R(titleLeft, 0, area.Width - titleLeft - titleRight, PageGeometry.HeaderHeight, sx, sy), format);
        using var line = new Pen(A(Color.White, 24), Math.Max(1f, sy));
        g.DrawLine(line, 8 * sx, PageGeometry.HeaderHeight * sy, (area.Width - 8) * sx, PageGeometry.HeaderHeight * sy);
    }

    private static void DrawFooter(Graphics g, Page page, PageHost host, Color accent, float sx, float sy, PageFonts f)
    {
        var count = page.Footer.Count;
        if (count == 0)
        {
            return;
        }

        var frame = host.Frame;
        var area = frame.Area;
        using (var line = new Pen(A(Color.White, 24), Math.Max(1f, sy)))
        {
            var y = (area.Height - PageGeometry.FooterHeight) * sy;
            g.DrawLine(line, 8 * sx, y, (area.Width - 8) * sx, y);
        }

        using var format = new StringFormat(StringFormatFlags.NoWrap) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
        for (var i = 0; i < count; i++)
        {
            var button = page.Footer[i];
            var enabled = button.IsEnabled;
            var r = frame.FooterButton(i, count);
            var rect = R(r.X - area.X, r.Y - area.Y, r.Width, r.Height, sx, sy);
            var hover = enabled && host.FooterHover == i;
            var pressed = enabled && host.FooterPressed == i;
            var baseAlpha = button.Primary ? 56 : 20;
            using var path = Rounded(rect, 9 * sx);
            using (var fill = new SolidBrush(A(button.Primary ? accent : Color.White, enabled ? baseAlpha + (hover ? 22 : 0) + (pressed ? 16 : 0) : 8)))
            {
                g.FillPath(fill, path);
            }

            using (var edge = new Pen(A(button.Primary ? accent : Color.White, enabled ? (button.Primary ? 170 : 54) : 24), Math.Max(1f, sx)))
            {
                g.DrawPath(edge, path);
            }

            using var text = new SolidBrush(A(Ink, enabled ? 240 : 100));
            g.DrawString(button.Text, f.Label, text, new RectangleF(rect.X, rect.Y + (pressed ? sy : 0), rect.Width, rect.Height), format);
        }
    }

    private static void DrawScrollbar(Graphics g, RowList rows, float viewportTop, float viewportHeight, float width, float sx, float sy)
    {
        if (rows.MaxScroll <= 0.5f)
        {
            return;
        }

        var ratio = viewportHeight / rows.ContentHeight;
        var thumb = Math.Max(24f, viewportHeight * ratio);
        var travel = viewportHeight - thumb;
        var y = viewportTop + (rows.MaxScroll <= 0 ? 0 : travel * rows.ScrollY / rows.MaxScroll);
        using var path = Rounded(R(width - 6, y + 2, 3, thumb - 4, sx, sy), 1.5f * sx);
        using var brush = new SolidBrush(A(Color.White, 70));
        g.FillPath(brush, path);
    }

    private static void DrawRow(Graphics g, Row row, int index, RowList rows, float top, float width, float height, Color accent, bool caretOn, float sx, float sy, PageFonts f)
    {
        var hover = rows.Hover == index;
        var pressed = rows.Pressed == index;
        var focus = rows.Focus == index;
        var part = hover ? rows.HoverPart : RowPart.None;
        var enabled = row.Enabled;
        switch (row)
        {
            case SectionRow section:
                using (var brush = new SolidBrush(A(Dim, 230)))
                using (var format = new StringFormat(StringFormatFlags.NoWrap) { LineAlignment = StringAlignment.Far })
                {
                    g.DrawString(section.Title.ToUpperInvariant(), f.Section, brush, R(12, top, width - 24, height - 4, sx, sy), format);
                }

                return;
            case NoteRow note:
                using (var brush = new SolidBrush(A(Dim, 220)))
                {
                    g.DrawString(note.Text, f.Small, brush, R(12, top + 3, width - 24, height - 4, sx, sy));
                }

                return;
            case HistoryRow history:
                DrawHistory(g, history.Entry, top, width, height, sx, sy, f);
                return;
            case LineRow line:
                using (var brush = new SolidBrush(line.Level switch
                {
                    LineLevel.Error => Color.FromArgb(240, 130, 130),
                    LineLevel.Alert => A(NebulaRenderer.ToneColor(Tone.Alert), 235),
                    _ => A(Dim, 230)
                }))
                using (var format = new StringFormat(StringFormatFlags.LineLimit) { Trimming = StringTrimming.EllipsisCharacter })
                {
                    g.DrawString(line.Text, f.Small, brush, R(12, top + 2, width - 24, height - 3, sx, sy), format);
                }

                return;
        }

        if (row is not TextRow)
        {
            DrawTile(g, R(4, top, width - 8, height, sx, sy), hover, pressed, focus, enabled, sx);
        }

        var ink = A(Ink, enabled ? 240 : 100);
        var dim = A(Dim, enabled ? 230 : 90);
        using var left = new StringFormat(StringFormatFlags.NoWrap) { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
        using var right = new StringFormat(StringFormatFlags.NoWrap) { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
        using var center = new StringFormat(StringFormatFlags.NoWrap) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
        using var inkBrush = new SolidBrush(ink);
        using var dimBrush = new SolidBrush(dim);
        switch (row)
        {
            case ToggleRow toggle:
            {
                var line = RowMetrics.RowHeight;
                g.DrawString(toggle.Label, f.Label, inkBrush, R(12, top, width - 24 - RowMetrics.SwitchWidth, toggle.Hint is null ? line : 30, sx, sy), left);
                if (toggle.Hint is not null)
                {
                    g.DrawString(toggle.Hint, f.Small, dimBrush, R(12, top + 26, width - 70, 18, sx, sy), left);
                }

                DrawSwitch(g, R(width - 12 - RowMetrics.SwitchWidth, top + (line - RowMetrics.SwitchHeight) / 2, RowMetrics.SwitchWidth, RowMetrics.SwitchHeight, sx, sy), toggle.Value, enabled, accent);
                break;
            }

            case SliderRow slider:
            {
                g.DrawString(slider.Label, f.Label, inkBrush, R(12, top + 2, width - 90, 26, sx, sy), left);
                g.DrawString(slider.Value + "%", f.Label, dimBrush, R(width - 80, top + 2, 68, 26, sx, sy), right);
                DrawSlider(g, slider, top, width, enabled, accent, sx, sy);
                break;
            }

            case ChoiceRow choice when choice.ShowPlay:
            {
                g.DrawString(choice.Label, f.Label, inkBrush, R(12, top + 3, width - 24, 24, sx, sy), left);
                var lineTop = top + RowMetrics.ControlLine + 1;
                var play = R(RowMetrics.Padding, lineTop, RowMetrics.PlayWidth, 26, sx, sy);
                var prev = R(RowMetrics.Padding + RowMetrics.PlayWidth + 6, lineTop, RowMetrics.ChoiceArrow, 26, sx, sy);
                var next = R(width - RowMetrics.Padding - RowMetrics.ChoiceArrow, lineTop, RowMetrics.ChoiceArrow, 26, sx, sy);
                DrawSmallTile(g, play, part == RowPart.Play, enabled, sx);
                DrawTriangle(g, play, true, A(Ink, enabled ? 230 : 90));
                DrawSmallTile(g, prev, part == RowPart.Prev, enabled, sx);
                DrawSmallTile(g, next, part == RowPart.Next, enabled, sx);
                DrawChevron(g, prev, false, A(Ink, enabled ? 230 : 90), sx);
                DrawChevron(g, next, true, A(Ink, enabled ? 230 : 90), sx);
                g.DrawString(choice.Selected, f.Small, inkBrush, new RectangleF(prev.Right, prev.Y, next.X - prev.Right, prev.Height), center);
                break;
            }

            case ChoiceRow choice:
            {
                var controlLeft = width - RowMetrics.Padding - RowMetrics.ChoiceWidth;
                g.DrawString(choice.Label, f.Label, inkBrush, R(12, top, controlLeft - 18, height, sx, sy), left);
                var prev = R(controlLeft, top + 4, RowMetrics.ChoiceArrow, 28, sx, sy);
                var next = R(width - RowMetrics.Padding - RowMetrics.ChoiceArrow, top + 4, RowMetrics.ChoiceArrow, 28, sx, sy);
                DrawSmallTile(g, prev, part == RowPart.Prev, enabled, sx);
                DrawSmallTile(g, next, part == RowPart.Next, enabled, sx);
                DrawChevron(g, prev, false, A(Ink, enabled ? 230 : 90), sx);
                DrawChevron(g, next, true, A(Ink, enabled ? 230 : 90), sx);
                g.DrawString(choice.Selected, f.Small, inkBrush, R(controlLeft + RowMetrics.ChoiceArrow, top, RowMetrics.ChoiceWidth - 2 * RowMetrics.ChoiceArrow, height, sx, sy), center);
                break;
            }

            case StepperRow stepper:
            {
                var controlLeft = width - RowMetrics.Padding - RowMetrics.StepperWidth;
                g.DrawString(stepper.Label, f.Label, inkBrush, R(12, top, controlLeft - 18, height, sx, sy), left);
                var minus = R(controlLeft, top + 4, RowMetrics.StepButton, 28, sx, sy);
                var plus = R(width - RowMetrics.Padding - RowMetrics.StepButton, top + 4, RowMetrics.StepButton, 28, sx, sy);
                DrawSmallTile(g, minus, part == RowPart.Minus, enabled, sx);
                DrawSmallTile(g, plus, part == RowPart.Plus, enabled, sx);
                using (var pen = new Pen(A(Ink, enabled ? 230 : 90), Math.Max(1.5f, 1.6f * sx)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawLine(pen, minus.X + minus.Width * 0.3f, minus.Y + minus.Height / 2, minus.Right - minus.Width * 0.3f, minus.Y + minus.Height / 2);
                    g.DrawLine(pen, plus.X + plus.Width * 0.3f, plus.Y + plus.Height / 2, plus.Right - plus.Width * 0.3f, plus.Y + plus.Height / 2);
                    g.DrawLine(pen, plus.X + plus.Width / 2, plus.Y + plus.Height * 0.3f, plus.X + plus.Width / 2, plus.Bottom - plus.Height * 0.3f);
                }

                var valueRect = R(controlLeft + RowMetrics.StepButton, top, RowMetrics.StepperWidth - 2 * RowMetrics.StepButton, height, sx, sy);
                if (part == RowPart.Value)
                {
                    using var path = Rounded(Rectangle.Round(RectangleF.Inflate(valueRect, -2 * sx, -6 * sy)), 6 * sx);
                    using var fill = new SolidBrush(A(Color.White, 20));
                    g.FillPath(fill, path);
                }

                g.DrawString(stepper.Value + stepper.Unit, f.Label, inkBrush, valueRect, center);
                break;
            }

            case PickerRow picker:
                g.DrawString(picker.Label, f.Label, inkBrush, R(12, top, width * 0.45f, height, sx, sy), left);
                g.DrawString(picker.Value, f.Small, dimBrush, R(width * 0.45f, top, width * 0.55f - 34, height, sx, sy), right);
                DrawChevron(g, R(width - 12 - 14, top + 4, 14, 28, sx, sy), true, A(Dim, enabled ? 230 : 90), sx);
                break;
            case ButtonRow button:
            {
                if (button.Primary && enabled)
                {
                    using var path = Rounded(R(4, top, width - 8, height, sx, sy), 9 * sx);
                    using var edge = new Pen(A(accent, 150), Math.Max(1f, sx));
                    g.DrawPath(edge, path);
                }

                using var brush = new SolidBrush(button.Primary && enabled ? A(accent, 255) : ink);
                g.DrawString(button.Text, f.Label, brush, R(4, top, width - 8, height, sx, sy), center);
                break;
            }

            case ItemRow item:
            {
                var removeLeft = width - RowMetrics.Padding - RowMetrics.IconButton;
                var editRight = item.ShowRemove ? removeLeft - RowMetrics.IconGap : width - RowMetrics.Padding;
                var textRight = (item.ShowEdit ? editRight - RowMetrics.IconButton : editRight) - 8;
                if (item.Marked)
                {
                    using var path = Rounded(R(5, top + 8, 3, height - 16, sx, sy), 1.5f * sx);
                    using var mark = new SolidBrush(A(accent, 230));
                    g.FillPath(mark, path);
                }

                var hasSecondary = item.Secondary.Length > 0;
                g.DrawString(item.Primary, f.Label, inkBrush, R(14, top + (hasSecondary ? 4 : 0), textRight - 14, hasSecondary ? 22 : height, sx, sy), left);
                if (hasSecondary)
                {
                    g.DrawString(item.Secondary, f.Small, dimBrush, R(14, top + 24, textRight - 14, 18, sx, sy), left);
                }

                if (item.ShowEdit)
                {
                    var tile = R(editRight - RowMetrics.IconButton, top + 8, RowMetrics.IconButton, 28, sx, sy);
                    DrawSmallTile(g, tile, part == RowPart.Edit, enabled, sx);
                    g.DrawString("✎", f.Symbol, inkBrush, tile, center);
                }

                if (item.ShowRemove)
                {
                    var tile = R(removeLeft, top + 8, RowMetrics.IconButton, 28, sx, sy);
                    DrawSmallTile(g, tile, part == RowPart.Remove, enabled, sx);
                    using var pen = new Pen(A(Ink, enabled ? 230 : 90), Math.Max(1.5f, 1.6f * sx)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                    var cx = tile.X + tile.Width / 2;
                    var cy = tile.Y + tile.Height / 2;
                    var d = 5f * sx;
                    g.DrawLine(pen, cx - d, cy - d, cx + d, cy + d);
                    g.DrawLine(pen, cx - d, cy + d, cx + d, cy - d);
                }

                break;
            }

            case TextRow input:
                DrawTextField(g, input, top, width, focus, enabled, accent, caretOn, sx, sy, f);
                break;
        }
    }

    private static void DrawHistory(Graphics g, HistoryEntry entry, float top, float width, float height, float sx, float sy, PageFonts f)
    {
        using (var path = Rounded(R(4, top, width - 8, height - 2, sx, sy), 8 * sx))
        using (var fill = new SolidBrush(A(Color.White, 9)))
        {
            g.FillPath(fill, path);
        }

        var tone = NebulaRenderer.ToneColor(PanelState.ToneOf(entry.Status));
        using var left = new StringFormat(StringFormatFlags.NoWrap) { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
        using var ink = new SolidBrush(A(Ink, 240));
        using var dim = new SolidBrush(A(Dim, 225));
        using var toneBrush = new SolidBrush(A(tone, 245));
        g.DrawString(entry.Time.ToString("dd.MM HH:mm:ss"), f.Small, dim, R(12, top + 4, 110, 22, sx, sy), left);
        g.DrawString(entry.Ip, f.Label, ink, R(124, top + 3, 170, 24, sx, sy), left);
        var place = entry.Country.Length > 0 ? $"{entry.Country} ({entry.CountryCode})" : entry.CountryCode;
        g.DrawString(place, f.Small, dim, R(300, top + 4, width - 312, 22, sx, sy), left);
        g.FillEllipse(toneBrush, 14 * sx, (top + 33) * sy, 8 * sx, 8 * sy);
        g.DrawString(Labels.For(entry.Status), f.Small, toneBrush, R(28, top + 27, 140, 20, sx, sy), left);
        var note = string.IsNullOrWhiteSpace(entry.Note) ? "" : "  ·  " + entry.Note.Replace("\n", "; ");
        g.DrawString(entry.Provider + note, f.Small, dim, R(170, top + 27, width - 182, 20, sx, sy), left);
    }

    private static void DrawTextField(Graphics g, TextRow input, float top, float width, bool focus, bool enabled, Color accent, bool caretOn, float sx, float sy, PageFonts f)
    {
        var field = R(10, top + 4, width - 20, 32, sx, sy);
        using (var path = Rounded(field, 8 * sx))
        {
            using (var fill = new SolidBrush(A(Color.Black, 70)))
            {
                g.FillPath(fill, path);
            }

            using var edge = new Pen(A(focus ? accent : Color.White, focus ? 200 : 40), Math.Max(1f, sx));
            g.DrawPath(edge, path);
        }

        var editor = input.Editor;
        var textX = field.X + 9 * sx;
        using var format = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap };
        var textTop = field.Y + (field.Height - f.Label.Size) / 2 - 1 * sy;
        if (editor.Text.Length == 0 && !focus)
        {
            using var placeholder = new SolidBrush(A(Dim, 150));
            g.DrawString(input.Placeholder, f.Label, placeholder, textX, textTop, format);
            return;
        }

        if (focus && editor.HasSelection)
        {
            var from = Measure(g, editor.Text[..editor.SelectionStart], f.Label);
            var to = Measure(g, editor.Text[..(editor.SelectionStart + editor.SelectionLength)], f.Label);
            using var selection = new SolidBrush(A(accent, 90));
            g.FillRectangle(selection, textX + from, field.Y + 6 * sy, Math.Max(1f, to - from), field.Height - 12 * sy);
        }

        using (var brush = new SolidBrush(A(Ink, enabled ? 245 : 100)))
        {
            g.DrawString(editor.Text, f.Label, brush, textX, textTop, format);
        }

        if (focus && caretOn)
        {
            var x = textX + Measure(g, editor.Text[..editor.Caret], f.Label);
            using var pen = new Pen(A(Ink, 240), Math.Max(1f, 1.2f * sx));
            g.DrawLine(pen, x, field.Y + 7 * sy, x, field.Bottom - 7 * sy);
        }
    }

    private static void DrawTile(Graphics g, RectangleF rect, bool hover, bool pressed, bool focus, bool enabled, float sx)
    {
        using var path = Rounded(rect, 9 * sx);
        var alpha = enabled ? 12 + (hover ? 14 : 0) + (pressed ? 10 : 0) : 5;
        using (var fill = new SolidBrush(A(Color.White, alpha)))
        {
            g.FillPath(fill, path);
        }

        using var edge = new Pen(A(Color.White, focus ? 150 : 24), Math.Max(1f, focus ? 1.4f * sx : sx));
        g.DrawPath(edge, path);
    }

    private static void DrawSmallTile(Graphics g, RectangleF rect, bool hover, bool enabled, float sx)
    {
        using var path = Rounded(rect, 7 * sx);
        using var fill = new SolidBrush(A(Color.White, enabled ? (hover ? 52 : 22) : 8));
        g.FillPath(fill, path);
    }

    private static void DrawSwitch(Graphics g, RectangleF rect, bool on, bool enabled, Color accent)
    {
        using var path = Rounded(rect, rect.Height / 2);
        using (var fill = new SolidBrush(on ? A(accent, enabled ? 210 : 90) : A(Color.White, enabled ? 54 : 24)))
        {
            g.FillPath(fill, path);
        }

        var knob = rect.Height - 6f * (rect.Height / RowMetrics.SwitchHeight);
        var pad = (rect.Height - knob) / 2;
        var x = on ? rect.Right - pad - knob : rect.X + pad;
        using var white = new SolidBrush(A(Color.White, enabled ? 245 : 140));
        g.FillEllipse(white, x, rect.Y + pad, knob, knob);
    }

    private static void DrawSlider(Graphics g, SliderRow slider, float top, float width, bool enabled, Color accent, float sx, float sy)
    {
        var x0 = RowMetrics.Padding * sx;
        var x1 = (width - RowMetrics.Padding) * sx;
        var y = (top + 36) * sy;
        var fraction = slider.Max == slider.Min ? 0f : (slider.Value - slider.Min) / (float)(slider.Max - slider.Min);
        var knobX = x0 + (x1 - x0) * fraction;
        using (var track = new SolidBrush(A(Color.White, enabled ? 46 : 20)))
        {
            g.FillRectangle(track, x0, y - 2 * sy, x1 - x0, 4 * sy);
        }

        using (var fill = new SolidBrush(A(accent, enabled ? 220 : 90)))
        {
            g.FillRectangle(fill, x0, y - 2 * sy, Math.Max(0, knobX - x0), 4 * sy);
        }

        using var knob = new SolidBrush(A(Color.White, enabled ? 250 : 140));
        var d = 14f * sx;
        g.FillEllipse(knob, knobX - d / 2, y - d / 2, d, d);
    }

    private static void DrawTriangle(Graphics g, RectangleF rect, bool pointRight, Color color)
    {
        var cx = rect.X + rect.Width / 2;
        var cy = rect.Y + rect.Height / 2;
        var w = rect.Width * 0.18f;
        var h = rect.Height * 0.24f;
        using var brush = new SolidBrush(color);
        g.FillPolygon(brush, pointRight
            ? new[] { new PointF(cx - w, cy - h), new PointF(cx + w * 1.4f, cy), new PointF(cx - w, cy + h) }
            : new[] { new PointF(cx + w, cy - h), new PointF(cx - w * 1.4f, cy), new PointF(cx + w, cy + h) });
    }

    private static void DrawChevron(Graphics g, RectangleF rect, bool right, Color color, float sx)
    {
        var cx = rect.X + rect.Width / 2;
        var cy = rect.Y + rect.Height / 2;
        var d = 3.5f * sx;
        var dir = right ? 1f : -1f;
        using var pen = new Pen(color, Math.Max(1.4f, 1.5f * sx)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawLines(pen, new[] { new PointF(cx - dir * d * 0.6f, cy - d * 1.4f), new PointF(cx + dir * d * 0.6f, cy), new PointF(cx - dir * d * 0.6f, cy + d * 1.4f) });
    }

    private static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var path = new GraphicsPath();
        if (d <= 0.5f)
        {
            path.AddRectangle(r);
            return path;
        }

        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
