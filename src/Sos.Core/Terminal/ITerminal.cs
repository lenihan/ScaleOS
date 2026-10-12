namespace Sos.Core.Terminal;

/// <summary>
/// The terminal ScaleOS draws into. Windows is the only implementation for now;
/// a termios-based one can be added for Linux later.
/// </summary>
public interface ITerminal : IDisposable
{
    (int Width, int Height) Size { get; }

    /// <summary>Starts reading input on a background thread; raw text chunks are passed to <paramref name="onInput"/>.</summary>
    void Start(Action<string> onInput);

    void Write(string vt);
}
