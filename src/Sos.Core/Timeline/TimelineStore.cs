using System.Text.Json;

namespace Sos.Core.Timeline;

/// <summary>
/// Append-only timeline storage:
/// <code>
/// timeline/index.jsonl            one JSON line per entry change; the last line for an id wins
/// timeline/entries/{id}/*.txt|json per-entry content (output.txt, error.txt, tree.json, state.json)
/// </code>
/// History is never rewritten: resuming an app creates a new entry linked by <see cref="TimelineEntry.ResumedFrom"/>.
/// </summary>
public sealed class TimelineStore
{
    public const string OutputFile = "output.txt";
    public const string ErrorFile = "error.txt";
    public const string TreeFile = "tree.json";
    public const string StateFile = "state.json";

    readonly string indexPath;
    readonly List<TimelineEntry> entries = [];
    readonly Dictionary<long, TimelineEntry> byId = [];
    long nextId = 1;

    public TimelineStore(string home)
    {
        Root = Path.Combine(home, "timeline");
        Directory.CreateDirectory(Root);
        indexPath = Path.Combine(Root, "index.jsonl");
        Load();
    }

    public string Root { get; }

    public IReadOnlyList<TimelineEntry> Entries => entries;

    public TimelineEntry? Get(long id) => byId.GetValueOrDefault(id);

    void Load()
    {
        if (!File.Exists(indexPath)) return;
        foreach (var line in File.ReadLines(indexPath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            TimelineEntry? entry;
            try { entry = JsonSerializer.Deserialize(line, TimelineJson.Default.TimelineEntry); }
            catch (JsonException) { continue; } // a torn last line from a crash; skip it

            if (entry is null) continue;
            if (byId.TryGetValue(entry.Id, out var existing))
                entries[entries.IndexOf(existing)] = entry;
            else
                entries.Add(entry);
            byId[entry.Id] = entry;
            nextId = Math.Max(nextId, entry.Id + 1);
        }
    }

    public TimelineEntry Append(EntryKind kind, string title, Action<TimelineEntry>? init = null)
    {
        var entry = new TimelineEntry { Id = nextId++, Time = DateTimeOffset.Now, Kind = kind, Title = title };
        init?.Invoke(entry);
        entries.Add(entry);
        byId[entry.Id] = entry;
        Update(entry);
        return entry;
    }

    /// <summary>Persists the entry's current fields by appending a new index line.</summary>
    public void Update(TimelineEntry entry) =>
        File.AppendAllText(indexPath, JsonSerializer.Serialize(entry, TimelineJson.Default.TimelineEntry) + "\n");

    /// <summary>
    /// After an unclean exit, nothing is still running: mark running commands done and live or suspended apps closed.
    /// Returns how many entries were fixed up.
    /// </summary>
    public int RecoverInterrupted()
    {
        int count = 0;
        foreach (var e in entries)
        {
            if (e.Status == EntryStatus.Running)
            {
                e.Status = EntryStatus.Done;
                e.Error = true;
                WriteText(e.Id, ErrorFile, "Interrupted: ScaleOS exited while this was running.");
            }
            else if (e.Status is EntryStatus.Live or EntryStatus.Suspended)
            {
                e.Status = EntryStatus.Closed;
            }
            else continue;

            Update(e);
            count++;
        }
        return count;
    }

    public string EntryDir(long id) => Path.Combine(Root, "entries", id.ToString());

    public void WriteText(long id, string name, string content)
    {
        var dir = EntryDir(id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, name), content);
    }

    public string? ReadText(long id, string name)
    {
        var path = Path.Combine(EntryDir(id), name);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }
}
