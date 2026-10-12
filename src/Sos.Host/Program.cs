using Sos.Host;

// The protocol owns stdout; anything else that writes to Console.Out goes to stderr (the host log).
var protocolOut = Console.OpenStandardOutput();
Console.SetOut(Console.Error);

UseBundledModules();

var host = new SosHost(
    protocolOut,
    Environment.GetEnvironmentVariable("SOS_SDK") ?? "",
    Environment.GetEnvironmentVariable("SOS_APPS") ?? "");
return await host.RunAsync(Console.OpenStandardInput());

// PowerShell's built-in modules (Microsoft.PowerShell.Utility for Out-String, ConvertTo-Json, ...) ship inside
// the SDK package under runtimes/<os>/lib/<tfm>/Modules, but PowerShell only looks in <app>/Modules. Put the
// bundled folder first on PSModulePath so the host never depends on an installed pwsh (a Pi won't have one).
static void UseBundledModules()
{
    var baseDir = AppContext.BaseDirectory;
    var os = OperatingSystem.IsWindows() ? "win" : "unix";
    var candidates = new List<string> { Path.Combine(baseDir, "Modules") };
    var lib = Path.Combine(baseDir, "runtimes", os, "lib");
    if (Directory.Exists(lib))
        candidates.AddRange(Directory.GetDirectories(lib).Select(tfm => Path.Combine(tfm, "Modules")));

    var modules = candidates.FirstOrDefault(Directory.Exists);
    if (modules is null) return;
    var existing = Environment.GetEnvironmentVariable("PSModulePath");
    Environment.SetEnvironmentVariable("PSModulePath",
        string.IsNullOrEmpty(existing) ? modules : modules + Path.PathSeparator + existing);
}
