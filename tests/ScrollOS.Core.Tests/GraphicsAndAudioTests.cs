using ScrollOS.Core.Audio;
using ScrollOS.Core.Render;
using ScrollOS.Core.Ui;
using ScrollOS.Protocol;

namespace ScrollOS.Core.Tests;

public class GraphicsAndAudioTests
{
    static readonly int Red = Style.Rgb(255, 0, 0);
    static readonly int Blue = Style.Rgb(0, 0, 255);

    [Fact]
    public void CanvasPacksTwoPixelsPerCellWithHalfBlocks()
    {
        var canvas = new Widget
        {
            Type = "canvas",
            Width = 3,
            Height = 4,
            Bg = "#0000ff",
            Sprites = new() { ["dot"] = new Sprite { Rows = ["#.", ".#"], Color = "#ff0000" } },
            Draw = [new SpriteDraw { S = "dot", X = 0, Y = 0 }],
        };

        var result = LayoutEngine.Render(canvas, 10, LayoutContext.Frozen);
        var row = result.Canvas.Rows[0];

        Assert.Equal(2, result.Canvas.Height);                        // 4 pixels tall -> 2 rows
        Assert.Equal(new Cell('▀', new Style(Fg: Red, Bg: Blue)), row[0]);  // red over blue
        Assert.Equal(new Cell('▀', new Style(Fg: Blue, Bg: Red)), row[1]);  // blue over red
        Assert.Equal(new Cell(' ', new Style(Bg: Blue)), row[2]);           // both background
        Assert.Equal(new Cell(' ', default), row[3]);                       // outside the canvas
    }

    [Fact]
    public void CanvasRectsAndTransparentBackground()
    {
        var canvas = new Widget
        {
            Type = "canvas",
            Width = 2,
            Height = 2,
            Rects = [new FillRect { X = 1, Y = 1, W = 1, H = 1, Color = "#ff0000" }],
        };

        var row = LayoutEngine.Render(canvas, 10, LayoutContext.Frozen).Canvas.Rows[0];

        Assert.Equal(new Cell(' ', default), row[0]);
        Assert.Equal(new Cell('▄', new Style(Fg: Red)), row[1]);
    }

    [Fact]
    public void CanvasClipsSpritesAndUsesPalette()
    {
        var canvas = new Widget
        {
            Type = "canvas",
            Width = 2,
            Height = 2,
            Sprites = new() { ["s"] = new Sprite { Rows = ["rb", "rb"], Palette = new() { ["r"] = "#ff0000", ["b"] = "#0000ff" } } },
            Draw = [new SpriteDraw { S = "s", X = -1, Y = 0 }, new SpriteDraw { S = "missing", X = 0, Y = 0 }],
        };

        var row = LayoutEngine.Render(canvas, 10, LayoutContext.Frozen).Canvas.Rows[0];

        Assert.Equal(new Cell(' ', new Style(Bg: Blue)), row[0]);
        Assert.Equal(new Cell(' ', default), row[1]);
    }

    [Fact]
    public void ToneSpecParses()
    {
        Assert.Equal([(440.0, 80), (0.0, 40), (660.0, 120)], ToneSynth.Parse("tone:440/80,0/40,660/120"));
        Assert.Null(ToneSynth.Parse("tone:440"));
        Assert.Null(ToneSynth.Parse("tone:440/-5"));
        Assert.Null(ToneSynth.Parse("beep:440/80"));
    }

    [Theory]
    [InlineData("/usr/bin/aplay", new[] { "-q", "x.wav" })]
    [InlineData("/usr/bin/paplay", new[] { "x.wav" })] // paplay rejects -q; its name also ends in "aplay"
    public void LinuxPlayerArguments(string player, string[] expected)
    {
#pragma warning disable CA1416 // pure argument logic; safe to test on any OS
        Assert.Equal(expected, LinuxAudio.PlayerArguments(player, "x.wav"));
#pragma warning restore CA1416
    }

    [Fact]
    public void ToneSynthWritesPcmWav()
    {
        var wav = ToneSynth.Wav("sine:440/100")!;

        Assert.Equal("RIFF"u8.ToArray(), wav[..4]);
        Assert.Equal("WAVE"u8.ToArray(), wav[8..12]);
        int samples = 22050 * 100 / 1000;
        Assert.Equal(44 + samples * 2, wav.Length);
        Assert.Equal(samples * 2, BitConverter.ToInt32(wav, 40));
    }
}
