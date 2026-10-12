using System.Collections.Concurrent;
using System.Text;
using Sos.Protocol;

namespace Sos.Host;

/// <summary>
/// Reads messages from the core and dispatches them to sessions. The shell and each app get their own
/// worker, so a slow command never blocks an app (and vice versa), while messages for one session stay in order.
/// </summary>
public sealed class SosHost(Stream protocolOut, string sdkPath, string appsDir)
{
    readonly StreamWriter writer = new(protocolOut, new UTF8Encoding(false)) { AutoFlush = true };
    readonly object writeLock = new();
    readonly ConcurrentDictionary<long, AppSession> apps = new();

    public string SdkPath { get; } = sdkPath;
    public string AppsDir { get; } = appsDir;

    public async Task<int> RunAsync(Stream input)
    {
        var shell = new ShellSession(this);
        shell.Start();
        // Open the shell runspace right away rather than on the first command; it reports "ready" when done.
        shell.Post(new Message { Type = "init" });

        using var reader = new StreamReader(input, new UTF8Encoding(false));
        while (await reader.ReadLineAsync() is { } line)
        {
            Message? m;
            try { m = Wire.Deserialize(line); }
            catch (System.Text.Json.JsonException ex)
            {
                Console.Error.WriteLine($"Bad message from core: {ex.Message}");
                continue;
            }
            if (m is null) continue;

            switch (m.Type)
            {
                case MessageTypes.Exec:
                    shell.Post(m);
                    break;
                case MessageTypes.Launch:
                    var session = new AppSession(this, m.Entry);
                    if (apps.TryAdd(m.Entry, session))
                    {
                        session.Start();
                        session.Post(m);
                    }
                    break;
                case MessageTypes.Input or MessageTypes.Close:
                    if (apps.TryGetValue(m.Entry, out var app)) app.Post(m);
                    else if (m.Type == MessageTypes.Close) Send(new Message { Type = MessageTypes.Exited, Entry = m.Entry });
                    break;
            }
        }

        // The core closed our stdin: it is shutting down.
        return 0;
    }

    internal void Remove(long entry) => apps.TryRemove(entry, out _);

    public void Send(Message message)
    {
        var line = Wire.Serialize(message);
        lock (writeLock) writer.WriteLine(line);
    }
}
