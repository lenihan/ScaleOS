using System.Runtime.CompilerServices;
using System.Text;
using System.Collections.Concurrent;
using Sos.Core.Audio;
using Sos.Core.Lifecycle;
using Sos.Core.Terminal;
using Xunit.Abstractions;

namespace Sos.Core.Tests;

/// <summary>
/// Runs the real core and PowerShell host against a fake terminal and walks the MVP script from docs/vision.md:
/// launch an app, interact, exit, still see it, restart, and resume it from history.
/// </summary>
public sealed class EndToEndTests(ITestOutputHelper output) : IDisposable
{
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    readonly string home = Path.Combine(Path.GetTempPath(), "sos-tests", Guid.NewGuid().ToString("n"));
    readonly RecordingAudio audio = new();

    public void Dispose()
    {
        if (Directory.Exists(home)) Directory.Delete(home, recursive: true);
    }

    [Fact]
    public async Task LaunchInteractExitAndResumeFromHistory()
    {
        var paths = TestPaths();

        // 1-2. Launch NotesPS and add a note.
        var terminal = new FakeTerminal(100, 40);
        var run = Task.Run(() => new SosApp(terminal, paths, audio).RunAsync());
        await terminal.WaitFor("PowerShell ●");
        terminal.Type("notes\r");
        await terminal.WaitFor("No notes yet.");
        terminal.Type("buy milk\r");
        await terminal.WaitFor("▸ buy milk");

        // 3-4. Exit it; the artifact stays in the timeline.
        terminal.Type("\x1b");
        await terminal.WaitFor("· closed");
        Assert.Contains("buy milk", terminal.Screen());
        output.WriteLine("After closing the app:\n" + terminal.Screen());
        terminal.Type("\x11"); // Ctrl+Q
        Assert.Equal(0, await run.WaitAsync(Timeout));

        // Restart: history is still there, before PowerShell has even started.
        var terminal2 = new FakeTerminal(100, 40);
        var run2 = Task.Run(() => new SosApp(terminal2, paths, audio).RunAsync());
        await terminal2.WaitFor("· closed");
        Assert.Contains("buy milk", terminal2.Screen());
        await terminal2.WaitFor("PowerShell ●");

        // 5. Click [ Resume ]: a new live entry continues from the artifact with its notes restored.
        var (x, y) = terminal2.Find("[ Resume ]");
        terminal2.Type($"\x1b[<0;{x + 2};{y + 1}M\x1b[<0;{x + 2};{y + 1}m");
        await terminal2.WaitFor("↻ from #");
        await terminal2.WaitFor("● live");
        terminal2.Type("research PTYs\r");
        await terminal2.WaitFor("▸ research PTYs");
        output.WriteLine("After resuming:\n" + terminal2.Screen());

        terminal2.Type("\x11");
        Assert.Equal(0, await run2.WaitAsync(Timeout));

        // Quitting saved the resumed app's state too.
        var index = await File.ReadAllTextAsync(Path.Combine(home, "timeline", "index.jsonl"));
        Assert.Contains("\"resumedFrom\"", index);
        var states = Directory.GetFiles(Path.Combine(home, "timeline", "entries"), "state.json", SearchOption.AllDirectories)
            .Select(File.ReadAllText).ToList();
        Assert.Contains(states, s => s.Contains("buy milk") && s.Contains("research PTYs"));
    }

    [Fact]
    public async Task SuspendedAppKeepsRunningAndNotifies()
    {
        var paths = TestPaths();
        var terminal = new FakeTerminal(100, 40);
        var run = Task.Run(() => new SosApp(terminal, paths, audio).RunAsync());
        await terminal.WaitFor("PowerShell ●");

        // Start a ~2 second timer, then send it to the background.
        terminal.Type("timer\r");
        await terminal.WaitFor("05:00");
        terminal.Type("0.03 tea\r");
        await terminal.WaitFor("Running");
        terminal.Type("\x1a"); // Ctrl+Z
        await terminal.WaitFor("◐ running in background");
        await terminal.WaitFor("◐ 1 in background");

        // It keeps ticking in the background and posts a notification when done.
        await terminal.WaitFor("'tea' finished");
        output.WriteLine("After the background timer finished:\n" + terminal.Screen());

        // Clicking the notification brings the app back at the bottom; the old entry is frozen and points to it.
        var (x, y) = terminal.Find("'tea' finished");
        terminal.Type($"\x1b[<0;{x + 1};{y + 1}M\x1b[<0;{x + 1};{y + 1}m");
        await terminal.WaitFor("continued in #");
        await terminal.WaitFor("● live");
        Assert.DoesNotContain("in background", terminal.Screen());
        output.WriteLine("After clicking the notification:\n" + terminal.Screen());

        terminal.Type("\x11");
        Assert.Equal(0, await run.WaitAsync(Timeout));
        Assert.Contains(audio.Played, s => s.StartsWith("sine:")); // the notification chime
    }

    [Fact]
    public async Task GameDrawsAnimatesPlaysSoundsAndPausesInBackground()
    {
        var paths = TestPaths();
        var terminal = new FakeTerminal(100, 40);
        var run = Task.Run(() => new SosApp(terminal, paths, audio).RunAsync());
        await terminal.WaitFor("PowerShell ●");

        terminal.Type("invaders\r");
        await terminal.WaitFor("Score 0");
        Assert.Contains("▀", terminal.Screen()); // sprites drawn with half blocks

        // Firing plays a sound and the shot animates on timer ticks.
        terminal.Type(" ");
        await WaitUntil(() => audio.Played.Contains("tone:1400/25,900/25"),
            () => $"fire sound; played: [{string.Join(" | ", audio.Played)}]\n{terminal.Screen()}\nhost log:\n" +
                  (File.Exists(paths.HostLog) ? File.ReadAllText(paths.HostLog) : "(none)"));
        var before = terminal.Screen();
        await WaitUntil(() => terminal.Screen() != before, () => "animation frame");
        output.WriteLine("Playing:\n" + terminal.Screen());

        // Sending it to the background pauses the game.
        terminal.Type("\x1a");
        await terminal.WaitFor("Paused · press P to continue");
        await terminal.WaitFor("◐ running in background");

        terminal.Type("\x11");
        Assert.Equal(0, await run.WaitAsync(Timeout));

        var state = Directory.GetFiles(Path.Combine(home, "timeline", "entries"), "state.json", SearchOption.AllDirectories)
            .Select(File.ReadAllText).Single();
        Assert.Contains("\"playerX\"", state);
        Assert.Contains("\"state\":\"paused\"", state);
    }

    [Fact]
    public async Task MissingAudioIsExplainedOnceInTheTimeline()
    {
        var paths = TestPaths();
        var terminal = new FakeTerminal(100, 40);
        var run = Task.Run(() => new SosApp(terminal, paths, new UnavailableAudio()).RunAsync());
        await terminal.WaitFor("PowerShell ●");

        terminal.Type("invaders\r");
        await terminal.WaitFor("Score 0");
        terminal.Type(" ");
        await terminal.WaitFor("No player here");
        terminal.Type(" ");
        await Task.Delay(500);
        terminal.Type("\x11");
        Assert.Equal(0, await run.WaitAsync(Timeout));

        var index = await File.ReadAllTextAsync(Path.Combine(home, "timeline", "index.jsonl"));
        Assert.Single(index.Split('\n'), line => line.Contains("No player here"));
    }

    sealed class UnavailableAudio : IAudio
    {
        public void Play(string sound) => throw new InvalidOperationException("Play shouldn't be called when audio is unavailable.");

        public string? Unavailable => "No player here.";
    }

    static async Task WaitUntil(Func<bool> condition, Func<string> what)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new Xunit.Sdk.XunitException($"Timed out waiting for {what()}.");
            await Task.Delay(50);
        }
    }

    sealed class RecordingAudio : IAudio
    {
        readonly ConcurrentQueue<string> played = new();

        public IReadOnlyCollection<string> Played => played;

        public void Play(string sound) => played.Enqueue(sound);
    }

    SosPaths TestPaths([CallerFilePath] string sourceFile = "")
    {
        // The repo is found from this source file so the tests also work when built with --artifacts-path.
        var repo = Path.GetDirectoryName(sourceFile)!;
        while (!File.Exists(Path.Combine(repo, "Sos.sln"))) repo = Path.GetDirectoryName(repo)!;
        var configuration = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}") ? "Release" : "Debug";
        string[] candidates =
        [
            // Built with --artifacts-path: <artifacts>/bin/Sos.Core.Tests/<config>/ next to <artifacts>/bin/Sos.Core/<config>/
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "Sos.Core", configuration.ToLowerInvariant(), "host", "sos-host.exe")),
            Path.Combine(repo, "src", "Sos.Core", "bin", configuration, "net10.0", "host", "sos-host.exe"),
        ];
        var hostExe = candidates.FirstOrDefault(File.Exists);
        Assert.True(hostExe is not null, $"Build the solution first; host not found at {string.Join(" or ", candidates)}");
        return new SosPaths(home, Path.Combine(repo, "apps"), Path.Combine(repo, "src", "Sos.Sdk", "Sos.Sdk.psm1"), hostExe);
    }

    /// <summary>A terminal that applies ScaleOS's VT output to an in-memory screen (cursor moves, clears, text).</summary>
    sealed class FakeTerminal(int width, int height) : ITerminal
    {
        readonly char[][] rows = Enumerable.Range(0, height).Select(_ => Enumerable.Repeat(' ', width).ToArray()).ToArray();
        readonly object gate = new();
        Action<string>? onInput;
        int cx, cy;

        public (int Width, int Height) Size => (width, height);

        public void Start(Action<string> onInput) => this.onInput = onInput;

        public void Type(string text) => onInput!(text);

        public void Write(string vt)
        {
            lock (gate)
            {
                for (int i = 0; i < vt.Length; i++)
                {
                    if (vt[i] == '\x1b' && i + 1 < vt.Length && vt[i + 1] == '[')
                    {
                        int j = i + 2;
                        while (j < vt.Length && (vt[j] < '@' || vt[j] > '~')) j++;
                        var parameters = vt[(i + 2)..j].Split(';');
                        if (vt[j] == 'H')
                        {
                            cy = parameters.Length > 0 && int.TryParse(parameters[0], out var r) ? r - 1 : 0;
                            cx = parameters.Length > 1 && int.TryParse(parameters[1], out var c) ? c - 1 : 0;
                        }
                        else if (vt[j] == 'J')
                        {
                            foreach (var row in rows) Array.Fill(row, ' ');
                        }
                        i = j;
                    }
                    else if (cx < width && cy < height)
                    {
                        rows[cy][cx++] = vt[i];
                    }
                }
            }
        }

        public string Screen()
        {
            lock (gate) return string.Join("\n", rows.Select(r => new string(r)));
        }

        public (int X, int Y) Find(string text)
        {
            var lines = Screen().Split('\n');
            for (int y = lines.Length - 1; y >= 0; y--)
                if (lines[y].IndexOf(text, StringComparison.Ordinal) is var x and >= 0) return (x, y);
            throw new Xunit.Sdk.XunitException($"'{text}' not on screen:\n{Screen()}");
        }

        public async Task WaitFor(string text)
        {
            var deadline = DateTime.UtcNow + Timeout;
            while (!Screen().Contains(text))
            {
                if (DateTime.UtcNow > deadline) throw new Xunit.Sdk.XunitException($"Timed out waiting for '{text}'. Screen:\n{Screen()}");
                await Task.Delay(50);
            }
        }

        public void Dispose() { }
    }
}
