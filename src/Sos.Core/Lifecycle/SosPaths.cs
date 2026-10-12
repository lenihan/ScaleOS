namespace Sos.Core.Lifecycle;

/// <summary>Where ScaleOS keeps its data and finds the host, SDK module and apps.</summary>
public sealed record SosPaths(string Home, string AppsDir, string SdkPath, string HostExe)
{
    public string HostLog => Path.Combine(Home, "logs", "host.log");
    public string CoreLog => Path.Combine(Home, "logs", "core.log");

    /// <summary>
    /// Data lives in <c>--home</c>, <c>%SOS_HOME%</c>, or <c>%USERPROFILE%\.sos</c>.
    /// When running from a repo checkout, apps and the SDK are read from the source tree so edits
    /// to .psm1 files take effect without rebuilding.
    /// </summary>
    public static SosPaths Resolve(string? homeOverride)
    {
        var home = homeOverride ?? Environment.GetEnvironmentVariable("SOS_HOME");
        if (home is null)
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            home = Path.Combine(profile, ".sos");
            MigrateLegacyHome(Path.Combine(profile, ".scrollos"), home);
        }
        home = Path.GetFullPath(home);

        var baseDir = AppContext.BaseDirectory;
        var repo = FindRepoRoot(baseDir);
        var hostExe = Path.Combine(baseDir, "host", OperatingSystem.IsWindows() ? "sos-host.exe" : "sos-host");

        return repo is null
            ? new SosPaths(home, Path.Combine(baseDir, "apps"), Path.Combine(baseDir, "sdk", "Sos.Sdk.psm1"), hostExe)
            : new SosPaths(home, Path.Combine(repo, "apps"), Path.Combine(repo, "src", "Sos.Sdk", "Sos.Sdk.psm1"), hostExe);
    }

    /// <summary>
    /// ScaleOS used to be called ScrollOS and kept its data in ~/.scrollos. Move that folder to the new location once,
    /// so history carries over. Returns true if it moved.
    /// </summary>
    public static bool MigrateLegacyHome(string legacy, string home)
    {
        if (Directory.Exists(home) || !Directory.Exists(legacy)) return false;
        try
        {
            Directory.Move(legacy, home);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false; // e.g. an old copy still running; the old folder is simply left alone
        }
    }

    static string? FindRepoRoot(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Sos.sln"))) return dir.FullName;
        return null;
    }
}
