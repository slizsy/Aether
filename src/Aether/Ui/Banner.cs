namespace Aether;

public static class Banner
{
    private static readonly Dictionary<char, string[]> Font = new()
    {
        ['A'] = new[] { " █████╗ ", "██╔══██╗", "███████║", "██╔══██║", "██║  ██║", "╚═╝  ╚═╝" },
        ['E'] = new[] { "███████╗", "██╔════╝", "█████╗  ", "██╔══╝  ", "███████╗", "╚══════╝" },
        ['T'] = new[] { "████████╗", "╚══██╔══╝", "   ██║   ", "   ██║   ", "   ██║   ", "   ╚═╝   " },
        ['H'] = new[] { "██╗  ██╗", "██║  ██║", "███████║", "██╔══██║", "██║  ██║", "╚═╝  ╚═╝" },
        ['R'] = new[] { "██████╗ ", "██╔══██╗", "██████╔╝", "██╔══██╗", "██║  ██║", "╚═╝  ╚═╝" },
    };

    private static readonly string[] Art = Build("AETHER");
    private static readonly Tone Light = new(250, 92, 92, ConsoleColor.Red);

    private static string[] Build(string word)
    {
        var rows = new string[6];
        for (int r = 0; r < rows.Length; r++)
            rows[r] = string.Concat(word.Select(c => Font[c][r]));
        return rows;
    }

    public static void Show()
    {
        Term.Clear();
        Term.Blank();

        int width = Art.Max(l => l.Length);
        if (Term.Width < width + 4)
        {
            Term.Line(("  A E T H E R", Tones.Red));
        }
        else
        {
            for (int row = 0; row < Art.Length; row++)
            {
                string line = Art[row];
                var parts = new (string, Tone)[line.Length + 1];
                parts[0] = ("  ", Tones.Gray);
                for (int col = 0; col < line.Length; col++)
                {
                    char ch = line[col];
                    Tone tone = ch == '█'
                        ? Term.Blend(Light, Tones.Crimson, (col + row * 3.0) / (width + 18))
                        : Tones.Ember;
                    parts[col + 1] = (ch.ToString(), tone);
                }
                Term.Line(parts);
            }
        }

        Term.Line(("  made by ", Tones.Gray), ("slizsy", Tones.Red));
        Term.Blank();
    }
}
