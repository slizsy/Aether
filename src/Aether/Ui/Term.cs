using System.Runtime.InteropServices;
using System.Text;

namespace Aether;

public readonly record struct Tone(int R, int G, int B, ConsoleColor Fallback);

public static class Tones
{
    public static readonly Tone Red = new(239, 68, 68, ConsoleColor.Red);
    public static readonly Tone Rose = new(252, 165, 165, ConsoleColor.Red);
    public static readonly Tone Crimson = new(185, 28, 28, ConsoleColor.DarkRed);
    public static readonly Tone Ember = new(110, 22, 22, ConsoleColor.DarkRed);
    public static readonly Tone Green = new(74, 222, 128, ConsoleColor.Green);
    public static readonly Tone Yellow = new(250, 204, 21, ConsoleColor.Yellow);
    public static readonly Tone Gray = new(120, 120, 120, ConsoleColor.DarkGray);
    public static readonly Tone Silver = new(170, 170, 170, ConsoleColor.Gray);
    public static readonly Tone White = new(240, 240, 240, ConsoleColor.White);
}

public static class Term
{
    private enum Mode { Plain, Legacy, Ansi256, TrueColor }

    private const string Reset = "\u001b[0m";

    private static readonly object Gate = new();
    private static Mode _mode = Mode.Legacy;
    private static (string Text, Tone Color)[]? _status;
    private static int _statusWidth;

    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int handle);
    [DllImport("kernel32.dll")] private static extern bool GetConsoleMode(IntPtr handle, out uint mode);
    [DllImport("kernel32.dll")] private static extern bool SetConsoleMode(IntPtr handle, uint mode);

    public static int Width
    {
        get
        {
            try
            {
                int w = Console.WindowWidth;
                return w > 0 ? w : 100;
            }
            catch { return 100; }
        }
    }

    public static void Init()
    {
        try { Console.OutputEncoding = new UTF8Encoding(false); } catch { }

        if (Console.IsOutputRedirected
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"))
            || Environment.GetEnvironmentVariable("TERM") == "dumb")
        {
            _mode = Mode.Plain;
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            try
            {
                var handle = GetStdHandle(-11);
                _mode = GetConsoleMode(handle, out var mode) && SetConsoleMode(handle, mode | 0x0004)
                    ? Mode.TrueColor
                    : Mode.Legacy;
            }
            catch { _mode = Mode.Legacy; }
            return;
        }

        // macOS Terminal.app only does 256 colors; iTerm2, Ghostty, Warp and friends set COLORTERM.
        var colorterm = Environment.GetEnvironmentVariable("COLORTERM") ?? "";
        _mode = colorterm.Contains("truecolor", StringComparison.OrdinalIgnoreCase)
                || colorterm.Contains("24bit", StringComparison.OrdinalIgnoreCase)
            ? Mode.TrueColor
            : Mode.Ansi256;
    }

    private static string Esc(Tone t) => _mode switch
    {
        Mode.TrueColor => $"\u001b[38;2;{t.R};{t.G};{t.B}m",
        Mode.Ansi256 => $"\u001b[38;5;{To256(t.R, t.G, t.B)}m",
        _ => ""
    };

    private static int To256(int r, int g, int b)
    {
        if (Math.Abs(r - g) < 12 && Math.Abs(g - b) < 12 && Math.Abs(r - b) < 12)
        {
            int avg = (r + g + b) / 3;
            if (avg < 8) return 16;
            if (avg > 238) return 231;
            return 232 + Math.Clamp((int)Math.Round((avg - 8) / 10.0), 0, 23);
        }
        static int Level(int v) => v < 48 ? 0 : v < 115 ? 1 : Math.Min(5, (v - 35) / 40);
        return 16 + 36 * Level(r) + 6 * Level(g) + Level(b);
    }

    private static void Put(string text, Tone tone)
    {
        if (text.Length == 0) return;
        switch (_mode)
        {
            case Mode.Plain:
                Console.Write(text);
                break;
            case Mode.Legacy:
                Console.ForegroundColor = tone.Fallback;
                Console.Write(text);
                Console.ResetColor();
                break;
            default:
                Console.Write(Esc(tone) + text + Reset);
                break;
        }
    }

    private static void Erase()
    {
        if (_status == null) return;
        Console.Write("\r" + new string(' ', _statusWidth) + "\r");
    }

    private static void Draw()
    {
        if (_status == null) return;
        int width = 0;
        foreach (var part in _status)
        {
            Put(part.Text, part.Color);
            width += part.Text.Length;
        }
        _statusWidth = width;
    }

    public static void SetStatus(params (string Text, Tone Color)[] parts)
    {
        if (Console.IsOutputRedirected) return;
        lock (Gate)
        {
            Erase();
            _status = parts;
            Draw();
        }
    }

    public static void ClearStatus()
    {
        lock (Gate)
        {
            Erase();
            _status = null;
            _statusWidth = 0;
        }
    }

    public static void Inline(params (string Text, Tone Color)[] parts)
    {
        lock (Gate)
        {
            Erase();
            foreach (var part in parts) Put(part.Text, part.Color);
        }
    }

    public static void Line(params (string Text, Tone Color)[] parts)
    {
        lock (Gate)
        {
            Erase();
            foreach (var part in parts) Put(part.Text, part.Color);
            Console.WriteLine();
            Draw();
        }
    }

    public static void Blank() => Line();

    public static void Row(string label, string value, Tone color)
    {
        Line(($"  {label,-12}", Tones.Gray), (value, color));
    }

    public static void Good(string text) => Line(("  [+] ", Tones.Green), (text, Tones.White));

    public static void Warn(string text) => Line(("  [!] ", Tones.Yellow), (text, Tones.White));

    public static void Hint(string text) => Line(("      " + text, Tones.Gray));

    // Heavy red divider with a lighter streak near the right, like a glint on metal.
    public static void Rule(int? length = null)
    {
        int w = Math.Clamp(length ?? Width - 4, 8, 76);
        var parts = new (string, Tone)[w + 1];
        parts[0] = ("  ", Tones.Gray);
        for (int i = 0; i < w; i++)
        {
            double t = (double)i / (w - 1);
            Tone tone = t < 0.68 ? Blend(Tones.Crimson, Tones.Red, t / 0.68)
                : t < 0.80 ? Blend(Tones.Red, Tones.Rose, (t - 0.68) / 0.12)
                : Blend(Tones.Rose, Tones.Crimson, (t - 0.80) / 0.20);
            parts[i + 1] = ("━", tone);
        }
        Line(parts);
    }

    public static void Section(string title)
    {
        Line(("  ▌ ", Tones.Red), (title.ToUpperInvariant(), Tones.White));
        Rule(Math.Min(Width - 4, 56));
        Blank();
    }

    public static void Prompt(string label, string? current = null)
    {
        Inline(
            ("  › ", Tones.Red),
            (label, Tones.White),
            (current != null ? $" [{current}]" : "", Tones.Gray),
            (": ", Tones.White));
    }

    public static void Columns(
        IReadOnlyList<(string Text, Tone Color)[]> left,
        IReadOnlyList<(string Text, Tone Color)[]> right,
        int leftWidth)
    {
        int rightWidth = right.Count == 0 ? 0 : right.Max(r => r.Sum(p => p.Text.Length));
        if (Width < leftWidth + rightWidth + 2)
        {
            foreach (var row in left) Line(row);
            Blank();
            foreach (var row in right) Line(row);
            return;
        }

        int rows = Math.Max(left.Count, right.Count);
        for (int i = 0; i < rows; i++)
        {
            var l = i < left.Count ? left[i] : Array.Empty<(string Text, Tone Color)>();
            var r = i < right.Count ? right[i] : Array.Empty<(string Text, Tone Color)>();
            int used = l.Sum(p => p.Text.Length);
            (string Text, Tone Color) pad = (new string(' ', Math.Max(1, leftWidth - used)), Tones.Gray);
            Line(l.Append(pad).Concat(r).ToArray());
        }
    }

    public static void Clear()
    {
        try
        {
            lock (Gate)
            {
                _status = null;
                _statusWidth = 0;
                if (_mode == Mode.Plain) Console.WriteLine();
                else Console.Clear();
            }
        }
        catch { }
    }

    public static void Title(string title)
    {
        try { Console.Title = title; } catch { }
    }

    public static void Bell()
    {
        _ = Task.Run(() =>
        {
            try
            {
                if (OperatingSystem.IsWindows()) Console.Beep(880, 120);
                else lock (Gate) Console.Write('\a');
            }
            catch { }
        });
    }

    public static Tone Blend(Tone a, Tone b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return new Tone(
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t),
            t < 0.5 ? a.Fallback : b.Fallback);
    }
}
