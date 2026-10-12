using Sos.Core.Input;

namespace Sos.Core.Tests;

public class VtInputParserTests
{
    readonly VtInputParser parser = new();

    [Fact]
    public void PlainTextBecomesCharKeys()
    {
        var events = parser.Feed("hi");
        Assert.Equal([new KeyEvent(Key.Char, 'h'), new KeyEvent(Key.Char, 'i')], events);
    }

    [Theory]
    [InlineData("\r", Key.Enter)]
    [InlineData("\t", Key.Tab)]
    [InlineData("\x7f", Key.Backspace)]
    [InlineData("\x1b", Key.Escape)]
    [InlineData("\x1b[A", Key.Up)]
    [InlineData("\x1bOB", Key.Down)]
    [InlineData("\x1b[3~", Key.Delete)]
    [InlineData("\x1b[5~", Key.PageUp)]
    [InlineData("\x1b[6~", Key.PageDown)]
    [InlineData("\x1b[Z", Key.BackTab)]
    public void SpecialKeys(string input, Key expected)
    {
        var key = Assert.IsType<KeyEvent>(Assert.Single(parser.Feed(input)));
        Assert.Equal(expected, key.Key);
    }

    [Fact]
    public void CtrlLetter()
    {
        var key = Assert.IsType<KeyEvent>(Assert.Single(parser.Feed("\x11")));
        Assert.True(key.IsCtrl('q'));
        Assert.Equal("Ctrl+Q", key.Name);
    }

    [Fact]
    public void ArrowWithModifiers()
    {
        var key = Assert.IsType<KeyEvent>(Assert.Single(parser.Feed("\x1b[1;5C")));
        Assert.Equal(new KeyEvent(Key.Right, '\0', Mods.Ctrl), key);
    }

    [Fact]
    public void SgrMousePressAndRelease()
    {
        var events = parser.Feed("\x1b[<0;10;5M\x1b[<0;10;5m");
        Assert.Equal(
            [new MouseEvent(MouseKind.Down, MouseButton.Left, 9, 4), new MouseEvent(MouseKind.Up, MouseButton.Left, 9, 4)],
            events);
    }

    [Fact]
    public void SgrMouseWheelAndMotion()
    {
        var events = parser.Feed("\x1b[<64;1;1M\x1b[<65;1;1M\x1b[<35;3;4M\x1b[<32;3;4M");
        Assert.Equal(
            [
                new MouseEvent(MouseKind.WheelUp, MouseButton.None, 0, 0),
                new MouseEvent(MouseKind.WheelDown, MouseButton.None, 0, 0),
                new MouseEvent(MouseKind.Move, MouseButton.None, 2, 3),
                new MouseEvent(MouseKind.Drag, MouseButton.Left, 2, 3),
            ],
            events);
    }

    [Fact]
    public void SequenceSplitAcrossChunksIsReassembled()
    {
        Assert.Empty(parser.Feed("\x1b[<0;1"));
        var mouse = Assert.IsType<MouseEvent>(Assert.Single(parser.Feed("2;7M")));
        Assert.Equal((11, 6), (mouse.X, mouse.Y));
    }

    [Fact]
    public void AltKey()
    {
        var key = Assert.IsType<KeyEvent>(Assert.Single(parser.Feed("\x1bx")));
        Assert.Equal(new KeyEvent(Key.Char, 'x', Mods.Alt), key);
    }
}
