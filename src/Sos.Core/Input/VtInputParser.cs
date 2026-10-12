using System.Text;

namespace Sos.Core.Input;

/// <summary>
/// Decodes VT input (keys, CSI sequences, SGR-1006 mouse) into <see cref="TermEvent"/>s.
/// Incomplete sequences at the end of a chunk are kept until the next chunk arrives.
/// </summary>
public sealed class VtInputParser
{
    const char Esc = '\x1b';
    const int MaxSequenceLength = 64;

    readonly StringBuilder pending = new();

    public List<TermEvent> Feed(ReadOnlySpan<char> chunk)
    {
        pending.Append(chunk);
        var s = pending.ToString();
        pending.Clear();

        var events = new List<TermEvent>();
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (c != Esc)
            {
                events.Add(KeyFor(c));
                i++;
                continue;
            }

            if (i + 1 >= s.Length)
            {
                // A lone ESC at the end of a read is the Escape key; terminals deliver sequences in one write.
                events.Add(new KeyEvent(Key.Escape));
                i++;
                continue;
            }

            char next = s[i + 1];
            if (next == '[')
            {
                int consumed = ParseCsi(s, i, events);
                if (consumed == 0)
                {
                    pending.Append(s, i, s.Length - i);
                    break;
                }
                i += consumed;
            }
            else if (next == 'O')
            {
                if (i + 2 >= s.Length)
                {
                    pending.Append(s, i, s.Length - i);
                    break;
                }
                if (Ss3Key(s[i + 2]) is { } key) events.Add(new KeyEvent(key));
                i += 3;
            }
            else if (next == Esc)
            {
                events.Add(new KeyEvent(Key.Escape));
                i++;
            }
            else
            {
                var k = KeyFor(next);
                events.Add(k with { Mods = k.Mods | Mods.Alt });
                i += 2;
            }
        }
        return events;
    }

    static KeyEvent KeyFor(char c) => c switch
    {
        '\r' or '\n' => new KeyEvent(Key.Enter),
        '\t' => new KeyEvent(Key.Tab),
        '\x7f' or '\b' => new KeyEvent(Key.Backspace),
        < ' ' and > '\0' => new KeyEvent(Key.Char, (char)('a' + c - 1), Mods.Ctrl),
        '\0' => new KeyEvent(Key.Char, ' ', Mods.Ctrl),
        _ => new KeyEvent(Key.Char, c),
    };

    static Key? Ss3Key(char c) => c switch
    {
        'A' => Key.Up,
        'B' => Key.Down,
        'C' => Key.Right,
        'D' => Key.Left,
        'H' => Key.Home,
        'F' => Key.End,
        _ => null,
    };

    /// <summary>Parses "ESC [ params final". Returns characters consumed, or 0 if the sequence is incomplete.</summary>
    static int ParseCsi(string s, int start, List<TermEvent> events)
    {
        int final = -1;
        for (int j = start + 2; j < s.Length && j < start + MaxSequenceLength; j++)
        {
            if (s[j] >= '@' && s[j] <= '~')
            {
                final = j;
                break;
            }
        }
        if (final < 0)
            return s.Length - start >= MaxSequenceLength ? s.Length - start : 0;

        var parameters = s.AsSpan(start + 2, final - start - 2);
        char f = s[final];

        if (parameters.Length > 0 && parameters[0] == '<' && (f == 'M' || f == 'm'))
        {
            if (ParseMouse(parameters[1..], f == 'M') is { } mouse) events.Add(mouse);
        }
        else
        {
            var parts = parameters.ToString().Split(';');
            int p1 = parts.Length > 0 && int.TryParse(parts[0], out var a) ? a : 1;
            int p2 = parts.Length > 1 && int.TryParse(parts[1], out var b) ? b : 1;
            var mods = ModsFrom(p2);

            Key? key = f switch
            {
                'A' => Key.Up,
                'B' => Key.Down,
                'C' => Key.Right,
                'D' => Key.Left,
                'H' => Key.Home,
                'F' => Key.End,
                'Z' => Key.BackTab,
                '~' => p1 switch
                {
                    1 or 7 => Key.Home,
                    2 => Key.Insert,
                    3 => Key.Delete,
                    4 or 8 => Key.End,
                    5 => Key.PageUp,
                    6 => Key.PageDown,
                    _ => null,
                },
                _ => null,
            };
            if (key is { } k) events.Add(new KeyEvent(k, '\0', mods));
        }
        return final - start + 1;
    }

    /// <summary>xterm modifier parameter: 1 + (shift | alt&lt;&lt;1 | ctrl&lt;&lt;2).</summary>
    static Mods ModsFrom(int p)
    {
        int bits = Math.Max(0, p - 1);
        var mods = Mods.None;
        if ((bits & 1) != 0) mods |= Mods.Shift;
        if ((bits & 2) != 0) mods |= Mods.Alt;
        if ((bits & 4) != 0) mods |= Mods.Ctrl;
        return mods;
    }

    /// <summary>SGR-1006 mouse: "b;x;y" with M for press/motion and m for release.</summary>
    static MouseEvent? ParseMouse(ReadOnlySpan<char> p, bool press)
    {
        var parts = p.ToString().Split(';');
        if (parts.Length != 3
            || !int.TryParse(parts[0], out var b)
            || !int.TryParse(parts[1], out var x)
            || !int.TryParse(parts[2], out var y))
            return null;

        var mods = Mods.None;
        if ((b & 4) != 0) mods |= Mods.Shift;
        if ((b & 8) != 0) mods |= Mods.Alt;
        if ((b & 16) != 0) mods |= Mods.Ctrl;

        var button = (b & 3) switch
        {
            0 => MouseButton.Left,
            1 => MouseButton.Middle,
            2 => MouseButton.Right,
            _ => MouseButton.None,
        };

        MouseKind kind;
        if ((b & 64) != 0)
        {
            if ((b & 3) > 1) return null; // horizontal wheel
            kind = (b & 1) == 0 ? MouseKind.WheelUp : MouseKind.WheelDown;
            button = MouseButton.None;
        }
        else if ((b & 32) != 0)
        {
            kind = button == MouseButton.None ? MouseKind.Move : MouseKind.Drag;
        }
        else
        {
            kind = press ? MouseKind.Down : MouseKind.Up;
        }

        return new MouseEvent(kind, button, x - 1, y - 1, mods);
    }
}
