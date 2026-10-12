using Sos.Core.Timeline;

namespace Sos.Core.Tests;

public sealed class TimelineStoreTests : IDisposable
{
    readonly string home = Path.Combine(Path.GetTempPath(), "sos-tests", Guid.NewGuid().ToString("n"));

    public void Dispose()
    {
        if (Directory.Exists(home)) Directory.Delete(home, recursive: true);
    }

    [Fact]
    public void EntriesSurviveReload()
    {
        var store = new TimelineStore(home);
        var prompt = store.Append(EntryKind.Prompt, "Get-Date", e => e.Status = EntryStatus.Running);
        store.WriteText(prompt.Id, TimelineStore.OutputFile, "Saturday");
        prompt.Status = EntryStatus.Done;
        store.Update(prompt);

        var reloaded = new TimelineStore(home);

        var entry = Assert.Single(reloaded.Entries);
        Assert.Equal(EntryStatus.Done, entry.Status);
        Assert.Equal("Get-Date", entry.Title);
        Assert.Equal("Saturday", reloaded.ReadText(entry.Id, TimelineStore.OutputFile));
    }

    [Fact]
    public void IdsKeepIncreasingAcrossReloads()
    {
        var first = new TimelineStore(home).Append(EntryKind.Notification, "a");
        var second = new TimelineStore(home).Append(EntryKind.Notification, "b");

        Assert.Equal(first.Id + 1, second.Id);
        Assert.Equal(["a", "b"], new TimelineStore(home).Entries.Select(e => e.Title));
    }

    [Fact]
    public void ResumeLinkIsStored()
    {
        var store = new TimelineStore(home);
        var original = store.Append(EntryKind.App, "Notes", e => e.Status = EntryStatus.Closed);
        store.Append(EntryKind.App, "Notes", e => e.ResumedFrom = original.Id);

        Assert.Equal(original.Id, new TimelineStore(home).Entries[^1].ResumedFrom);
    }

    [Fact]
    public void RecoverInterruptedClosesLiveAppsAndFinishesCommands()
    {
        var store = new TimelineStore(home);
        store.Append(EntryKind.App, "Notes", e => e.Status = EntryStatus.Live);
        store.Append(EntryKind.Prompt, "Start-Sleep 100", e => e.Status = EntryStatus.Running);
        store.Append(EntryKind.App, "Timer", e => e.Status = EntryStatus.Suspended);

        var reloaded = new TimelineStore(home);
        Assert.Equal(3, reloaded.RecoverInterrupted());

        var after = new TimelineStore(home).Entries;
        Assert.Equal(EntryStatus.Closed, after[0].Status);
        Assert.Equal(EntryStatus.Done, after[1].Status);
        Assert.True(after[1].Error);
        Assert.Equal(EntryStatus.Closed, after[2].Status);
    }

    [Fact]
    public void LegacyScrollOSHomeIsMovedOnce()
    {
        var legacy = Path.Combine(home, ".scrollos");
        var current = Path.Combine(home, ".sos");
        new TimelineStore(legacy).Append(EntryKind.Notification, "from the old name");

        Assert.True(Lifecycle.SosPaths.MigrateLegacyHome(legacy, current));
        Assert.False(Directory.Exists(legacy));
        Assert.Equal("from the old name", Assert.Single(new TimelineStore(current).Entries).Title);

        // Once the new folder exists, an old one (e.g. recreated by an old build) is left alone.
        Directory.CreateDirectory(legacy);
        Assert.False(Lifecycle.SosPaths.MigrateLegacyHome(legacy, current));
    }

    [Fact]
    public void TornLastLineIsIgnored()
    {
        new TimelineStore(home).Append(EntryKind.Notification, "ok");
        File.AppendAllText(Path.Combine(home, "timeline", "index.jsonl"), "{\"id\":2,\"tit");

        Assert.Single(new TimelineStore(home).Entries);
    }
}
