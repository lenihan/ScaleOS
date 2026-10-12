using System.Diagnostics;
using System.Text;
using Sos.Protocol;

namespace Sos.Core.Lifecycle;

/// <summary>
/// Runs sos-host.exe (the PowerShell host) as a child process and exchanges JSON-lines messages with it
/// over stdin/stdout. Host stderr goes to a log file.
/// </summary>
public sealed class HostClient(SosPaths paths) : IDisposable
{
    readonly object writeLock = new();
    readonly object logLock = new();
    Process? process;

    public bool IsRunning => process is { HasExited: false };

    /// <summary>Starts the host. <paramref name="onMessage"/> and <paramref name="onExit"/> run on a background thread.</summary>
    public void Start(Action<Message> onMessage, Action onExit)
    {
        if (!File.Exists(paths.HostExe))
            throw new FileNotFoundException($"PowerShell host not found at {paths.HostExe}. Build the solution first.");

        var utf8 = new UTF8Encoding(false);
        var psi = new ProcessStartInfo(paths.HostExe)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = utf8,
            StandardOutputEncoding = utf8,
            StandardErrorEncoding = utf8,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };
        psi.Environment["SOS_HOME"] = paths.Home;
        psi.Environment["SOS_APPS"] = paths.AppsDir;
        psi.Environment["SOS_SDK"] = paths.SdkPath;

        var p = Process.Start(psi) ?? throw new InvalidOperationException("Could not start the PowerShell host.");
        process = p;

        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) Log(e.Data); };
        p.BeginErrorReadLine();

        var reader = new Thread(() =>
        {
            try
            {
                while (p.StandardOutput.ReadLine() is { } line)
                {
                    Message? message = null;
                    try { message = Wire.Deserialize(line); }
                    catch (System.Text.Json.JsonException) { Log("Unparseable host message: " + line); }
                    if (message is not null) onMessage(message);
                }
            }
            catch (IOException) { }
            onExit();
        }) { IsBackground = true, Name = "ScaleOS host reader" };
        reader.Start();
    }

    public bool Send(Message message)
    {
        var p = process;
        if (p is null || p.HasExited) return false;
        try
        {
            lock (writeLock)
            {
                p.StandardInput.WriteLine(Wire.Serialize(message));
                p.StandardInput.Flush();
            }
            return true;
        }
        catch (IOException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    void Log(string line)
    {
        lock (logLock)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(paths.HostLog)!);
                File.AppendAllText(paths.HostLog, $"{DateTimeOffset.Now:O} {line}\n");
            }
            catch (IOException) { }
        }
    }

    public void Dispose()
    {
        var p = process;
        process = null;
        if (p is null) return;
        try
        {
            // Closing stdin asks the host to exit; kill it if it doesn't.
            p.StandardInput.Close();
            if (!p.WaitForExit(2000)) p.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException) { }
        p.Dispose();
    }
}
