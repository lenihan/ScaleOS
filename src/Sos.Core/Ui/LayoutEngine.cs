using Sos.Core.Render;
using Sos.Protocol;

namespace Sos.Core.Ui;

public enum HitKind { Click, Select, Focus }

/// <summary>A clickable region produced by layout: a button (Click), a list item (Select) or an input (Focus).</summary>
public sealed record Hit(Rect Rect, HitKind Kind, string Target, int Index = -1);

/// <summary>Runtime state that affects how a live app draws (typed-but-unsent input text, which input has focus).</summary>
public sealed class LayoutContext
{
    public static readonly LayoutContext Frozen = new();

    public string? FocusedInput { get; init; }
    public IReadOnlyDictionary<string, string>? InputValues { get; init; }
}

public sealed class LayoutResult(int width)
{
    public Canvas Canvas { get; } = new(width);
    public List<Hit> Hits { get; } = [];
    public (int X, int Y)? Cursor { get; set; }
}

/// <summary>
/// Lays out an app's widget tree into a canvas of a given width. Height is whatever the content needs,
/// because the timeline scrolls; apps never get a fixed-size window.
/// </summary>
public static class LayoutEngine
{
    const int MaxDepth = 64;
    const int RowGap = 1;

    public static LayoutResult Render(Widget root, int width, LayoutContext context)
    {
        var result = new LayoutResult(width);
        int height = Draw(root, result, context, 0, 0, width, 0);
        result.Canvas.EnsureHeight(height);
        return result;
    }

    /// <summary>Returns the first input widget's id in the tree, used to give a freshly rendered app keyboard focus.</summary>
    public static string? FirstInputId(Widget? w)
    {
        if (w is null) return null;
        if (IsType(w, "input")) return w.Id ?? "input";
        foreach (var child in w.Children ?? [])
            if (FirstInputId(child) is { } id) return id;
        return null;
    }

    public static bool ContainsInput(Widget? w, string id) =>
        w is not null && ((IsType(w, "input") && (w.Id ?? "input") == id) || (w.Children ?? []).Any(c => ContainsInput(c, id)));

    static bool IsType(Widget w, string type) => string.Equals(w.Type, type, StringComparison.OrdinalIgnoreCase);

    static int Draw(Widget w, LayoutResult r, LayoutContext ctx, int x, int y, int width, int depth)
    {
        if (width <= 0 || depth > MaxDepth) return 0;
        return (w.Type ?? "column").ToLowerInvariant() switch
        {
            "panel" => DrawPanel(w, r, ctx, x, y, width, depth),
            "row" => DrawRow(w, r, ctx, x, y, width, depth),
            "text" => DrawText(w, r, x, y, width),
            "button" => DrawButton(w, r, x, y, width),
            "list" => DrawList(w, r, x, y, width),
            "input" => DrawInput(w, r, ctx, x, y, width),
            "divider" => DrawDivider(r, x, y, width),
            "canvas" => DrawCanvas(w, r, x, y, width),
            _ => DrawColumn(w, r, ctx, x, y, width, depth),
        };
    }

    static int DrawColumn(Widget w, LayoutResult r, LayoutContext ctx, int x, int y, int width, int depth)
    {
        int cy = y;
        foreach (var child in w.Children ?? [])
            cy += Draw(child, r, ctx, x, cy, width, depth + 1);
        return cy - y;
    }

    static int DrawPanel(Widget w, LayoutResult r, LayoutContext ctx, int x, int y, int width, int depth)
    {
        if (width < 4) return DrawColumn(w, r, ctx, x, y, width, depth);

        var c = r.Canvas;
        var border = new Style(Fg: Color(w.Fg, Theme.Border));
        int innerHeight = Math.Max(1, DrawColumn(w, r, ctx, x + 2, y + 1, width - 4, depth));
        int height = innerHeight + 2;

        c.Put(x, y, '╭', border);
        c.Fill(x + 1, y, width - 2, 1, '─', border);
        c.Put(x + width - 1, y, '╮', border);
        if (!string.IsNullOrEmpty(w.Title))
            c.Write(x + 2, y, $" {w.Title} ", new Style(Fg: Theme.Accent, Bold: true), width - 4);

        for (int row = y + 1; row < y + height - 1; row++)
        {
            c.Put(x, row, '│', border);
            c.Put(x + width - 1, row, '│', border);
        }

        int bottom = y + height - 1;
        c.Put(x, bottom, '╰', border);
        c.Fill(x + 1, bottom, width - 2, 1, '─', border);
        c.Put(x + width - 1, bottom, '╯', border);
        return height;
    }

    /// <summary>
    /// Lays children out side by side. Buttons and single-line text keep their natural width;
    /// everything else shares the remaining space equally.
    /// </summary>
    static int DrawRow(Widget w, LayoutResult r, LayoutContext ctx, int x, int y, int width, int depth)
    {
        var kids = w.Children ?? [];
        if (kids.Count == 0) return 0;

        int available = width - RowGap * (kids.Count - 1);
        var natural = kids.Select(NaturalWidth).ToArray();
        int fixedTotal = natural.Sum(n => n ?? 0);
        int flexCount = natural.Count(n => n is null);

        int[] widths;
        if (fixedTotal + flexCount * 4 <= available)
        {
            int flex = flexCount > 0 ? (available - fixedTotal) / flexCount : 0;
            widths = natural.Select(n => n ?? flex).ToArray();
        }
        else
        {
            int each = Math.Max(0, available / kids.Count);
            widths = kids.Select(_ => each).ToArray();
        }

        int cx = x, height = 0;
        for (int i = 0; i < kids.Count; i++)
        {
            height = Math.Max(height, Draw(kids[i], r, ctx, cx, y, widths[i], depth + 1));
            cx += widths[i] + RowGap;
        }
        return height;
    }

    static int? NaturalWidth(Widget w) => (w.Type ?? "").ToLowerInvariant() switch
    {
        "button" => ButtonLabel(w).Length,
        "text" when w.Text is { } t && !t.Contains('\n') => t.Length,
        _ => null,
    };

    static int DrawText(Widget w, LayoutResult r, int x, int y, int width)
    {
        var style = new Style(
            Fg: Color(w.Fg, w.Dim == true ? Theme.Muted : 0),
            Bg: Color(w.Bg, 0),
            Bold: w.Bold == true);
        var lines = TextWrap.Wrap(w.Text ?? "", width);
        for (int i = 0; i < lines.Count; i++)
        {
            if (Style.IsSet(style.Bg)) r.Canvas.Fill(x, y + i, width, 1, ' ', style);
            r.Canvas.Write(x, y + i, lines[i], style, width);
        }
        r.Canvas.EnsureHeight(y + lines.Count);
        return lines.Count;
    }

    static string ButtonLabel(Widget w) => $"[ {w.Text ?? w.Id ?? "?"} ]";

    static int DrawButton(Widget w, LayoutResult r, int x, int y, int width)
    {
        var label = TextWrap.Truncate(ButtonLabel(w), width);
        var style = new Style(Fg: Color(w.Fg, Style.Rgb(230, 233, 240)), Bg: Color(w.Bg, Theme.ButtonBg), Bold: true);
        r.Canvas.Write(x, y, label, style, width);
        r.Hits.Add(new Hit(new Rect(x, y, label.Length, 1), HitKind.Click, w.Id ?? w.Text ?? ""));
        return 1;
    }

    static int DrawList(Widget w, LayoutResult r, int x, int y, int width)
    {
        var items = w.Items ?? [];
        if (items.Count == 0)
        {
            r.Canvas.Write(x, y, TextWrap.Truncate(w.Placeholder ?? "(empty)", width), new Style(Fg: Theme.Muted), width);
            return 1;
        }

        int selected = w.Selected ?? -1;
        var id = w.Id ?? "list";
        for (int i = 0; i < items.Count; i++)
        {
            bool isSelected = i == selected;
            var style = isSelected
                ? new Style(Fg: Color(w.Fg, 0), Bg: Theme.SelectedBg, Bold: true)
                : new Style(Fg: Color(w.Fg, 0));
            if (isSelected) r.Canvas.Fill(x, y + i, width, 1, ' ', style);
            r.Canvas.Write(x, y + i, (isSelected ? "▸ " : "  ") + TextWrap.Truncate(items[i] ?? "", width - 2), style, width);
            r.Hits.Add(new Hit(new Rect(x, y + i, width, 1), HitKind.Select, id, i));
        }
        return items.Count;
    }

    static int DrawInput(Widget w, LayoutResult r, LayoutContext ctx, int x, int y, int width)
    {
        var id = w.Id ?? "input";
        string? typed = null;
        ctx.InputValues?.TryGetValue(id, out typed);
        var value = typed ?? w.Value ?? "";
        bool focused = ctx.FocusedInput == id;

        var field = new Style(Bg: Theme.InputBg);
        r.Canvas.Fill(x, y, width, 1, ' ', field);
        r.Canvas.Write(x, y, "› ", field with { Fg = focused ? Theme.Accent : Theme.Muted, Bold = true }, width);

        int available = width - 3; // prefix plus a cell for the cursor
        if (available > 0)
        {
            string shown = value.Length > available ? value[^available..] : value;
            if (shown.Length == 0 && !string.IsNullOrEmpty(w.Placeholder))
                r.Canvas.Write(x + 2, y, TextWrap.Truncate(w.Placeholder, available + 1), field with { Fg = Theme.Muted }, available + 1);
            else
                r.Canvas.Write(x + 2, y, shown, field, available);
            if (focused) r.Cursor = (x + 2 + shown.Length, y);
        }

        r.Hits.Add(new Hit(new Rect(x, y, width, 1), HitKind.Focus, id));
        return 1;
    }

    static int DrawDivider(LayoutResult r, int x, int y, int width)
    {
        r.Canvas.Fill(x, y, width, 1, '─', new Style(Fg: Theme.Border));
        return 1;
    }

    const int MaxCanvasSize = 1000;
    static readonly int White = Style.Rgb(230, 233, 240);

    /// <summary>
    /// Rasterizes rectangles and sprites into a pixel buffer, then draws two pixels per cell with half blocks:
    /// '▀' with the top pixel as foreground and the bottom pixel as background. Works in any truecolor terminal,
    /// diffs like text, and is stored in history like any other widget.
    /// </summary>
    static int DrawCanvas(Widget w, LayoutResult r, int x, int y, int width)
    {
        int pw = Math.Clamp(w.Width ?? width, 1, MaxCanvasSize);
        int ph = Math.Clamp(w.Height ?? 20, 1, MaxCanvasSize);
        int bg = Style.Hex(w.Bg);
        var pixels = new int[pw * ph];
        Array.Fill(pixels, bg);

        void Plot(int px, int py, int color)
        {
            if (px >= 0 && py >= 0 && px < pw && py < ph) pixels[py * pw + px] = color;
        }

        foreach (var rect in w.Rects ?? [])
        {
            int color = Color(rect.Color, White);
            int rx = (int)Math.Round(rect.X), ry = (int)Math.Round(rect.Y);
            int rw = (int)Math.Round(rect.W), rh = (int)Math.Round(rect.H);
            for (int py = Math.Max(0, ry); py < Math.Min(ph, ry + rh); py++)
                for (int px = Math.Max(0, rx); px < Math.Min(pw, rx + rw); px++)
                    pixels[py * pw + px] = color;
        }

        foreach (var draw in w.Draw ?? [])
        {
            if (draw.S is null || w.Sprites is null || !w.Sprites.TryGetValue(draw.S, out var sprite) || sprite.Rows is null) continue;
            int color = Color(draw.Color, Color(sprite.Color, White));
            int ox = (int)Math.Round(draw.X), oy = (int)Math.Round(draw.Y);
            for (int row = 0; row < sprite.Rows.Count; row++)
            {
                var line = sprite.Rows[row] ?? "";
                for (int col = 0; col < line.Length; col++)
                {
                    char ch = line[col];
                    if (ch is '.' or ' ') continue;
                    int c = sprite.Palette is not null && sprite.Palette.TryGetValue(ch.ToString(), out var hex) ? Color(hex, color) : color;
                    Plot(ox + col, oy + row, c);
                }
            }
        }

        int cols = Math.Min(pw, width);
        int rows = (ph + 1) / 2;
        for (int cy = 0; cy < rows; cy++)
        {
            for (int cx = 0; cx < cols; cx++)
            {
                int top = pixels[2 * cy * pw + cx];
                int bottom = 2 * cy + 1 < ph ? pixels[(2 * cy + 1) * pw + cx] : bg;
                if (top == bottom) r.Canvas.Put(x + cx, y + cy, ' ', new Style(Bg: top));
                else if (top == 0) r.Canvas.Put(x + cx, y + cy, '▄', new Style(Fg: bottom));
                else r.Canvas.Put(x + cx, y + cy, '▀', new Style(Fg: top, Bg: bottom));
            }
        }
        r.Canvas.EnsureHeight(y + rows);
        return rows;
    }

    static int Color(string? hex, int fallback) => Style.Hex(hex) is var c && c != 0 ? c : fallback;
}
