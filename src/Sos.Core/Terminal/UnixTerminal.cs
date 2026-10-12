using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Sos.Core.Terminal;

/// <summary>
/// Raw-mode terminal for Linux (WSL today, Raspberry Pi later). Talks to libc directly instead of System.Console,
/// because .NET's console layer would otherwise line-buffer input and manage terminal settings itself.
/// termios is handled as an opaque buffer and set up with cfmakeraw, so no struct layout is hard-coded.
/// </summary>
[UnsupportedOSPlatform("windows")]
public sealed unsafe partial class UnixTerminal : ITerminal
{
    const int StdIn = 0, StdOut = 1;
    const int TcsaNow = 0;
    const int TermiosBufferSize = 256; // larger than struct termios on any libc
    const ulong TiocGWinSz = 0x5413;   // Linux, same value on x86-64 and arm64

    const string Enter = "\x1b[?1049h\x1b[?7l\x1b[?1000h\x1b[?1002h\x1b[?1003h\x1b[?1006h\x1b[2J";
    const string Leave = "\x1b[?1006l\x1b[?1003l\x1b[?1002l\x1b[?1000l\x1b[?7h\x1b[0m\x1b[?25h\x1b[?1049l";

    readonly byte[] original = new byte[TermiosBufferSize];
    bool disposed;

    UnixTerminal()
    {
        var raw = new byte[TermiosBufferSize];
        fixed (byte* o = original) tcgetattr(StdIn, o);
        Array.Copy(original, raw, raw.Length);
        fixed (byte* r = raw)
        {
            // No echo, no line buffering, no signal keys (Ctrl+C/Z/Q arrive as bytes), no output processing.
            cfmakeraw(r);
            tcsetattr(StdIn, TcsaNow, r);
        }
        Write(Enter);
    }

    /// <summary>Opens the terminal, or returns null with a reason when stdin/stdout aren't a terminal.</summary>
    public static UnixTerminal? TryOpen(out string? error)
    {
        if (isatty(StdIn) != 1 || isatty(StdOut) != 1)
        {
            error = "ScaleOS needs an interactive terminal (stdin/stdout must not be redirected).";
            return null;
        }
        var probe = new byte[TermiosBufferSize];
        fixed (byte* p = probe)
        {
            if (tcgetattr(StdIn, p) != 0)
            {
                error = $"Couldn't read terminal settings (errno {Marshal.GetLastPInvokeError()}).";
                return null;
            }
        }
        error = null;
        return new UnixTerminal();
    }

    public (int Width, int Height) Size
    {
        get
        {
            ushort* ws = stackalloc ushort[4]; // rows, cols, xpixel, ypixel
            return ioctl(StdOut, TiocGWinSz, ws) == 0 && ws[0] > 0 && ws[1] > 0 ? (ws[1], ws[0]) : (80, 24);
        }
    }

    public void Start(Action<string> onInput)
    {
        var thread = new Thread(() => ReadLoop(onInput)) { IsBackground = true, Name = "ScaleOS input" };
        thread.Start();
    }

    void ReadLoop(Action<string> onInput)
    {
        var decoder = new UTF8Encoding(false).GetDecoder(); // keeps partial multi-byte characters between reads
        var bytes = new byte[4096];
        var chars = new char[4096];
        while (!disposed)
        {
            nint n;
            fixed (byte* b = bytes) n = read(StdIn, b, (nuint)bytes.Length);
            if (n < 0 && Marshal.GetLastPInvokeError() == 4) continue; // EINTR
            if (n <= 0) break;
            int count = decoder.GetChars(bytes, 0, (int)n, chars, 0);
            if (count > 0) onInput(new string(chars, 0, count));
        }
    }

    public void Write(string vt)
    {
        if (vt.Length == 0) return;
        var bytes = Encoding.UTF8.GetBytes(vt);
        fixed (byte* b = bytes)
        {
            int offset = 0;
            while (offset < bytes.Length)
            {
                var written = write(StdOut, b + offset, (nuint)(bytes.Length - offset));
                if (written < 0 && Marshal.GetLastPInvokeError() == 4) continue; // EINTR
                if (written <= 0) return;
                offset += (int)written;
            }
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Write(Leave);
        fixed (byte* o = original) tcsetattr(StdIn, TcsaNow, o);
    }

    [LibraryImport("libc", SetLastError = true)]
    private static partial nint read(int fd, byte* buffer, nuint count);

    [LibraryImport("libc", SetLastError = true)]
    private static partial nint write(int fd, byte* buffer, nuint count);

    [LibraryImport("libc", SetLastError = true)]
    private static partial int isatty(int fd);

    [LibraryImport("libc", SetLastError = true)]
    private static partial int tcgetattr(int fd, byte* termios);

    [LibraryImport("libc", SetLastError = true)]
    private static partial int tcsetattr(int fd, int optionalActions, byte* termios);

    [LibraryImport("libc")]
    private static partial void cfmakeraw(byte* termios);

    [LibraryImport("libc", SetLastError = true)]
    private static partial int ioctl(int fd, ulong request, ushort* winsize);
}
