namespace Aether;

public sealed record Pattern(string Key, string Name, string Example, int MinLength);

public static class Patterns
{
    private const string Letters = "abcdefghijklmnopqrstuvwxyz";
    private const string Digits = "0123456789";
    private const string Vowels = "aeiou";
    private const string Consonants = "bcdfghjklmnprstvwxyz";

    public const string Default = "mix";

    public static readonly Pattern[] All =
    {
        new("letters", "Letters Only", "abc, xyz", 2),
        new("alphanumeric", "Alphanumeric", "a1b2, xyz9", 2),
        new("double", "Double Letters", "vvss, ddff", 2),
        new("dot", "Period Dot Mix", "a.b, x.yz", 3),
        new("triple", "Triple + Different", "aaae, bbbx", 4),
        new("underscore", "Underscore Mix", "a_b_c, x_y", 3),
        new("vowel", "Vowel-Heavy", "baki, zito", 2),
        new("palindrome", "Palindrome", "abccba, xyzzyx", 2),
        new("mix", "Random Mix", "Recommended variety", 2),
    };

    public static Pattern Find(string? key)
    {
        return All.FirstOrDefault(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            ?? All.First(p => p.Key == Default);
    }

    public static int IndexOf(string? key) => Array.IndexOf(All, Find(key));

    // Every name the pattern can produce, when that is small enough to list outright.
    public static List<string>? Enumerate(Pattern p, int length)
    {
        string? chars = p.Key switch
        {
            "letters" => Letters,
            "alphanumeric" => Letters + Digits,
            _ => null
        };
        if (chars == null || Math.Pow(chars.Length, length) > 1_000_000) return null;

        int total = (int)Math.Pow(chars.Length, length);
        var all = new List<string>(total);
        var buf = new char[length];
        for (int i = 0; i < total; i++)
        {
            int n = i;
            for (int pos = length - 1; pos >= 0; pos--)
            {
                buf[pos] = chars[n % chars.Length];
                n /= chars.Length;
            }
            if (p.Key == "alphanumeric" && !(buf.Any(char.IsDigit) && buf.Any(char.IsLetter))) continue;
            all.Add(new string(buf));
        }
        return all;
    }

    public static string Make(Pattern p, int length)
    {
        var rng = Random.Shared;
        var buf = new char[length];

        switch (p.Key)
        {
            case "letters":
                Fill(buf, Letters);
                break;

            case "alphanumeric":
                Fill(buf, Letters + Digits);
                if (!buf.Any(char.IsDigit)) buf[rng.Next(length)] = Pick(Digits);
                else if (!buf.Any(char.IsLetter)) buf[rng.Next(length)] = Pick(Letters);
                break;

            case "double":
            {
                char prev = '\0';
                for (int i = 0; i < length; i += 2)
                {
                    char c = PickOther(Letters, prev);
                    buf[i] = c;
                    if (i + 1 < length) buf[i + 1] = c;
                    prev = c;
                }
                break;
            }

            case "dot":
                Fill(buf, Letters);
                buf[1 + rng.Next(length - 2)] = '.';
                break;

            case "triple":
            {
                char a = Pick(Letters);
                buf[0] = buf[1] = buf[2] = a;
                for (int i = 3; i < length; i++) buf[i] = PickOther(Letters, a);
                break;
            }

            case "underscore":
                for (int i = 0; i < length; i++)
                    buf[i] = i % 2 == 1 && i < length - 1 ? '_' : Pick(Letters);
                break;

            case "vowel":
            {
                bool vowel = rng.Next(4) == 0;
                for (int i = 0; i < length; i++, vowel = !vowel)
                    buf[i] = Pick(vowel ? Vowels : Consonants);
                break;
            }

            case "palindrome":
                for (int i = 0; i < (length + 1) / 2; i++)
                    buf[i] = buf[length - 1 - i] = Pick(Letters);
                break;

            default:
            {
                var options = All.Where(x => x.Key != Default && x.MinLength <= length).ToArray();
                return Make(options[rng.Next(options.Length)], length);
            }
        }

        return new string(buf);
    }

    private static char Pick(string chars) => chars[Random.Shared.Next(chars.Length)];

    private static char PickOther(string chars, char not)
    {
        char c;
        do c = Pick(chars); while (c == not);
        return c;
    }

    private static void Fill(char[] buf, string chars)
    {
        for (int i = 0; i < buf.Length; i++) buf[i] = Pick(chars);
    }
}
