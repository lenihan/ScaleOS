using Sos.Core;
using Sos.Core.Lifecycle;
using Sos.Core.Terminal;

string? home = null;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] is "--home" && i + 1 < args.Length) home = args[++i];
    else if (args[i] is "-h" or "--help")
    {
        Console.WriteLine("Usage: sos [--home <dir>]");
        Console.WriteLine("  --home  where the timeline is stored (default: %USERPROFILE%\\.sos)");
        return 0;
    }
}

string? error = null;
ITerminal? terminal =
    OperatingSystem.IsWindows() ? WindowsConsole.TryOpen(out error)
    : OperatingSystem.IsLinux() ? UnixTerminal.TryOpen(out error)
    : null;
error ??= terminal is null ? "ScaleOS runs on Windows and Linux." : null;
if (terminal is null)
{
    Console.Error.WriteLine(error);
    return 1;
}

var paths = SosPaths.Resolve(home);
try
{
    return await new SosApp(terminal, paths).RunAsync();
}
catch (Exception ex)
{
    terminal.Dispose();
    Console.Error.WriteLine($"ScaleOS crashed: {ex}");
    return 1;
}
finally
{
    terminal.Dispose();
}
