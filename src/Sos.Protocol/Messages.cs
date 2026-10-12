using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sos.Protocol;

/// <summary>
/// One line of the JSON-lines protocol between the core (sos) and the PowerShell host (sos-host).
/// A single flat shape keeps both sides simple; each message type uses the fields it needs.
/// Session identity is the timeline entry id (<see cref="Entry"/>).
/// </summary>
public sealed class Message
{
    public string Type { get; set; } = "";
    public long Entry { get; set; }
    public string? Text { get; set; }
    public string? Error { get; set; }
    public string? Cwd { get; set; }
    public string? AppPath { get; set; }
    /// <summary>App state as a JSON string, produced by Save-AppState.</summary>
    public string? State { get; set; }
    /// <summary>Widget tree as a JSON string, produced by Show-App.</summary>
    public string? Tree { get; set; }
    public InputEvent? Event { get; set; }
    public int Width { get; set; }
    public long Target { get; set; }
}

public static class MessageTypes
{
    // core -> host
    public const string Exec = "exec";
    public const string Launch = "launch";
    public const string Input = "input";
    public const string Close = "close";

    // host -> core
    public const string Ready = "ready";
    public const string Done = "done";
    public const string Render = "render";
    public const string Exited = "exited";
    public const string LaunchRequest = "launch-request";
    public const string ResumeRequest = "resume-request";
    public const string QuitRequest = "quit-request";
    public const string Notify = "notify";
    /// <summary>Text is a .wav path, or a tone spec like "tone:440/80,660/80" (frequency Hz / milliseconds).</summary>
    public const string Sound = "sound";
}

/// <summary>An abstract input event delivered to an app's Invoke-AppInput.</summary>
public sealed class InputEvent
{
    /// <summary>click | select | submit | key</summary>
    public string Type { get; set; } = "";
    public string? Target { get; set; }
    public int Index { get; set; } = -1;
    public string? Value { get; set; }
    public string? Key { get; set; }
}

/// <summary>
/// A node of the retained UI tree an app returns from Show-App.
/// Value-type fields are nullable because PowerShell hashtables freely emit nulls.
/// </summary>
public sealed class Widget
{
    /// <summary>panel | column | row | text | button | list | input | divider | canvas</summary>
    public string? Type { get; set; }
    public string? Id { get; set; }
    public string? Text { get; set; }
    public string? Title { get; set; }
    public string? Value { get; set; }
    public string? Placeholder { get; set; }
    public List<string>? Items { get; set; }
    public int? Selected { get; set; }
    public string? Fg { get; set; }
    public string? Bg { get; set; }
    public bool? Bold { get; set; }
    public bool? Dim { get; set; }
    public List<Widget>? Children { get; set; }

    // Canvas: a Width x Height pixel area drawn from rectangles and named sprites.
    public int? Width { get; set; }
    public int? Height { get; set; }
    public Dictionary<string, Sprite>? Sprites { get; set; }
    public List<SpriteDraw>? Draw { get; set; }
    public List<FillRect>? Rects { get; set; }
}

/// <summary>
/// A small bitmap. Each row is a string; '.' and ' ' are transparent, any other character is a pixel in
/// <see cref="Color"/>, or in the color <see cref="Palette"/> maps that character to.
/// </summary>
public sealed class Sprite
{
    public List<string>? Rows { get; set; }
    public string? Color { get; set; }
    public Dictionary<string, string>? Palette { get; set; }
}

/// <summary>Draws sprite <see cref="S"/> with its top-left corner at (X, Y). Coordinates are doubles because PowerShell math often produces them.</summary>
public sealed class SpriteDraw
{
    public string? S { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public string? Color { get; set; }
}

public sealed class FillRect
{
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; } = 1;
    public double H { get; set; } = 1;
    public string? Color { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(Message))]
[JsonSerializable(typeof(Widget))]
public partial class ProtocolJson : JsonSerializerContext;

public static class Wire
{
    public static string Serialize(Message message) => JsonSerializer.Serialize(message, ProtocolJson.Default.Message);

    public static Message? Deserialize(string line) => JsonSerializer.Deserialize(line, ProtocolJson.Default.Message);

    public static Widget? ParseTree(string json) => JsonSerializer.Deserialize(json, ProtocolJson.Default.Widget);
}
