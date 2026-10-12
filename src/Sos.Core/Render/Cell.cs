namespace Sos.Core.Render;

/// <summary>
/// Visual attributes of a cell. Colors are 0 for "terminal default", otherwise <see cref="Rgb"/>-encoded,
/// so <c>default(Style)</c> is plain default text.
/// </summary>
public readonly record struct Style(int Fg = 0, int Bg = 0, bool Bold = false, bool Dim = false, bool Underline = false)
{
    const int HasColor = 0x1000000;

    public static int Rgb(int r, int g, int b) => HasColor | (r << 16) | (g << 8) | b;

    /// <summary>Parses "#rrggbb" (or "rrggbb"); returns 0 (default color) when missing or invalid.</summary>
    public static int Hex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return 0;
        var s = hex.Trim().TrimStart('#');
        return s.Length == 6 && int.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out var v)
            ? HasColor | v
            : 0;
    }

    public static bool IsSet(int color) => (color & HasColor) != 0;
}

public readonly record struct Cell(char Ch, Style Style)
{
    public static readonly Cell Blank = new(' ', default);

    public static char Sanitize(char ch) => char.IsControl(ch) ? ' ' : ch;
}

public static class Theme
{
    public static readonly int Accent = Style.Rgb(97, 175, 239);
    public static readonly int Muted = Style.Rgb(128, 134, 150);
    public static readonly int ErrorFg = Style.Rgb(240, 113, 120);
    public static readonly int Ok = Style.Rgb(152, 195, 121);
    public static readonly int Border = Style.Rgb(88, 96, 116);
    public static readonly int BarBg = Style.Rgb(36, 40, 52);
    public static readonly int ButtonBg = Style.Rgb(52, 61, 86);
    public static readonly int SelectedBg = Style.Rgb(44, 72, 110);
    public static readonly int InputBg = Style.Rgb(30, 34, 44);
}
