namespace Sos.Core.Input;

public enum Key
{
    Char, Enter, Backspace, Delete, Tab, BackTab, Escape,
    Up, Down, Left, Right, Home, End, PageUp, PageDown, Insert,
}

[Flags]
public enum Mods { None = 0, Shift = 1, Alt = 2, Ctrl = 4 }

public enum MouseKind { Down, Up, Move, Drag, WheelUp, WheelDown }

public enum MouseButton { None, Left, Middle, Right }

/// <summary>A terminal input event, already decoded from VT sequences.</summary>
public abstract record TermEvent;

public sealed record KeyEvent(Key Key, char Ch = '\0', Mods Mods = Mods.None) : TermEvent
{
    public bool IsCtrl(char letter) => Key == Key.Char && Mods.HasFlag(Mods.Ctrl) && char.ToLowerInvariant(Ch) == letter;

    /// <summary>Printable character with no Ctrl/Alt.</summary>
    public bool IsText => Key == Key.Char && (Mods & (Mods.Ctrl | Mods.Alt)) == 0;

    /// <summary>The name apps see in key events, e.g. "Up", "Delete", "Space", "a", "Ctrl+S".</summary>
    public string Name => Key == Key.Char
        ? (Mods.HasFlag(Mods.Ctrl) ? "Ctrl+" + char.ToUpperInvariant(Ch)
            : Mods.HasFlag(Mods.Alt) ? "Alt+" + Ch
            : Ch == ' ' ? "Space"
            : Ch.ToString())
        : Key.ToString();
}

/// <summary>A mouse event in 0-based cell coordinates.</summary>
public sealed record MouseEvent(MouseKind Kind, MouseButton Button, int X, int Y, Mods Mods = Mods.None) : TermEvent;
