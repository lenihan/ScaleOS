using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Sos.Core.Audio;

/// <summary>
/// The runtime's audio engine. Apps ask for sounds through the host; the core plays them.
/// A sound is a .wav path or a tone spec such as "tone:440/80,0/40,660/120" ("sine:" for a softer sine wave).
/// </summary>
public interface IAudio
{
    void Play(string sound);

    /// <summary>Why sounds can't play on this machine, or null if they can (or are deliberately muted).</summary>
    string? Unavailable => null;
}

public sealed class NullAudio : IAudio
{
    public void Play(string sound) { }
}

/// <summary>Plays through winmm's PlaySound. One sound at a time: a new sound cuts off the previous one.</summary>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsAudio(string cacheDir) : IAudio
{
    const uint SndAsync = 0x1, SndNoDefault = 0x2, SndFilename = 0x20000;

    public void Play(string sound)
    {
        try
        {
            var path = ToneSynth.IsTone(sound) ? ToneSynth.CachedWav(sound, cacheDir) : sound;
            if (path is not null && File.Exists(path)) PlaySoundW(path, 0, SndFilename | SndAsync | SndNoDefault);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    [LibraryImport("winmm.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PlaySoundW(string sound, nint module, uint flags);
}

/// <summary>
/// Plays through an external player: paplay (PulseAudio, e.g. WSLg) or aplay (ALSA, e.g. Raspberry Pi OS Lite).
/// Silent if neither is installed. One sound at a time, like <see cref="WindowsAudio"/>.
/// </summary>
[UnsupportedOSPlatform("windows")]
public sealed class LinuxAudio(string cacheDir, Action<string>? log = null) : IAudio
{
    readonly string? player = FindPlayer();

    public string? Unavailable => player is not null ? null
        : "No sound: install an audio player. On WSL: sudo apt install pulseaudio-utils. " +
          "On Raspberry Pi OS: sudo apt install alsa-utils.";
    System.Diagnostics.Process? current;

    static string? FindPlayer()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var name in new[] { "paplay", "aplay" })
            foreach (var dir in path.Split(':', StringSplitOptions.RemoveEmptyEntries))
                if (File.Exists(Path.Combine(dir, name))) return Path.Combine(dir, name);
        return null;
    }

    public void Play(string sound)
    {
        if (player is null) return;
        try
        {
            var path = ToneSynth.IsTone(sound) ? ToneSynth.CachedWav(sound, cacheDir) : sound;
            if (path is null || !File.Exists(path)) return;

            try { if (current is { HasExited: false }) current.Kill(); } catch (InvalidOperationException) { }
            current?.Dispose();

            // Redirect everything so the player never touches the terminal ScaleOS is drawing on.
            var psi = new System.Diagnostics.ProcessStartInfo(player)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var arg in PlayerArguments(player, path)) psi.ArgumentList.Add(arg);
            var process = System.Diagnostics.Process.Start(psi);
            current = process;
            if (process is not null && log is not null) _ = ReportFailureAsync(process);
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            log?.Invoke($"{player} couldn't start: {ex.Message}");
        }
    }

    /// <summary>Command-line arguments for playing <paramref name="wav"/> with the given player executable.</summary>
    public static string[] PlayerArguments(string player, string wav) =>
        Path.GetFileName(player) == "aplay" ? ["-q", wav] : [wav]; // only aplay has -q; "paplay" also ends in "aplay"

    /// <summary>Logs the player's exit code and error output when it fails (only when a log is attached).</summary>
    async Task ReportFailureAsync(System.Diagnostics.Process process)
    {
        try
        {
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0 || error.Length > 0)
                log?.Invoke($"{player} exited with {process.ExitCode}: {error.Trim()}");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException) { } // killed by the next sound
    }
}

/// <summary>Turns tone specs into 16-bit mono PCM WAV files.</summary>
public static class ToneSynth
{
    const int SampleRate = 22050;
    const double Volume = 0.18;
    const int MaxNotes = 64;
    const int MaxNoteMs = 5000;

    public static bool IsTone(string sound) => sound.StartsWith("tone:", StringComparison.Ordinal) || sound.StartsWith("sine:", StringComparison.Ordinal);

    /// <summary>Parses "tone:440/80,660/80" into (frequency, milliseconds) notes; frequency 0 is a rest.</summary>
    public static List<(double Hz, int Ms)>? Parse(string spec)
    {
        if (!IsTone(spec)) return null;
        var notes = new List<(double, int)>();
        foreach (var part in spec[5..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pieces = part.Split('/');
            if (pieces.Length != 2
                || !double.TryParse(pieces[0], System.Globalization.CultureInfo.InvariantCulture, out var hz)
                || !int.TryParse(pieces[1], out var ms)
                || hz < 0 || hz > 20000 || ms <= 0 || ms > MaxNoteMs)
                return null;
            notes.Add((hz, ms));
            if (notes.Count > MaxNotes) return null;
        }
        return notes.Count > 0 ? notes : null;
    }

    public static byte[]? Wav(string spec)
    {
        var notes = Parse(spec);
        if (notes is null) return null;
        bool sine = spec.StartsWith("sine:", StringComparison.Ordinal);

        var samples = new List<short>();
        foreach (var (hz, ms) in notes)
        {
            int count = SampleRate * ms / 1000;
            int fade = Math.Min(count / 2, SampleRate * 3 / 1000); // 3ms ramps avoid clicks between notes
            for (int i = 0; i < count; i++)
            {
                double value = 0;
                if (hz > 0)
                {
                    double phase = Math.Sin(2 * Math.PI * hz * i / SampleRate);
                    value = sine ? phase : Math.Sign(phase) * 0.6;
                }
                double envelope = Math.Min(1.0, Math.Min((double)i / Math.Max(1, fade), (double)(count - i) / Math.Max(1, fade)));
                samples.Add((short)(value * envelope * Volume * short.MaxValue));
            }
        }

        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        int dataBytes = samples.Count * 2;
        w.Write("RIFF"u8);
        w.Write(36 + dataBytes);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);                 // fmt chunk size
        w.Write((short)1);           // PCM
        w.Write((short)1);           // mono
        w.Write(SampleRate);
        w.Write(SampleRate * 2);     // byte rate
        w.Write((short)2);           // block align
        w.Write((short)16);          // bits per sample
        w.Write("data"u8);
        w.Write(dataBytes);
        foreach (var s in samples) w.Write(s);
        w.Flush();
        return stream.ToArray();
    }

    /// <summary>Returns the path of a cached WAV for the spec, synthesizing it on first use; null if the spec is invalid.</summary>
    public static string? CachedWav(string spec, string cacheDir)
    {
        var name = new StringBuilder();
        foreach (var ch in spec) name.Append(char.IsLetterOrDigit(ch) || ch == '.' ? ch : '_');
        if (name.Length > 150) return null;
        var path = Path.Combine(cacheDir, name + ".wav");
        if (File.Exists(path)) return path;

        var wav = Wav(spec);
        if (wav is null) return null;
        Directory.CreateDirectory(cacheDir);
        File.WriteAllBytes(path, wav);
        return path;
    }
}
