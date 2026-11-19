namespace Aether;

public static class Input
{
    public static string? Ask(string label, string? current = null)
    {
        Term.Prompt(label, current);
        return Console.ReadLine()?.Trim();
    }

    public static void Pause()
    {
        Term.Blank();
        Term.Inline(("  Press Enter to continue", Tones.Gray));
        Console.ReadLine();
    }

    public static List<string> Block()
    {
        var lines = new List<string>();
        while (true)
        {
            var line = Console.ReadLine();
            if (line == null) break;
            line = line.Trim();
            if (line.Length == 0) break;
            lines.Add(line);
        }
        return lines;
    }

    public static int Int(string label, int current, int min, int max)
    {
        while (true)
        {
            var s = Ask($"{label} ({min:N0}-{max:N0})", current.ToString("N0"));
            if (string.IsNullOrEmpty(s)) return current;
            s = s.Replace(",", "").Replace("_", "");
            if (int.TryParse(s, out var n) && n >= min && n <= max) return n;
            Term.Hint($"Use a number from {min:N0} to {max:N0}.");
        }
    }

    public static bool Bool(string label, bool current)
    {
        while (true)
        {
            var s = Ask($"{label} (y/n)", current ? "y" : "n")?.ToLowerInvariant();
            switch (s)
            {
                case null or "": return current;
                case "y" or "yes": return true;
                case "n" or "no": return false;
            }
            Term.Hint("Type y or n.");
        }
    }

    public static string Choice(string label, string current, IReadOnlyList<string> options)
    {
        while (true)
        {
            var s = Ask($"{label} ({string.Join("/", options)})", current)?.ToLowerInvariant();
            if (string.IsNullOrEmpty(s)) return current;
            if (options.Contains(s)) return s;
            Term.Hint("Pick one of: " + string.Join(", ", options));
        }
    }
}
