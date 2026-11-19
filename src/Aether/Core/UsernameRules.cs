namespace Aether;

public sealed record CustomLoad(List<string> Names, int Invalid, int Skipped);

public static class UsernameRules
{
    public static bool IsValid(string name)
    {
        if (name.Length < 2 || name.Length > 32) return false;
        char prev = '\0';
        foreach (var ch in name)
        {
            bool ok = (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '_' || ch == '.';
            if (!ok) return false;
            if (ch == '.' && prev == '.') return false;
            prev = ch;
        }
        return true;
    }

    public static List<string> Generate(UsernamesCfg cfg, Func<string, bool> skip)
    {
        var pattern = Patterns.Find(cfg.Pattern);
        int length = Math.Clamp(cfg.Length, pattern.MinLength, 32);
        int want = Math.Max(0, cfg.Amount);

        var everything = Patterns.Enumerate(pattern, length);
        if (everything != null)
        {
            everything.RemoveAll(n => !IsValid(n) || skip(n));
            Shuffle(everything);
            if (everything.Count > want) everything.RemoveRange(want, everything.Count - want);
            return everything;
        }

        var result = new List<string>(Math.Min(want, 100_000));
        var seen = new HashSet<string>();
        long maxTries = (long)want * 20 + 1000;
        for (long t = 0; t < maxTries && result.Count < want; t++)
        {
            var candidate = Patterns.Make(pattern, length);
            if (!IsValid(candidate) || skip(candidate) || !seen.Add(candidate)) continue;
            result.Add(candidate);
        }
        return result;
    }

    public static CustomLoad LoadCustom(Func<string, bool> skip)
    {
        var names = new List<string>();
        int invalid = 0, skipped = 0;
        try
        {
            if (!File.Exists(Paths.Usernames))
            {
                File.WriteAllText(Paths.Usernames, "");
                return new CustomLoad(names, 0, 0);
            }
            var seen = new HashSet<string>();
            foreach (var raw in File.ReadLines(Paths.Usernames))
            {
                var name = raw.Trim().ToLowerInvariant();
                if (name.Length == 0 || name[0] == '#') continue;
                if (!IsValid(name)) { invalid++; continue; }
                if (!seen.Add(name)) continue;
                if (skip(name)) { skipped++; continue; }
                names.Add(name);
            }
        }
        catch { }
        return new CustomLoad(names, invalid, skipped);
    }

    public static (List<string> Names, int Invalid) Parse(IEnumerable<string> lines)
    {
        var names = new List<string>();
        var seen = new HashSet<string>();
        int invalid = 0;
        foreach (var raw in lines)
        {
            var name = raw.Trim().ToLowerInvariant();
            if (name.Length == 0 || name[0] == '#') continue;
            if (!IsValid(name)) { invalid++; continue; }
            if (seen.Add(name)) names.Add(name);
        }
        return (names, invalid);
    }

    private static void Shuffle(List<string> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
