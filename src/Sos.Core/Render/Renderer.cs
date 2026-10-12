using System.Text;

namespace Sos.Core.Render;

/// <summary>
/// Turns a <see cref="CellBuffer"/> into VT output, emitting only the cells that changed since the last frame.
/// </summary>
public sealed class Renderer
{
    Cell[]? previous;
    int previousWidth, previousHeight;

    /// <summary>Forces the next frame to redraw every cell.</summary>
    public void Invalidate() => previous = null;

    public string Render(CellBuffer buffer, (int X, int Y)? cursor)
    {
        var sb = new StringBuilder();
        sb.Append("\x1b[?25l");

        bool full = previous is null || previousWidth != buffer.Width || previousHeight != buffer.Height;
        if (full) sb.Append("\x1b[0m\x1b[2J");

        Style? current = null;
        int cx = -1, cy = -1;
        for (int y = 0; y < buffer.Height; y++)
        {
            for (int x = 0; x < buffer.Width; x++)
            {
                int i = y * buffer.Width + x;
                var cell = buffer.Cells[i];
                if (!full && previous![i] == cell) continue;

                if (cx != x || cy != y) sb.Append("\x1b[").Append(y + 1).Append(';').Append(x + 1).Append('H');
                if (current != cell.Style)
                {
                    AppendSgr(sb, cell.Style);
                    current = cell.Style;
                }
                sb.Append(cell.Ch);
                cx = x + 1;
                cy = y;
            }
        }

        sb.Append("\x1b[0m");
        if (cursor is { } c)
            sb.Append("\x1b[").Append(c.Y + 1).Append(';').Append(c.X + 1).Append("H\x1b[?25h");

        previous = (Cell[])buffer.Cells.Clone();
        previousWidth = buffer.Width;
        previousHeight = buffer.Height;
        return sb.ToString();
    }

    static void AppendSgr(StringBuilder sb, Style s)
    {
        sb.Append("\x1b[0");
        if (s.Bold) sb.Append(";1");
        if (s.Dim) sb.Append(";2");
        if (s.Underline) sb.Append(";4");
        if (Style.IsSet(s.Fg)) AppendColor(sb, 38, s.Fg);
        if (Style.IsSet(s.Bg)) AppendColor(sb, 48, s.Bg);
        sb.Append('m');
    }

    static void AppendColor(StringBuilder sb, int code, int color) =>
        sb.Append(';').Append(code).Append(";2;")
          .Append((color >> 16) & 0xff).Append(';')
          .Append((color >> 8) & 0xff).Append(';')
          .Append(color & 0xff);
}
