using System.Collections;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Threading.Channels;
using Sos.Protocol;

namespace Sos.Host;

/// <summary>A runspace plus a queue of messages processed one at a time.</summary>
public abstract class Session(SosHost host)
{
    readonly Channel<Message> queue = Channel.CreateUnbounded<Message>(new() { SingleReader = true });

    protected SosHost Host { get; } = host;
    protected Runspace? Runspace { get; set; }

    public void Post(Message m) => queue.Writer.TryWrite(m);

    public void Start() => _ = Task.Run(async () =>
    {
        try
        {
            await foreach (var m in queue.Reader.ReadAllAsync())
            {
                if (!Handle(m)) break;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{GetType().Name} crashed: {ex}");
        }
        finally
        {
            Runspace?.Dispose();
        }
    });

    /// <summary>Handles one message; returns false to end the session.</summary>
    protected abstract bool Handle(Message m);

    protected Runspace CreateRunspace(CoreBridge? bridge)
    {
        var iss = InitialSessionState.CreateDefault2();
        if (OperatingSystem.IsWindows()) iss.ExecutionPolicy = Microsoft.PowerShell.ExecutionPolicy.Bypass;
        if (File.Exists(Host.SdkPath)) iss.ImportPSModule(Host.SdkPath);
        if (bridge is not null) iss.Variables.Add(new SessionStateVariableEntry("Sos", bridge, "Connection to the ScaleOS core"));

        var rs = RunspaceFactory.CreateRunspace(iss);
        rs.Open();
        return rs;
    }

    /// <summary>Runs a pipeline built by <paramref name="build"/>. Returns its output and the first error, if any.</summary>
    protected (List<PSObject> Output, string? Error) Run(Action<PowerShell> build)
    {
        using var ps = PowerShell.Create();
        ps.Runspace = Runspace;
        build(ps);
        try
        {
            var output = ps.Invoke().Where(o => o is not null).ToList();
            var error = ps.Streams.Error.Count > 0 ? Describe(ps.Streams.Error[0]) : null;
            return (output, error);
        }
        catch (Exception ex)
        {
            return ([], ex is RuntimeException re ? Describe(re.ErrorRecord) : ex.Message);
        }
    }

    protected static string Describe(ErrorRecord record)
    {
        var position = record.InvocationInfo?.PositionMessage;
        return string.IsNullOrWhiteSpace(position) ? record.ToString() : $"{record}\n{position}";
    }

    protected string Cwd()
    {
        try { return Runspace?.SessionStateProxy.Path.CurrentLocation.Path ?? ""; }
        catch (Exception) { return ""; }
    }
}

/// <summary>The interactive shell: runs prompt commands in one long-lived runspace so variables and cd persist.</summary>
public sealed class ShellSession(SosHost host) : Session(host)
{
    bool ready;

    protected override bool Handle(Message m)
    {
        EnsureReady();
        if (m.Type != MessageTypes.Exec) return true;

        using var ps = PowerShell.Create();
        ps.Runspace = Runspace;
        ps.AddScript(m.Text ?? "").AddCommand("Out-String").AddParameter("Width", Math.Max(40, m.Width));

        string output = "";
        var errors = new List<string>();
        try
        {
            output = string.Concat(ps.Invoke().Select(o => o?.ToString()));
        }
        catch (Exception ex)
        {
            errors.Add(ex is RuntimeException re ? Describe(re.ErrorRecord) : ex.Message);
        }
        errors.AddRange(ps.Streams.Error.Select(e => e.ToString()));

        // Write-Host and warnings arrive on their own streams; show them before the pipeline output.
        var extra = ps.Streams.Information.Select(i => i.MessageData?.ToString())
            .Concat(ps.Streams.Warning.Select(w => "WARNING: " + w.Message));
        var text = string.Join("\n", extra.Append(output.Replace("\r\n", "\n").Trim('\n')).Where(s => !string.IsNullOrEmpty(s)));

        Host.Send(new Message
        {
            Type = MessageTypes.Done,
            Entry = m.Entry,
            Text = text,
            Error = errors.Count > 0 ? string.Join("\n", errors) : null,
            Cwd = Cwd(),
        });
        return true;
    }

    void EnsureReady()
    {
        if (ready) return;
        ready = true;
        Runspace = CreateRunspace(new CoreBridge(Host));
        Run(ps => ps.AddCommand("Register-SosApps"));
        Host.Send(new Message { Type = MessageTypes.Ready, Cwd = Cwd() });
    }
}

/// <summary>
/// One running app: its own runspace with the app module imported. Implements the app contract:
/// Start-App, Restore-AppState, Show-App, Invoke-AppInput, Save-AppState (all but Show-App optional).
/// </summary>
public sealed class AppSession(SosHost host, long entry) : Session(host)
{
    const string RenderScript = """
        $w = @(Show-App)
        $root = if ($w.Count -eq 1) { $w[0] } else { [ordered]@{ type = 'column'; children = $w } }
        ConvertTo-Json -InputObject $root -Depth 64 -Compress
        """;

    const string StartScript = """
        param($StateJson)
        if ($StateJson -and (Get-Command Restore-AppState -ErrorAction Ignore)) {
            Restore-AppState -State (ConvertFrom-Json -InputObject $StateJson -AsHashtable)
        } elseif (Get-Command Start-App -ErrorAction Ignore) {
            Start-App
        }
        """;

    const string InputScript = """
        param($InputEvent)
        if (Get-Command Invoke-AppInput -ErrorAction Ignore) { Invoke-AppInput $InputEvent }
        """;

    const string SaveScript = """
        if (Get-Command Save-AppState -ErrorAction Ignore) {
            $s = Save-AppState
            if ($null -ne $s) { ConvertTo-Json -InputObject $s -Depth 64 -Compress }
        }
        """;

    const string TickType = "tick";

    Timer? timer;
    int tickPending;
    string? lastTree, lastError;

    public long Entry => entry;

    protected override bool Handle(Message m)
    {
        switch (m.Type)
        {
            case MessageTypes.Launch:
                return Launch(m);
            case MessageTypes.Input:
                return Dispatch(ToHashtable(m.Event));
            case TickType:
                Interlocked.Exchange(ref tickPending, 0);
                return Dispatch(new Hashtable(StringComparer.OrdinalIgnoreCase) { ["type"] = TickType });
            case MessageTypes.Close:
                return Exit(null);
            default:
                return true;
        }
    }

    bool Dispatch(Hashtable inputEvent)
    {
        var (output, error) = Run(ps => ps.AddScript(InputScript).AddArgument(inputEvent));
        if (output.Any(o => o.BaseObject is string s && s.Equals("exit", StringComparison.OrdinalIgnoreCase)))
            return Exit(error);
        Render(error);
        return true;
    }

    /// <summary>
    /// Delivers a 'tick' event every <paramref name="milliseconds"/> (0 stops). Ticks keep coming while the app
    /// is suspended, which is how apps do background work. Ticks don't pile up behind a slow handler.
    /// </summary>
    public void SetTimer(int milliseconds)
    {
        timer?.Dispose();
        timer = milliseconds <= 0 ? null : new Timer(_ =>
        {
            if (Interlocked.Exchange(ref tickPending, 1) == 0) Post(new Message { Type = TickType });
        }, null, milliseconds, milliseconds);
    }

    bool Launch(Message m)
    {
        try
        {
            Runspace = CreateRunspace(new CoreBridge(Host, this));
        }
        catch (Exception ex)
        {
            return Exit($"Couldn't create a PowerShell runspace: {ex.Message}", save: false);
        }

        var (_, importError) = Run(ps => ps.AddCommand("Import-Module").AddParameter("Name", m.AppPath).AddParameter("Global").AddParameter("Force"));
        if (importError is not null) return Exit($"Couldn't load {m.AppPath}: {importError}", save: false);

        var (_, startError) = Run(ps => ps.AddScript(StartScript).AddArgument(m.State));
        Render(startError);
        return true;
    }

    void Render(string? priorError)
    {
        var (output, error) = Run(ps => ps.AddScript(RenderScript));
        var tree = output.LastOrDefault()?.BaseObject as string;
        error = priorError ?? error;

        // Timer ticks often don't change anything; only send real updates.
        if (tree == lastTree && error == lastError) return;
        lastTree = tree;
        lastError = error;
        Host.Send(new Message { Type = MessageTypes.Render, Entry = entry, Tree = tree, Error = error });
    }

    bool Exit(string? error, bool save = true)
    {
        SetTimer(0);
        string? state = null;
        if (save)
        {
            var (output, saveError) = Run(ps => ps.AddScript(SaveScript));
            state = output.LastOrDefault()?.BaseObject as string;
            error ??= saveError;
        }
        Host.Send(new Message { Type = MessageTypes.Exited, Entry = entry, State = state, Error = error });
        Host.Remove(entry);
        return false;
    }

    static Hashtable ToHashtable(InputEvent? e) => new(StringComparer.OrdinalIgnoreCase)
    {
        ["type"] = e?.Type,
        ["target"] = e?.Target,
        ["index"] = e?.Index ?? -1,
        ["value"] = e?.Value,
        ["key"] = e?.Key,
    };
}

/// <summary>
/// Exposed to every runspace as <c>$Sos</c> so SDK commands can ask the core to do things.
/// <paramref name="app"/> is null in the shell runspace.
/// </summary>
public sealed class CoreBridge(SosHost host, AppSession? app = null)
{
    public bool IsShell => app is null;

    public void Launch(string name, string appPath) =>
        host.Send(new Message { Type = MessageTypes.LaunchRequest, Text = name, AppPath = appPath });

    public void Resume(long id) => host.Send(new Message { Type = MessageTypes.ResumeRequest, Target = id });

    public void Quit() => host.Send(new Message { Type = MessageTypes.QuitRequest });

    /// <summary>Adds a notification to the timeline. From an app, clicking it opens that app.</summary>
    public void Notify(string text) =>
        host.Send(new Message { Type = MessageTypes.Notify, Text = text, Entry = app?.Entry ?? 0 });

    /// <summary>Asks the core to play a .wav path or a tone spec ("tone:440/80,660/80").</summary>
    public void PlaySound(string sound) => host.Send(new Message { Type = MessageTypes.Sound, Text = sound });

    public void SetTimer(int milliseconds)
    {
        if (app is null) throw new InvalidOperationException("Timers are only available to apps.");
        app.SetTimer(milliseconds);
    }
}
