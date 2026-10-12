using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Sos.Core.Terminal;

/// <summary>
/// Puts the Windows console into raw VT mode: VT output, VT input (so keys and mouse arrive as escape
/// sequences), the alternate screen, and SGR mouse reporting. Restores everything on dispose.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsConsole : ITerminal
{
    const int StdInput = -10, StdOutput = -11;

    const uint EnableProcessedInput = 0x1, EnableLineInput = 0x2, EnableEchoInput = 0x4,
        EnableMouseInput = 0x10, EnableQuickEditMode = 0x40, EnableExtendedFlags = 0x80,
        EnableVirtualTerminalInput = 0x200;

    const uint EnableProcessedOutput = 0x1, EnableVirtualTerminalProcessing = 0x4, DisableNewlineAutoReturn = 0x8;

    // Alternate screen, no autowrap (so drawing the last column never scrolls), mouse: press, drag, any-motion, SGR encoding.
    const string Enter = "\x1b[?1049h\x1b[?7l\x1b[?1000h\x1b[?1002h\x1b[?1003h\x1b[?1006h\x1b[2J";
    const string Leave = "\x1b[?1006l\x1b[?1003l\x1b[?1002l\x1b[?1000l\x1b[?7h\x1b[0m\x1b[?25h\x1b[?1049l";

    readonly nint input, output;
    readonly uint originalInputMode, originalOutputMode;
    readonly Encoding originalInputEncoding, originalOutputEncoding;
    readonly Stream stdout;
    bool disposed;

    WindowsConsole(nint input, nint output, uint inputMode, uint outputMode)
    {
        this.input = input;
        this.output = output;
        originalInputMode = inputMode;
        originalOutputMode = outputMode;
        originalInputEncoding = Console.InputEncoding;
        originalOutputEncoding = Console.OutputEncoding;

        Console.InputEncoding = new UTF8Encoding(false);
        Console.OutputEncoding = new UTF8Encoding(false);

        uint rawIn = (inputMode | EnableVirtualTerminalInput | EnableExtendedFlags)
            & ~(EnableProcessedInput | EnableLineInput | EnableEchoInput | EnableQuickEditMode | EnableMouseInput);
        SetConsoleMode(input, rawIn);
        SetConsoleMode(output, outputMode | EnableProcessedOutput | EnableVirtualTerminalProcessing | DisableNewlineAutoReturn);

        stdout = Console.OpenStandardOutput();
        Write(Enter);
    }

    /// <summary>Opens the console, or returns null with a reason when stdin/stdout aren't an interactive console.</summary>
    public static WindowsConsole? TryOpen(out string? error)
    {
        var input = GetStdHandle(StdInput);
        var output = GetStdHandle(StdOutput);
        if (!GetConsoleMode(input, out var inMode) || !GetConsoleMode(output, out var outMode))
        {
            error = "ScaleOS needs an interactive console (stdin/stdout must not be redirected).";
            return null;
        }
        error = null;
        return new WindowsConsole(input, output, inMode, outMode);
    }

    public (int Width, int Height) Size
    {
        get
        {
            try { return (Console.WindowWidth, Console.WindowHeight); }
            catch (IOException) { return (80, 24); }
        }
    }

    public void Start(Action<string> onInput)
    {
        var thread = new Thread(() => ReadLoop(onInput)) { IsBackground = true, Name = "ScaleOS input" };
        thread.Start();
    }

    unsafe void ReadLoop(Action<string> onInput)
    {
        const int size = 4096;
        var buffer = new char[size];
        while (!disposed)
        {
            uint read;
            bool ok;
            fixed (char* p = buffer) ok = ReadConsoleW(input, p, size, out read, 0);
            if (!ok) break;
            if (read > 0) onInput(new string(buffer, 0, (int)read));
        }
    }

    public void Write(string vt)
    {
        if (vt.Length == 0) return;
        var bytes = Encoding.UTF8.GetBytes(vt);
        stdout.Write(bytes, 0, bytes.Length);
        stdout.Flush();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { Write(Leave); } catch (IOException) { }
        SetConsoleMode(input, originalInputMode);
        SetConsoleMode(output, originalOutputMode);
        Console.InputEncoding = originalInputEncoding;
        Console.OutputEncoding = originalOutputEncoding;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GetStdHandle(int handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetConsoleMode(nint handle, out uint mode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetConsoleMode(nint handle, uint mode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool ReadConsoleW(nint handle, char* buffer, uint toRead, out uint read, nint control);
}
