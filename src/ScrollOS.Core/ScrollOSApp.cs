using System.Threading.Channels;
using ScrollOS.Core.Audio;
using ScrollOS.Core.Input;
using ScrollOS.Core.Lifecycle;
using ScrollOS.Core.Render;
using ScrollOS.Core.Shell;
using ScrollOS.Core.Terminal;
using ScrollOS.Core.Timeline;
using ScrollOS.Core.Ui;
using ScrollOS.Protocol;

namespace ScrollOS.Core;

/// <summary>
/// The ScrollOS runtime: one event loop that owns the timeline, routes input, talks to the PowerShell host
/// and redraws the screen. Everything runs on the loop's thread; other threads only post events.
/// </summary>
public sealed class ScrollOSApp
{
    abstract record LoopEvent;
    sealed record InputChunk(string Text) : LoopEvent;
    sealed record HostMessage(Message Message) : LoopEvent;
    sealed record HostExited(HostClient Client) : LoopEvent;
    sealed record Tick : LoopEvent;

    const int WheelLines = 3;
    const int MaxHostRestarts = 3;
    static readonly TimeSpan DoubleClickTime = TimeSpan.FromMilliseconds(400);
    static readonly TimeSpan QuitTimeout = TimeSpan.FromSeconds(3);

    readonly ITerminal terminal;
    readonly ScrollPaths paths;
    readonly IAudio audio;
    readonly TimelineStore store;
    readonly Dictionary<long, AppSession> sessions = [];
    readonly TimelineView view;
    readonly Renderer renderer = new();
    readonly VtInputParser parser = new();
    readonly LineEditor editor = new();
    readonly Channel<LoopEvent> events = Channel.CreateUnbounded<LoopEvent>(new() { SingleReader = true });
    readonly List<UiHit> hits = [];

    HostClient? host;
    bool hostReady;
    /// <summary>True once the host couldn't be started or kept crashing; before the first start it's just "starting".</summary>
    bool hostFailed;
    int hostRestarts;
    string? cwd;
    long? focusedApp;
    int scroll;
    bool dirty = true;
    (int W, int H) size;
    string clock = "";
    DateTime lastClick;
    (int X, int Y) lastClickAt;
    DateTime? quitDeadline;
    bool stop;

    /// <summary>Plays when a background app posts a notification.</summary>
    const string NotificationChime = "sine:880/90,1320/160";

    public ScrollOSApp(ITerminal terminal, ScrollPaths paths, IAudio? audio = null)
    {
        this.terminal = terminal;
        this.paths = paths;
        this.audio = audio ?? DefaultAudio(paths);
        store = new TimelineStore(paths.Home);
        view = new TimelineView(store, sessions);
    }

    static IAudio DefaultAudio(ScrollPaths paths)
    {
        var cache = Path.Combine(paths.Home, "cache", "sounds");
        if (Environment.GetEnvironmentVariable("SCROLLOS_MUTE") is "1") return new NullAudio();
        if (OperatingSystem.IsWindows()) return new WindowsAudio(cache);
        if (OperatingSystem.IsLinux()) return new LinuxAudio(cache, Trace ? line => TraceLine(paths, "audio: " + line) : null);
        return new NullAudio();
    }

    public async Task<int> RunAsync()
    {
        bool firstRun = store.Entries.Count == 0;
        store.RecoverInterrupted();
        foreach (var e in store.Entries)
            if (e.Kind == EntryKind.Prompt) editor.AddHistory(e.Title);
        store.Append(EntryKind.Session, "ScrollOS started");
        if (firstRun)
            Notify("Welcome to ScrollOS. History is your desktop. Type 'notes' to launch an app, Esc to close it, " +
                   "then scroll up and press [ Resume ] (or run Resume-App <id>) to bring it back.");

        // Show the persisted timeline before PowerShell has even started.
        size = terminal.Size;
        Draw();

        terminal.Start(text => events.Writer.TryWrite(new InputChunk(text)));
        StartHost();
        _ = TickAsync();

        await foreach (var ev in events.Reader.ReadAllAsync())
        {
            try { Handle(ev); }
            catch (Exception ex) { Notify($"Internal error: {ex.Message}", error: true); }

            if (stop) break;
            if (dirty && !events.Reader.TryPeek(out _)) Draw();
        }

        host?.Dispose();
        return 0;
    }

    async Task TickAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        while (await timer.WaitForNextTickAsync())
            events.Writer.TryWrite(new Tick());
    }

    void StartHost()
    {
        var client = new HostClient(paths);
        try
        {
            client.Start(m => events.Writer.TryWrite(new HostMessage(m)), () => events.Writer.TryWrite(new HostExited(client)));
            host = client;
            hostFailed = false;
        }
        catch (Exception ex)
        {
            hostFailed = true;
            Notify(ex.Message, error: true);
        }
    }

    void Handle(LoopEvent ev)
    {
        switch (ev)
        {
            case InputChunk chunk:
                foreach (var input in parser.Feed(chunk.Text)) HandleInput(input);
                break;
            case HostMessage hm:
                HandleHost(hm.Message);
                break;
            case HostExited he when he.Client == host:
                HandleHostExit();
                break;
            case Tick:
                HandleTick();
                break;
        }
    }

    void HandleTick()
    {
        var current = terminal.Size;
        if (current != size)
        {
            size = current;
            renderer.Invalidate();
            view.InvalidateAll();
            dirty = true;
        }

        var now = DateTime.Now.ToString("HH:mm");
        if (now != clock) dirty = true;

        if (quitDeadline is { } deadline && (sessions.Count == 0 || DateTime.Now > deadline)) stop = true;
    }

    // ---------------------------------------------------------------- input

    void HandleInput(TermEvent input)
    {
        switch (input)
        {
            case KeyEvent key:
                HandleKey(key);
                dirty = true;
                break;
            // Hover and drag don't change anything yet, so they don't cost a redraw.
            case MouseEvent { Kind: MouseKind.Move or MouseKind.Drag or MouseKind.Up }:
                break;
            case MouseEvent mouse:
                HandleMouse(mouse);
                dirty = true;
                break;
        }
    }

    void HandleKey(KeyEvent key)
    {
        if (key.IsCtrl('q'))
        {
            BeginQuit();
            return;
        }

        int page = Math.Max(1, size.H - 4);
        switch (key.Key)
        {
            case Key.PageUp: scroll += page; return;
            case Key.PageDown: scroll = Math.Max(0, scroll - page); return;
        }

        if (focusedApp is { } id && sessions.TryGetValue(id, out var session))
        {
            HandleAppKey(session, key);
            return;
        }

        if (key.Key == Key.Enter)
        {
            Submit(editor.Submit());
            return;
        }
        if (key.Key == Key.Tab && LiveSessionNearestBottom() is { } live)
        {
            Focus(live);
            return;
        }
        if (key.Key == Key.Escape)
        {
            editor.Handle(new KeyEvent(Key.Char, 'u', Mods.Ctrl));
            return;
        }
        if (editor.Handle(key)) scroll = 0;
    }

    void HandleAppKey(AppSession session, KeyEvent key)
    {
        if (key.IsCtrl('z'))
        {
            Suspend(session.EntryId);
            return;
        }
        switch (key.Key)
        {
            case Key.Escape:
                Close(session.EntryId);
                return;
            case Key.Tab:
                Focus(null);
                return;
        }

        if (session.Tree is null)
        {
            session.PendingKeys.Add(key);
            return;
        }

        if (session.FocusedInput is { } inputId)
        {
            var value = session.Inputs.GetValueOrDefault(inputId) ?? "";
            if (key.IsText)
            {
                session.Inputs[inputId] = value + key.Ch;
                view.Invalidate(session.EntryId);
                return;
            }
            if (key.Key == Key.Backspace)
            {
                if (value.Length > 0) session.Inputs[inputId] = value[..^1];
                view.Invalidate(session.EntryId);
                return;
            }
            if (key.Key == Key.Enter)
            {
                session.Inputs.Remove(inputId);
                SendEvent(session, new InputEvent { Type = "submit", Target = inputId, Value = value });
                return;
            }
        }

        SendEvent(session, new InputEvent { Type = "key", Key = key.Name });
    }

    void HandleMouse(MouseEvent mouse)
    {
        switch (mouse.Kind)
        {
            case MouseKind.WheelUp:
                scroll += WheelLines;
                return;
            case MouseKind.WheelDown:
                scroll = Math.Max(0, scroll - WheelLines);
                return;
            case MouseKind.Down when mouse.Button == MouseButton.Left:
                break;
            default:
                return;
        }

        var now = DateTime.Now;
        bool doubleClick = now - lastClick < DoubleClickTime && lastClickAt == (mouse.X, mouse.Y);
        lastClick = doubleClick ? default : now;
        lastClickAt = (mouse.X, mouse.Y);

        UiHit? hit = null;
        for (int i = hits.Count - 1; i >= 0; i--)
        {
            if (hits[i].Rect.Contains(mouse.X, mouse.Y))
            {
                hit = hits[i];
                break;
            }
        }
        if (hit is null) return;

        var action = hit.Action;
        switch (action.Kind)
        {
            case UiActionKind.FocusPrompt:
                Focus(null);
                break;
            case UiActionKind.Resume:
                Resume(action.Entry);
                break;
            case UiActionKind.Artifact when doubleClick:
                Resume(action.Entry);
                break;
            case UiActionKind.Close:
                Close(action.Entry);
                break;
            case UiActionKind.Suspend:
                Suspend(action.Entry);
                break;
            case UiActionKind.FocusApp:
                Focus(action.Entry);
                break;
            case UiActionKind.AppHit when sessions.TryGetValue(action.Entry, out var session):
                Focus(action.Entry);
                var h = action.Hit!;
                switch (h.Kind)
                {
                    case HitKind.Focus:
                        session.FocusedInput = h.Target;
                        view.Invalidate(session.EntryId);
                        break;
                    case HitKind.Click:
                        SendEvent(session, new InputEvent { Type = "click", Target = h.Target });
                        break;
                    case HitKind.Select:
                        SendEvent(session, new InputEvent { Type = "select", Target = h.Target, Index = h.Index });
                        break;
                }
                break;
        }
    }

    void Focus(long? entryId)
    {
        if (focusedApp == entryId) return;
        focusedApp = entryId;
        view.InvalidateAll();
    }

    long? LiveSessionNearestBottom()
    {
        var live = sessions.Keys.Where(id => store.Get(id)?.Status == EntryStatus.Live).ToList();
        return live.Count == 0 ? null : live.Max();
    }

    AppSession? SessionForHost(long hostId) => sessions.Values.FirstOrDefault(s => s.HostId == hostId);

    int BackgroundCount => sessions.Keys.Count(id => store.Get(id)?.Status == EntryStatus.Suspended);

    // ---------------------------------------------------------------- commands and app lifecycle

    void Submit(string text)
    {
        text = text.Trim();
        scroll = 0;
        if (text.Length == 0) return;
        if (text is "exit" or "quit")
        {
            BeginQuit();
            return;
        }

        var entry = store.Append(EntryKind.Prompt, text, e =>
        {
            e.Status = EntryStatus.Running;
            e.Cwd = cwd;
        });
        if (host is null || !host.Send(new Message { Type = MessageTypes.Exec, Entry = entry.Id, Text = text, Width = ContentWidth }))
            Finish(entry, null, "The PowerShell host isn't running.");
    }

    void Finish(TimelineEntry entry, string? output, string? error)
    {
        if (!string.IsNullOrEmpty(output)) store.WriteText(entry.Id, TimelineStore.OutputFile, output);
        if (!string.IsNullOrEmpty(error)) store.WriteText(entry.Id, TimelineStore.ErrorFile, error);
        entry.Status = EntryStatus.Done;
        entry.Error = !string.IsNullOrEmpty(error);
        store.Update(entry);
        view.Invalidate(entry.Id);
    }

    void StartApp(string name, string appPath, long? resumedFrom, string? state)
    {
        if (!File.Exists(appPath))
        {
            Notify($"Can't start {name}: {appPath} doesn't exist.", error: true);
            return;
        }

        var entry = store.Append(EntryKind.App, name, e =>
        {
            e.Status = EntryStatus.Live;
            e.App = name;
            e.AppPath = appPath;
            e.ResumedFrom = resumedFrom;
        });
        sessions[entry.Id] = new AppSession(entry.Id, name, appPath);
        Focus(entry.Id);
        scroll = 0;

        if (host is null || !host.Send(new Message { Type = MessageTypes.Launch, Entry = entry.Id, Text = name, AppPath = appPath, State = state }))
            AppExited(entry.Id, null, "The PowerShell host isn't running.");
    }

    /// <summary>
    /// Brings an app back from history. A live app gets focus; a suspended one moves to the bottom of the
    /// timeline; a closed artifact is relaunched from its saved state as a new entry.
    /// </summary>
    void Resume(long id)
    {
        var entry = store.Get(id);

        // A suspended-then-resumed app left a frozen snapshot behind; follow it to where the app continued.
        for (int hops = 0; entry?.ContinuedIn is { } next && hops < 1000; hops++) entry = store.Get(next);

        if (entry is not { Kind: EntryKind.App, AppPath: not null })
        {
            Notify($"#{id} isn't an app artifact, so there's nothing to resume.", error: true);
            return;
        }
        if (sessions.TryGetValue(entry.Id, out var session))
        {
            if (entry.Status == EntryStatus.Suspended) MoveToBottom(session, entry);
            else Focus(entry.Id);
            return;
        }
        StartApp(entry.App ?? entry.Title, entry.AppPath, entry.Id, store.ReadText(entry.Id, TimelineStore.StateFile));
    }

    /// <summary>Sends a live app to the background. It keeps running (timers, background work) but loses focus.</summary>
    void Suspend(long id)
    {
        if (!sessions.TryGetValue(id, out var session) || session.Closing) return;
        if (store.Get(id) is not { Status: EntryStatus.Live } entry) return;

        entry.Status = EntryStatus.Suspended;
        store.Update(entry);
        if (focusedApp == id) Focus(null);
        view.Invalidate(id);
        SendEvent(session, new InputEvent { Type = "suspend" });
    }

    /// <summary>
    /// Resumes a suspended app at the bottom of the timeline. History stays append-only: the old entry is frozen
    /// as a snapshot pointing to the new one, and the running session moves to the new entry.
    /// </summary>
    void MoveToBottom(AppSession session, TimelineEntry old)
    {
        var entry = store.Append(EntryKind.App, session.Name, e =>
        {
            e.Status = EntryStatus.Live;
            e.App = session.Name;
            e.AppPath = session.AppPath;
            e.ResumedFrom = old.Id;
        });

        if (session.TreeJson is { } tree) store.WriteText(old.Id, TimelineStore.TreeFile, tree);
        old.Status = EntryStatus.Closed;
        old.ContinuedIn = entry.Id;
        store.Update(old);

        sessions.Remove(old.Id);
        session.EntryId = entry.Id;
        sessions[entry.Id] = session;
        view.Invalidate(old.Id);
        Focus(entry.Id);
        scroll = 0;
        SendEvent(session, new InputEvent { Type = "resume" });
    }

    void Close(long id)
    {
        if (!sessions.TryGetValue(id, out var session) || session.Closing) return;
        session.Closing = true;
        view.Invalidate(id);
        if (host is null || !host.Send(new Message { Type = MessageTypes.Close, Entry = session.HostId }))
            AppExited(id, null, null);
    }

    /// <summary>Freezes a live app into a history artifact: its last UI tree and its saved state.</summary>
    void AppExited(long id, string? state, string? error)
    {
        var entry = store.Get(id);
        sessions.Remove(id, out var session);
        if (entry is null) return;

        if (session?.TreeJson is { } tree) store.WriteText(id, TimelineStore.TreeFile, tree);
        if (!string.IsNullOrEmpty(state)) store.WriteText(id, TimelineStore.StateFile, state);
        if (!string.IsNullOrEmpty(error)) store.WriteText(id, TimelineStore.ErrorFile, error);
        entry.Status = EntryStatus.Closed;
        entry.Error = !string.IsNullOrEmpty(error);
        store.Update(entry);

        if (focusedApp == id) Focus(null);
        view.Invalidate(id);
        dirty = true;
    }

    void SendEvent(AppSession session, InputEvent input)
    {
        if (session.Closing) return;
        host?.Send(new Message { Type = MessageTypes.Input, Entry = session.HostId, Event = input });
        view.Invalidate(session.EntryId);
    }

    void Notify(string text, bool error = false, long? source = null)
    {
        store.Append(EntryKind.Notification, text, e =>
        {
            e.Error = error;
            e.Source = source;
        });
        dirty = true;
    }

    void BeginQuit()
    {
        if (quitDeadline is not null) return;
        quitDeadline = DateTime.Now + QuitTimeout;
        foreach (var id in sessions.Keys.ToList()) Close(id);
        if (sessions.Count == 0) stop = true;
    }

    // ---------------------------------------------------------------- host messages

    static readonly bool Trace = Environment.GetEnvironmentVariable("SCROLLOS_TRACE") is "1";

    void HandleHost(Message m)
    {
        dirty = true;
        if (Trace) TraceLine($"host: {m.Type} entry={m.Entry} {m.Text}");
        switch (m.Type)
        {
            case MessageTypes.Ready:
                hostReady = true;
                hostRestarts = 0;
                cwd ??= m.Cwd;
                break;

            case MessageTypes.Done when store.Get(m.Entry) is { } entry:
                if (m.Cwd is not null) cwd = m.Cwd;
                Finish(entry, m.Text, m.Error);
                break;

            case MessageTypes.Render when SessionForHost(m.Entry) is { } session:
                session.SetTree(m.Tree);
                session.Error = m.Error;
                view.Invalidate(session.EntryId);
                if (session.Tree is not null && session.PendingKeys.Count > 0)
                {
                    var pending = session.PendingKeys.ToList();
                    session.PendingKeys.Clear();
                    foreach (var key in pending) HandleAppKey(session, key);
                }
                break;

            case MessageTypes.Exited:
                AppExited(SessionForHost(m.Entry)?.EntryId ?? m.Entry, m.State, m.Error);
                break;

            case MessageTypes.LaunchRequest when m.Text is not null && m.AppPath is not null:
                StartApp(m.Text, m.AppPath, null, null);
                break;

            case MessageTypes.ResumeRequest:
                Resume(m.Target);
                break;

            case MessageTypes.QuitRequest:
                BeginQuit();
                break;

            case MessageTypes.Notify when m.Text is not null:
                var source = SessionForHost(m.Entry)?.EntryId;
                Notify(m.Text, m.Error is not null, source);
                if (source is not null) PlaySound(NotificationChime);
                break;

            case MessageTypes.Sound when m.Text is not null:
                PlaySound(m.Text);
                break;
        }
    }

    /// <summary>With SCROLLOS_TRACE=1, appends a diagnostic line to logs/core.log.</summary>
    void TraceLine(string line) => TraceLine(paths, line);

    static readonly object TraceLock = new();

    static void TraceLine(ScrollPaths paths, string line)
    {
        lock (TraceLock) // audio failures are reported from background threads
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(paths.CoreLog)!);
                File.AppendAllText(paths.CoreLog, $"{DateTimeOffset.Now:HH:mm:ss.fff} {line}\n");
            }
            catch (IOException) { }
        }
    }

    bool audioWarned;

    /// <summary>Plays a sound, or, the first time sound turns out to be unavailable, explains why in the timeline.</summary>
    void PlaySound(string sound)
    {
        if (Trace) TraceLine($"sound: {sound} via {audio.GetType().Name}, unavailable={audio.Unavailable ?? "no"}");
        if (audio.Unavailable is { } reason)
        {
            if (!audioWarned) Notify(reason, error: true);
            audioWarned = true;
            return;
        }
        audio.Play(sound);
    }

    void HandleHostExit()
    {
        hostReady = false;
        host?.Dispose();
        host = null;

        foreach (var id in sessions.Keys.ToList()) AppExited(id, null, "The PowerShell host stopped, so this app was closed without saving.");
        foreach (var e in store.Entries.Where(e => e.Status == EntryStatus.Running).ToList())
            Finish(e, null, "The PowerShell host stopped while this was running.");

        if (quitDeadline is not null)
        {
            stop = true;
            return;
        }
        if (hostRestarts++ < MaxHostRestarts)
        {
            Notify($"The PowerShell host stopped unexpectedly; restarting it. Details: {paths.HostLog}", error: true);
            StartHost();
        }
        else
        {
            hostFailed = true;
            Notify($"The PowerShell host keeps stopping. See {paths.HostLog}.", error: true);
        }
    }

    // ---------------------------------------------------------------- drawing

    int ContentWidth => Math.Max(10, size.W - 2);

    void Draw()
    {
        dirty = false;
        var (w, h) = size;
        if (w < 20 || h < 5)
        {
            terminal.Write("\x1b[2J\x1b[HTerminal too small for ScrollOS");
            renderer.Invalidate();
            return;
        }

        var buffer = new CellBuffer(w, h);
        hits.Clear();
        DrawStatusBar(buffer);
        var cursor = view.Draw(buffer, 1, 1, ContentWidth, h - 2, ref scroll, focusedApp, hits);
        var promptCursor = DrawPrompt(buffer);
        if (focusedApp is null) cursor = promptCursor;

        terminal.Write(renderer.Render(buffer, cursor));
    }

    void DrawStatusBar(CellBuffer b)
    {
        var bar = new Style(Bg: Theme.BarBg);
        b.Fill(0, 0, b.Width, 1, ' ', bar);
        int x = b.Write(1, 0, "ScrollOS", bar with { Fg = Theme.Accent, Bold = true });
        x = b.Write(x, 0, "  history is the desktop", bar with { Fg = Theme.Muted });
        if (BackgroundCount is var background and > 0)
            x = b.Write(x, 0, $"  ◐ {background} in background", bar with { Fg = Theme.Accent });
        if (scroll > 0) b.Write(x, 0, $"  ↑ {scroll} lines back · PgDn to return", bar with { Fg = Theme.Accent });

        clock = DateTime.Now.ToString("HH:mm");
        var hostState = hostReady ? "PowerShell ●" : hostFailed ? "PowerShell offline" : "PowerShell starting…";
        var right = $"{hostState}  {clock} ";
        b.Write(b.Width - right.Length, 0, right, bar with { Fg = hostReady ? Theme.Ok : Theme.Muted });
    }

    (int X, int Y) DrawPrompt(CellBuffer b)
    {
        int y = b.Height - 1;
        bool active = focusedApp is null;
        hits.Add(new UiHit(new Rect(0, y, b.Width, 1), new UiAction(UiActionKind.FocusPrompt)));

        string hint = !active ? "Tab: prompt · Ctrl+Z: background · Esc: close · Ctrl+Q: quit"
            : !hostReady ? "starting PowerShell…"
            : "";
        if (hint.Length > 0) b.Write(b.Width - hint.Length - 1, y, hint, new Style(Fg: Theme.Muted));

        int x = b.Write(1, y, $"PS {TimelineView.ShortPath(cwd)}> ", new Style(Fg: active ? Theme.Accent : Theme.Muted, Bold: true));
        int available = Math.Max(1, b.Width - x - (hint.Length > 0 ? hint.Length + 2 : 1));
        var text = editor.Text;
        int start = Math.Max(0, editor.Cursor - available + 1);
        var visible = text.Substring(start, Math.Min(available, text.Length - start));
        b.Write(x, y, visible, new Style(Fg: active ? 0 : Theme.Muted), available);
        return (x + editor.Cursor - start, y);
    }
}
