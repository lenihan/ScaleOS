using Sos.Protocol;

namespace Sos.Core.Lifecycle;

/// <summary>Core-side state of a running app: its latest UI tree and any text typed into its inputs.</summary>
public sealed class AppSession(long entryId, string name, string appPath)
{
    /// <summary>The id the PowerShell host knows this session by: the entry it was launched in. Never changes.</summary>
    public long HostId { get; } = entryId;
    /// <summary>The timeline entry currently showing this app. Moves to a new entry when a suspended app is resumed.</summary>
    public long EntryId { get; set; } = entryId;
    public string Name { get; } = name;
    public string AppPath { get; } = appPath;

    /// <summary>The raw JSON from the host, kept verbatim so the closed artifact can be stored exactly as last seen.</summary>
    public string? TreeJson { get; private set; }
    public Widget? Tree { get; private set; }
    public string? Error { get; set; }
    public bool Closing { get; set; }

    public Dictionary<string, string> Inputs { get; } = [];
    public string? FocusedInput { get; set; }

    /// <summary>Keys typed before the app's first render; replayed once it knows which input has focus.</summary>
    public List<Input.KeyEvent> PendingKeys { get; } = [];

    public void SetTree(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        try
        {
            Tree = Wire.ParseTree(json);
            TreeJson = json;
        }
        catch (System.Text.Json.JsonException ex)
        {
            Error = "Show-App returned something that isn't a widget tree: " + ex.Message;
        }

        // Keep keyboard focus on an input that still exists, or move it to the first one.
        if (FocusedInput is null || !Ui.LayoutEngine.ContainsInput(Tree, FocusedInput))
            FocusedInput = Ui.LayoutEngine.FirstInputId(Tree);
    }
}
