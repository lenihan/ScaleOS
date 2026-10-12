namespace Sos.Core.Ui;

public static class TextWrap
{
    /// <summary>Word-wraps text to <paramref name="width"/>, hard-splitting words longer than a line.</summary>
    public static List<string> Wrap(string text, int width)
    {
        var lines = new List<string>();
        if (width <= 0) return lines;

        foreach (var raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var para = raw.Replace("\t", "    ");
            int i = 0;
            while (para.Length - i > width)
            {
                // Look for a space within [i, i + width] so the line breaks between words.
                int cut = para.LastIndexOf(' ', i + width, width + 1);
                if (cut <= i)
                {
                    lines.Add(para.Substring(i, width));
                    i += width;
                }
                else
                {
                    lines.Add(para[i..cut].TrimEnd());
                    i = cut + 1;
                }
            }
            lines.Add(para[i..]);
        }
        return lines;
    }

    /// <summary>Single-line text cut to <paramref name="width"/> with an ellipsis.</summary>
    public static string Truncate(string text, int width)
    {
        text = text.Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');
        if (width <= 0) return "";
        return text.Length <= width ? text : text[..(width - 1)] + "…";
    }
}
