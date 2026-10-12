using Sos.Core.Render;

namespace Sos.Core.Ui;

/// <summary>A fixed-width grid of cells that grows downward as content is drawn.</summary>
public sealed class Canvas(int width)
{
    readonly List<Cell[]> rows = [];

    public int Width { get; } = Math.Max(0, width);
    public int Height => rows.Count;
    public IReadOnlyList<Cell[]> Rows => rows;

    public void EnsureHeight(int height)
    {
        while (rows.Count < height)
        {
            var row = new Cell[Width];
            Array.Fill(row, Cell.Blank);
            rows.Add(row);
        }
    }

    public void Put(int x, int y, char ch, Style style)
    {
        if (x < 0 || y < 0 || x >= Width) return;
        EnsureHeight(y + 1);
        rows[y][x] = new Cell(Cell.Sanitize(ch), style);
    }

    /// <summary>Writes text on one line, clipped to the canvas and to <paramref name="maxWidth"/>. Returns the next x.</summary>
    public int Write(int x, int y, string text, Style style, int maxWidth = int.MaxValue)
    {
        int end = maxWidth >= Width ? Width : Math.Min(Width, x + maxWidth);
        foreach (var ch in text)
        {
            if (x >= end) break;
            Put(x++, y, ch, style);
        }
        EnsureHeight(y + 1);
        return x;
    }

    public void Fill(int x, int y, int width, int height, char ch, Style style)
    {
        for (int row = y; row < y + height; row++)
            for (int col = x; col < x + width; col++)
                Put(col, row, ch, style);
    }

    /// <summary>Copies another canvas into this one at (x, y), optionally transforming styles.</summary>
    public void Draw(Canvas source, int x, int y, Func<Style, Style>? restyle = null)
    {
        for (int row = 0; row < source.Height; row++)
        {
            var cells = source.rows[row];
            for (int col = 0; col < cells.Length; col++)
            {
                var c = cells[col];
                Put(x + col, y + row, c.Ch, restyle is null ? c.Style : restyle(c.Style));
            }
        }
        EnsureHeight(y + source.Height);
    }
}

public readonly record struct Rect(int X, int Y, int W, int H)
{
    public bool Contains(int x, int y) => x >= X && x < X + W && y >= Y && y < Y + H;

    public Rect Offset(int dx, int dy) => this with { X = X + dx, Y = Y + dy };
}
