using Sos.Core.Ui;
using Sos.Protocol;

namespace Sos.Core.Tests;

public class LayoutEngineTests
{
    static string Row(Canvas canvas, int y) => new(canvas.Rows[y].Select(c => c.Ch).ToArray());

    static Widget Text(string text) => new() { Type = "text", Text = text };

    [Fact]
    public void PanelDrawsBorderAroundChildren()
    {
        var panel = new Widget { Type = "panel", Title = "Notes", Children = [Text("hello")] };

        var result = LayoutEngine.Render(panel, 20, LayoutContext.Frozen);

        Assert.Equal(3, result.Canvas.Height);
        Assert.StartsWith("╭─ Notes ─", Row(result.Canvas, 0));
        Assert.Equal("│ hello            │", Row(result.Canvas, 1));
        Assert.Equal("╰" + new string('─', 18) + "╯", Row(result.Canvas, 2));
    }

    [Fact]
    public void TextWrapsToWidth()
    {
        var result = LayoutEngine.Render(Text("one two three"), 8, LayoutContext.Frozen);

        Assert.Equal(2, result.Canvas.Height);
        Assert.Equal("one two ", Row(result.Canvas, 0));
        Assert.Equal("three   ", Row(result.Canvas, 1));
    }

    [Fact]
    public void ButtonsInARowKeepNaturalWidthAndAreClickable()
    {
        var row = new Widget
        {
            Type = "row",
            Children = [new Widget { Type = "button", Text = "OK", Id = "ok" }, new Widget { Type = "button", Text = "Cancel" }],
        };

        var result = LayoutEngine.Render(row, 40, LayoutContext.Frozen);

        Assert.StartsWith("[ OK ] [ Cancel ]", Row(result.Canvas, 0));
        Assert.Equal(
            [new Hit(new Rect(0, 0, 6, 1), HitKind.Click, "ok"), new Hit(new Rect(7, 0, 10, 1), HitKind.Click, "Cancel")],
            result.Hits);
    }

    [Fact]
    public void ListMarksSelectionAndHasOneHitPerItem()
    {
        var list = new Widget { Type = "list", Id = "notes", Items = ["a", "b"], Selected = 1 };

        var result = LayoutEngine.Render(list, 10, LayoutContext.Frozen);

        Assert.Equal("  a       ", Row(result.Canvas, 0));
        Assert.Equal("▸ b       ", Row(result.Canvas, 1));
        Assert.Equal(new Hit(new Rect(0, 1, 10, 1), HitKind.Select, "notes", 1), result.Hits[1]);
    }

    [Fact]
    public void FocusedInputShowsTypedTextAndCursor()
    {
        var input = new Widget { Type = "input", Id = "new", Placeholder = "type here" };
        var context = new LayoutContext { FocusedInput = "new", InputValues = new Dictionary<string, string> { ["new"] = "abc" } };

        var result = LayoutEngine.Render(input, 20, context);

        Assert.StartsWith("› abc", Row(result.Canvas, 0));
        Assert.Equal((5, 0), result.Cursor);
        Assert.Equal(HitKind.Focus, Assert.Single(result.Hits).Kind);
    }

    [Fact]
    public void EmptyInputShowsPlaceholder()
    {
        var input = new Widget { Type = "input", Id = "new", Placeholder = "type here" };

        var result = LayoutEngine.Render(input, 20, LayoutContext.Frozen);

        Assert.StartsWith("› type here", Row(result.Canvas, 0));
        Assert.Null(result.Cursor);
    }

    [Fact]
    public void FindsFirstInput()
    {
        var tree = new Widget { Type = "panel", Children = [Text("x"), new Widget { Type = "input", Id = "q" }] };

        Assert.Equal("q", LayoutEngine.FirstInputId(tree));
        Assert.True(LayoutEngine.ContainsInput(tree, "q"));
        Assert.False(LayoutEngine.ContainsInput(tree, "z"));
    }

    [Fact]
    public void ParsesTreeJsonFromPowerShell()
    {
        // The shape ConvertTo-Json produces from the SDK's hashtables, including nulls and empty strings.
        const string json = """{"type":"panel","title":"Notes","fg":"","children":[{"type":"list","id":"notes","items":[],"selected":-1,"placeholder":null},{"type":"text","text":"hi","bold":false,"dim":true}]}""";

        var tree = Wire.ParseTree(json)!;

        Assert.Equal("panel", tree.Type);
        Assert.Equal(-1, tree.Children![0].Selected);
        Assert.True(tree.Children[1].Dim);
    }

    [Theory]
    [InlineData("short", 10, new[] { "short" })]
    [InlineData("abcdefghij", 4, new[] { "abcd", "efgh", "ij" })]
    [InlineData("a b\nc", 10, new[] { "a b", "c" })]
    public void Wrap(string text, int width, string[] expected) => Assert.Equal(expected, TextWrap.Wrap(text, width));
}
