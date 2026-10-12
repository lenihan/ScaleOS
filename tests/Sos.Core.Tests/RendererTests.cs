using Sos.Core.Render;

namespace Sos.Core.Tests;

public class RendererTests
{
    [Fact]
    public void FirstFrameClearsAndDrawsEverything()
    {
        var buffer = new CellBuffer(4, 2);
        buffer.Write(0, 0, "ab", default);

        var output = new Renderer().Render(buffer, null);

        Assert.Contains("\x1b[2J", output);
        Assert.Contains("ab", output);
    }

    [Fact]
    public void SecondFrameOnlyWritesChangedCells()
    {
        var renderer = new Renderer();
        var buffer = new CellBuffer(10, 3);
        buffer.Write(0, 0, "hello", default);
        renderer.Render(buffer, null);

        buffer.Put(4, 1, 'X', default);
        var output = renderer.Render(buffer, null);

        Assert.DoesNotContain("\x1b[2J", output);
        Assert.DoesNotContain("hello", output);
        Assert.Contains("\x1b[2;5H", output);
        Assert.Contains("X", output);
    }

    [Fact]
    public void UnchangedFrameWritesNoCells()
    {
        var renderer = new Renderer();
        var buffer = new CellBuffer(5, 1);
        buffer.Write(0, 0, "abc", default);
        renderer.Render(buffer, null);

        Assert.Equal("\x1b[?25l\x1b[0m", renderer.Render(buffer, null));
    }

    [Fact]
    public void TruecolorStyle()
    {
        var buffer = new CellBuffer(1, 1);
        buffer.Put(0, 0, 'x', new Style(Fg: Style.Rgb(1, 2, 3), Bold: true));

        Assert.Contains("\x1b[0;1;38;2;1;2;3m", new Renderer().Render(buffer, null));
    }

    [Fact]
    public void CursorIsPositionedAndShown()
    {
        var output = new Renderer().Render(new CellBuffer(5, 5), (2, 3));
        Assert.EndsWith("\x1b[4;3H\x1b[?25h", output);
    }

    [Fact]
    public void HexColors()
    {
        Assert.Equal(Style.Rgb(0x12, 0x34, 0x56), Style.Hex("#123456"));
        Assert.Equal(0, Style.Hex(""));
        Assert.Equal(0, Style.Hex("nope"));
    }
}
