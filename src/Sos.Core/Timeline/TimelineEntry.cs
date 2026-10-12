using System.Text.Json.Serialization;

namespace Sos.Core.Timeline;

public enum EntryKind { Session, Prompt, App, Notification }

/// <summary>
/// Done/Running: commands. Live: an app in the foreground. Suspended: an app still running in the background.
/// Closed: an app that has exited and is now a frozen artifact.
/// </summary>
public enum EntryStatus { Done, Running, Live, Closed, Suspended }

/// <summary>
/// One item in the timeline. Large content (command output, the app's last UI tree, app state)
/// lives in files in the entry's folder and is loaded only when the entry scrolls into view.
/// </summary>
public sealed class TimelineEntry
{
    public long Id { get; set; }
    public DateTimeOffset Time { get; set; }
    public EntryKind Kind { get; set; }
    public EntryStatus Status { get; set; }
    /// <summary>Command text, app name, or notification text.</summary>
    public string Title { get; set; } = "";
    public string? Cwd { get; set; }
    public string? App { get; set; }
    public string? AppPath { get; set; }
    /// <summary>For resumed apps, the artifact this entry continues from.</summary>
    public long? ResumedFrom { get; set; }
    /// <summary>For an app that was suspended and later resumed, the entry where it continued.</summary>
    public long? ContinuedIn { get; set; }
    /// <summary>For notifications, the app entry that sent it.</summary>
    public long? Source { get; set; }
    public bool Error { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(TimelineEntry))]
public partial class TimelineJson : JsonSerializerContext;
