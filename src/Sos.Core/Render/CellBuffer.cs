namespace Sos.Core.Render;

/// <summary>A fixed-size grid of cells matching the terminal screen.</summary>
public sealed class CellBuffer
{
    public CellBuffer(int width, int height)
    {
        Width = Math.Max(0, width);
        Height = Math.Max(0, height);
        Cells = new Cell[Width * Height];
        Clear();
    }

    public int Width { get; }
    public int Height { get; }
    public Cell[] Cells { get; }

    public Cell this[int x, int y] => Cells[y * Width + x];

    public void Clear() => Array.Fill(Cells, Cell.Blank);

    public void Put(int x, int y, char ch, Style style)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return;
        Cells[y * Width + x] = new Cell(Cell.Sanitize(ch), style);
    }

    /// <summary>Writes text on one line, clipped to the buffer and to <paramref name="maxWidth"/>. Returns the next x.</summary>
    public int Write(int x, int y, string text, Style style, int maxWidth = int.MaxValue)
    {
        int end = maxWidth >= Width ? Width : Math.Min(Width, x + maxWidth);
        foreach (var ch in text)
        {
            if (x >= end) break;
            Put(x++, y, ch, style);
        }
        return x;
    }

    public void Fill(int x, int y, int width, int height, char ch, Style style)
    {
        for (int row = y; row < y + height; row++)
            for (int col = x; col < x + width; col++)
                Put(col, row, ch, style);
    }

    /// <summary>Copies a row of cells to (x, y), clipped to the buffer.</summary>
    public void Blit(Cell[] row, int x, int y)
    {
        if (y < 0 || y >= Height) return;
        for (int i = 0; i < row.Length; i++)
        {
            int col = x + i;
            if (col >= 0 && col < Width) Cells[y * Width + col] = row[i];
        }
    }
}
