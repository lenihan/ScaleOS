namespace ScrollOS.Core.Lifecycle;

/// <summary>Where ScrollOS keeps its data and finds the host, SDK module and apps.</summary>
public sealed record ScrollPaths(string Home, string AppsDir, string SdkPath, string HostExe)
{
    public string HostLog => Path.Combine(Home, "logs", "host.log");
    public string CoreLog => Path.Combine(Home, "logs", "core.log");

    /// <summary>
    /// Data lives in <c>--home</c>, <c>%SCROLLOS_HOME%</c>, or <c>%USERPROFILE%\.scrollos</c>.
    /// When running from a repo checkout, apps and the SDK are read from the source tree so edits
    /// to .psm1 files take effect without rebuilding.
    /// </summary>
    public static ScrollPaths Resolve(string? homeOverride)
    {
        var home = homeOverride
            ?? Environment.GetEnvironmentVariable("SCROLLOS_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".scrollos");
        home = Path.GetFullPath(home);

        var baseDir = AppContext.BaseDirectory;
        var repo = FindRepoRoot(baseDir);
        var hostExe = Path.Combine(baseDir, "host", OperatingSystem.IsWindows() ? "scrollos-host.exe" : "scrollos-host");

        return repo is null
            ? new ScrollPaths(home, Path.Combine(baseDir, "apps"), Path.Combine(baseDir, "sdk", "ScrollOS.Sdk.psm1"), hostExe)
            : new ScrollPaths(home, Path.Combine(repo, "apps"), Path.Combine(repo, "src", "ScrollOS.Sdk", "ScrollOS.Sdk.psm1"), hostExe);
    }

    static string? FindRepoRoot(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "ScrollOS.sln"))) return dir.FullName;
        return null;
    }
}
