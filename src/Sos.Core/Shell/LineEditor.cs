using System.Text;
using Sos.Core.Input;

namespace Sos.Core.Shell;

/// <summary>Single-line command editor for the prompt, with history.</summary>
public sealed class LineEditor
{
    readonly StringBuilder text = new();
    readonly List<string> history = [];
    int historyIndex;

    public string Text => text.ToString();
    public int Cursor { get; private set; }

    public void AddHistory(string command)
    {
        if (command.Length > 0 && (history.Count == 0 || history[^1] != command)) history.Add(command);
        historyIndex = history.Count;
    }

    /// <summary>Applies an editing key. Returns false if the key isn't an editing key.</summary>
    public bool Handle(KeyEvent key)
    {
        if (key.IsText)
        {
            text.Insert(Cursor++, key.Ch);
            return true;
        }
        if (key.IsCtrl('u') || key.IsCtrl('c'))
        {
            Set("");
            return true;
        }

        switch (key.Key)
        {
            case Key.Backspace when Cursor > 0:
                text.Remove(--Cursor, 1);
                return true;
            case Key.Delete when Cursor < text.Length:
                text.Remove(Cursor, 1);
                return true;
            case Key.Left:
                Cursor = Math.Max(0, Cursor - 1);
                return true;
            case Key.Right:
                Cursor = Math.Min(text.Length, Cursor + 1);
                return true;
            case Key.Home:
                Cursor = 0;
                return true;
            case Key.End:
                Cursor = text.Length;
                return true;
            case Key.Up when historyIndex > 0:
                Set(history[--historyIndex]);
                return true;
            case Key.Down when historyIndex < history.Count:
                historyIndex++;
                Set(historyIndex < history.Count ? history[historyIndex] : "");
                return true;
            case Key.Backspace or Key.Delete or Key.Up or Key.Down:
                return true;
            default:
                return false;
        }
    }

    public string Submit()
    {
        var command = Text;
        AddHistory(command.Trim());
        Set("");
        return command;
    }

    void Set(string value)
    {
        text.Clear().Append(value);
        Cursor = text.Length;
    }
}
