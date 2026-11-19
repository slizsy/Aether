using System.Collections.Concurrent;
using System.Diagnostics;

namespace Aether;

public sealed class App
{
    private readonly record struct Note(string Text, bool Good);

    private AppConfig _cfg = new();
    private readonly ProxyPool _pool = new();
    private readonly Blacklist _blacklist = new();
    private List<string> _names = new();
    private int _invalid;
    private int _skipped;

    public async Task RunAsync()
    {
        Term.Init();
        Term.Title("Aether");
        Paths.EnsureAll();
        _cfg = ConfigStore.Load(out var warning);
        _blacklist.Load();
        _pool.Load();
        Prepare();

        Note? note = warning != null ? new Note(warning, false) : null;
        while (true)
        {
            DrawMain(note);
            note = null;

            var choice = Input.Ask("Select an option");
            if (choice == null) return;

            switch (choice)
            {
                case "1": note = await StartAsync(); break;
                case "2": note = PatternMenu(); break;
                case "3": note = PasteUsernames(); break;
                case "4": await ProxiesMenuAsync(); break;
                case "5": await SettingsMenuAsync(); break;
                case "6": FilesMenu(); break;
                case "7": return;
                case "": break;
                default: note = new Note("Pick a number from the menu.", false); break;
            }
        }
    }

    private void Prepare()
    {
        _invalid = 0;
        _skipped = 0;
        if (_cfg.Usernames.Custom)
        {
            var load = UsernameRules.LoadCustom(_blacklist.Contains);
            _names = load.Names;
            _invalid = load.Invalid;
            _skipped = load.Skipped;
        }
        else
        {
            _names = UsernameRules.Generate(_cfg.Usernames, _blacklist.Contains);
        }
    }

    private string Source()
    {
        return _cfg.Usernames.Custom
            ? "your own list"
            : $"{Patterns.Find(_cfg.Usernames.Pattern).Name}, {_cfg.Usernames.Length} chars";
    }

    private static (string, Tone)[] Item(string key, string label, string? hint = null, int pad = 0)
    {
        return new[]
        {
            ("[", Tones.Gray), (key, Tones.White), ("]", Tones.Gray),
            (new string(' ', key.Length > 1 ? 1 : 2), Tones.Gray),
            (pad > 0 ? label.PadRight(pad) : label, Tones.White),
            (hint ?? "", Tones.Gray)
        };
    }

    private static void Menu(params (string Key, string Label)[] items)
    {
        foreach (var (key, label) in items)
            Term.Line(new[] { ("  ", Tones.Gray) }.Concat(Item(key, label)).ToArray());
    }

    private static (string, Tone)[] Status(string label, Tone state, string value)
    {
        return new[] { ("  ● ", state), (label.PadRight(11), state), (value, Tones.White) };
    }

    private static (string, Tone)[] Detail(string text) => new[] { ("    " + text, Tones.Gray) };

    private static void Header(string title)
    {
        Banner.Show();
        Term.Section(title);
    }

    private static void ShowNote(Note? note)
    {
        if (note == null) return;
        Term.Blank();
        if (note.Value.Good) Term.Good(note.Value.Text);
        else Term.Warn(note.Value.Text);
    }

    private void DrawMain(Note? note)
    {
        Banner.Show();

        var left = new List<(string, Tone)[]>
        {
            Status("USERNAMES", _names.Count > 0 ? Tones.Green : Tones.Red, $"{_names.Count:N0} ready"),
            Detail(Source()),
            Status("PROXIES", _pool.Count > 0 ? Tones.Green : Tones.Yellow, _pool.Count > 0 ? $"{_pool.Count:N0} loaded" : "none, your IP"),
            Detail($"{_cfg.Threads} threads, {_cfg.Timeout}s timeout"),
        };

        var right = new List<(string, Tone)[]>
        {
            Item("1", "Start checking"),
            Item("2", "Generate usernames"),
            Item("3", "Paste usernames"),
            Item("4", "Proxies"),
            Item("5", "Settings"),
            Item("6", "Stats and files"),
            Item("7", "Exit"),
        };

        Term.Columns(left, right, 40);
        Term.Blank();
        Term.Rule();

        if (_names.Count == 0)
        {
            Term.Blank();
            Term.Line(("  Tip  ", Tones.Red), ("no usernames ready. Pick 2 to generate some or 3 to paste a list.", Tones.Gray));
        }
        else if (_pool.Count == 0)
        {
            Term.Blank();
            Term.Line(("  Tip  ", Tones.Red), ("no proxies, so checks use your own IP. Add some under 4.", Tones.Gray));
        }

        ShowNote(note);
        Term.Blank();
    }

    private async Task<Note?> StartAsync()
    {
        if (_names.Count == 0)
            return new Note("No usernames to check. Generate or paste some first.", false);

        Banner.Show();
        using var runner = new Runner(_cfg, _pool, _blacklist, Source());

        bool connected = await runner.PreflightAsync();
        if (!connected && !Input.Bool("Start anyway", false))
            return new Note("Cancelled. Fix the connection under Proxies, then try again.", false);

        Term.Blank();
        Term.Section("Checking");
        Term.Line(
            ($"  {_names.Count:N0} usernames", Tones.White),
            ("  |  ", Tones.Gray),
            ($"{_cfg.Threads} threads", Tones.White),
            ("  |  ", Tones.Gray),
            (_pool.Count > 0 ? $"{_pool.Count} proxies" : "your own IP", Tones.White),
            ("  |  Ctrl+C stops", Tones.Gray));
        Term.Blank();

        await runner.RunAsync(_names);

        Input.Pause();
        Prepare();
        return null;
    }

    private Note? PatternMenu()
    {
        Header("Pattern selection");

        for (int i = 0; i < Patterns.All.Length; i++)
        {
            var p = Patterns.All[i];
            Term.Line(new[] { ("  ", Tones.Gray) }.Concat(Item((i + 1).ToString(), p.Name, p.Example, 22)).ToArray());
        }
        Term.Blank();

        var u = _cfg.Usernames;
        int pick = Input.Int("Pattern", Patterns.IndexOf(u.Pattern) + 1, 1, Patterns.All.Length);
        var pattern = Patterns.All[pick - 1];
        int length = Input.Int("Length", Math.Max(u.Length, pattern.MinLength), pattern.MinLength, 32);
        int count = Input.Int("Count", u.Amount, 1, 1_000_000);

        u.Custom = false;
        u.Pattern = pattern.Key;
        u.Length = length;
        u.Amount = count;
        _cfg.Normalize();
        ConfigStore.Save(_cfg);

        Term.Blank();
        Term.Line(("  Generating...", Tones.Gray));
        Prepare();

        if (_names.Count == 0)
            return new Note($"Every {pattern.Name} name at {length} chars was checked before. Try another length.", false);
        if (_names.Count < count)
            return new Note($"Only {_names.Count:N0} unchecked {pattern.Name} names fit {length} chars. All of them are ready.", true);
        return new Note($"{_names.Count:N0} usernames ready ({pattern.Name}, {length} chars).", true);
    }

    private Note PasteUsernames()
    {
        Header("Paste usernames");
        Term.Line(("  One per line. Press Enter on an empty line to finish.", Tones.Gray));
        Term.Line(("  2-32 chars: lowercase letters, digits, _ and .", Tones.Gray));
        Term.Blank();
        var pasted = Input.Block();
        var parsed = UsernameRules.Parse(pasted);
        if (parsed.Names.Count == 0)
            return new Note(pasted.Count == 0 ? "Nothing pasted." : "No valid usernames found. Use 2-32 letters, digits, _ or .", false);

        try
        {
            var existing = File.Exists(Paths.Usernames) ? UsernameRules.Parse(File.ReadAllLines(Paths.Usernames)).Names : new List<string>();
            bool add = existing.Count == 0 || Input.Bool("Add to the existing list", true);
            var all = add ? existing.Concat(parsed.Names).Distinct().ToList() : parsed.Names;
            File.WriteAllLines(Paths.Usernames, all);
            _cfg.Usernames.Custom = true;
            ConfigStore.Save(_cfg);
            Prepare();
            string extra = parsed.Invalid > 0 ? $", {parsed.Invalid} invalid skipped" : "";
            string seen = _skipped > 0 ? $", {_skipped} checked before" : "";
            return new Note($"{parsed.Names.Count} added. {_names.Count:N0} ready to check{extra}{seen}.", true);
        }
        catch (Exception ex)
        {
            return new Note("Could not save: " + ex.Message, false);
        }
    }

    private async Task ProxiesMenuAsync()
    {
        Note? note = null;
        while (true)
        {
            Header("Proxies");
            Term.Line(Status("LOADED", _pool.Count > 0 ? Tones.Green : Tones.Yellow, _pool.Count.ToString("N0")));
            Term.Line(Status("READY", _pool.Ready() > 0 ? Tones.Green : Tones.Yellow, _pool.Ready().ToString("N0")));
            Term.Blank();
            Menu(("1", "Paste proxies"), ("2", "Test proxies"), ("3", "Open proxies.txt"), ("4", "Remove all proxies"), ("5", "Back"));
            ShowNote(note);
            Term.Blank();
            note = null;

            var choice = Input.Ask("Select an option");
            if (choice == null || choice == "5") return;

            switch (choice)
            {
                case "1":
                    note = PasteProxies();
                    break;
                case "2":
                    await TestProxiesAsync();
                    break;
                case "3":
                    try
                    {
                        if (!File.Exists(Paths.Proxies)) File.WriteAllText(Paths.Proxies, "");
                        Open(Paths.Proxies);
                        Term.Blank();
                        Term.Inline(("  Save the file, then press Enter to reload it", Tones.Gray));
                        Console.ReadLine();
                        _pool.Load();
                        note = new Note($"{_pool.Count} proxies loaded.", true);
                    }
                    catch
                    {
                        note = new Note("Could not open it. Path: " + Paths.Proxies, false);
                    }
                    break;
                case "4":
                    Term.Blank();
                    if (Input.Bool("Remove every proxy", false))
                    {
                        try { File.WriteAllText(Paths.Proxies, ""); } catch { }
                        _pool.Load();
                        note = new Note("All proxies removed.", true);
                    }
                    break;
                case "":
                    break;
                default:
                    note = new Note("Pick a number from the menu.", false);
                    break;
            }
        }
    }

    private Note PasteProxies()
    {
        Term.Blank();
        Term.Line(("  Paste proxies, one per line. Press Enter on an empty line to finish.", Tones.Gray));
        Term.Line(("  host:port   host:port:user:pass   http://user:pass@host:port   socks5://...", Tones.Gray));
        Term.Blank();
        var pasted = Input.Block();
        if (pasted.Count == 0) return new Note("Nothing pasted.", false);

        var good = new List<string>();
        int bad = 0;
        foreach (var line in pasted)
        {
            if (line[0] == '#') continue;
            if (ProxyPool.Normalize(line) != null) good.Add(line);
            else bad++;
        }
        if (good.Count == 0)
            return new Note($"No valid proxies found ({bad} unreadable).", false);

        try
        {
            var existing = File.Exists(Paths.Proxies) ? File.ReadAllLines(Paths.Proxies).ToList() : new List<string>();
            bool hasExisting = existing.Any(l => l.Trim().Length > 0 && l.TrimStart()[0] != '#');
            bool add = !hasExisting || Input.Bool("Add to the existing proxies", true);
            var output = add ? existing.Concat(good).Distinct().ToList() : good.Distinct().ToList();
            File.WriteAllLines(Paths.Proxies, output);
            _pool.Load();
            string extra = bad > 0 ? $", {bad} unreadable skipped" : "";
            return new Note($"{good.Count} added. {_pool.Count} proxies loaded{extra}.", true);
        }
        catch (Exception ex)
        {
            return new Note("Could not save: " + ex.Message, false);
        }
    }

    private async Task TestProxiesAsync()
    {
        Term.Blank();
        if (_pool.Count == 0)
        {
            Term.Warn("No proxies to test.");
            Input.Pause();
            return;
        }

        Term.Line(("  Testing ", Tones.White), (_pool.Count.ToString(), Tones.Red), (" proxies against Discord", Tones.White));
        Term.Blank();

        var dead = new ConcurrentBag<string>();
        var reasons = new ConcurrentBag<string>();
        int alive = 0;
        var options = new ParallelOptions { MaxDegreeOfParallelism = 20 };

        await Parallel.ForEachAsync(_pool.Nodes, options, async (node, ct) =>
        {
            var probe = await Checker.ProbeAsync(node.GetClient(_cfg.Timeout), ct);
            if (probe.Ok)
            {
                Interlocked.Increment(ref alive);
                Term.Line(("  [+] ", Tones.Green), (node.Display.PadRight(24), Tones.White), ($"{probe.Ms} ms", Tones.Gray));
            }
            else
            {
                dead.Add(node.Url);
                reasons.Add(probe.Detail);
                Term.Line(("  [-] ", Tones.Red), (node.Display.PadRight(24), Tones.White), (probe.Detail, Tones.Gray));
            }
        });

        Term.Blank();
        Term.Line(("  Working ", Tones.Gray), (alive.ToString(), Tones.Green), ("   Dead ", Tones.Gray), (dead.Count.ToString(), Tones.Red));

        if (alive == 0 && !reasons.IsEmpty)
        {
            string top = reasons.GroupBy(r => r).OrderByDescending(g => g.Count()).First().Key;
            Term.Blank();
            Term.Warn("Every proxy failed: " + top);
            Term.Hint("Check the login, your data balance, and whether the plan allows discord.com.");
        }

        if (!dead.IsEmpty && alive > 0 && Input.Bool("Remove the dead proxies from the file", false))
        {
            RemoveDead(new HashSet<string>(dead));
            Term.Good("Removed");
        }
        Input.Pause();
    }

    private void RemoveDead(HashSet<string> dead)
    {
        try
        {
            var keep = new List<string>();
            foreach (var raw in File.ReadAllLines(Paths.Proxies))
            {
                var line = raw.Trim();
                if (line.Length > 0 && line[0] != '#')
                {
                    var url = ProxyPool.Normalize(line);
                    if (url != null && dead.Contains(url)) continue;
                }
                keep.Add(raw);
            }
            File.WriteAllLines(Paths.Proxies, keep);
        }
        catch { }
        _pool.Load();
    }

    private async Task SettingsMenuAsync()
    {
        Header("Settings");
        var c = _cfg;

        Term.Line(("  [", Tones.Gray), ("auto", Tones.White), ("]      threads follow your proxy count", Tones.Gray));
        Term.Line(("  [", Tones.Gray), ("safe", Tones.White), ("]      5 threads, slow and gentle", Tones.Gray));
        Term.Line(("  [", Tones.Gray), ("balanced", Tones.White), ("]  20 threads", Tones.Gray));
        Term.Line(("  [", Tones.Gray), ("fast", Tones.White), ("]      50 threads, needs good proxies", Tones.Gray));
        Term.Line(("  [", Tones.Gray), ("custom", Tones.White), ("]    set every value yourself", Tones.Gray));
        Term.Blank();

        string preset = Input.Choice("Preset", "keep", new[] { "keep", "auto", "safe", "balanced", "fast", "custom" });
        switch (preset)
        {
            case "auto":
                c.Threads = _pool.Count == 0 ? 3 : Math.Clamp(_pool.Count * 2, 3, 40);
                c.Timeout = 15; c.Retry.Enabled = true; c.Retry.MaxAttempts = 4;
                break;
            case "safe":
                c.Threads = 5; c.Timeout = 20; c.Retry.Enabled = true; c.Retry.MaxAttempts = 5;
                break;
            case "balanced":
                c.Threads = 20; c.Timeout = 15; c.Retry.Enabled = true; c.Retry.MaxAttempts = 4;
                break;
            case "fast":
                c.Threads = 50; c.Timeout = 10; c.Retry.Enabled = true; c.Retry.MaxAttempts = 3;
                break;
            case "custom":
                c.Threads = Input.Int("Threads", c.Threads, 1, 1000);
                c.Timeout = Input.Int("Timeout seconds", c.Timeout, 1, 120);
                c.Retry.Enabled = Input.Bool("Retry failed requests", c.Retry.Enabled);
                if (c.Retry.Enabled)
                    c.Retry.MaxAttempts = Input.Int("Max attempts", c.Retry.MaxAttempts, 1, 20);
                break;
        }

        Term.Blank();
        c.Verbose = Input.Bool("Show every result", c.Verbose);
        c.Beep = Input.Bool("Beep when a name is available", c.Beep);

        string state = string.IsNullOrEmpty(c.Webhook) ? "none" : "set";
        var hook = Input.Ask("Discord webhook URL, - clears", state);
        bool changed = false;
        if (hook == "-")
        {
            c.Webhook = "";
        }
        else if (!string.IsNullOrEmpty(hook))
        {
            if (Webhook.IsValid(hook))
            {
                c.Webhook = Webhook.Clean(hook);
                changed = true;
            }
            else
            {
                Term.Warn("That is not a Discord webhook URL, kept the old value.");
                Term.Hint("In Discord: Channel settings > Integrations > Webhooks > Copy Webhook URL.");
            }
        }

        if (!string.IsNullOrEmpty(c.Webhook) && Input.Bool("Send a test message", changed))
        {
            Term.Line(("  Sending...", Tones.Gray));
            var test = await Webhook.TestAsync(c.Webhook);
            if (test.Ok) Term.Good(test.Message);
            else Term.Warn("Webhook failed: " + test.Message);
        }

        c.Normalize();
        ConfigStore.Save(c);
        _pool.ResetClients();

        Term.Blank();
        Term.Line(
            ("  [+] ", Tones.Green),
            ("Saved  ", Tones.White),
            ($"{c.Threads} threads, {c.Timeout}s timeout, {(c.Retry.Enabled ? c.Retry.MaxAttempts : 1)} attempts", Tones.Gray));
        Input.Pause();
    }

    private void FilesMenu()
    {
        Note? note = null;
        while (true)
        {
            Header("Stats and files");

            var life = LifetimeStats.Load();
            long saved = 0;
            try
            {
                if (File.Exists(Paths.Valids))
                    saved = File.ReadLines(Paths.Valids).Count(l => l.Trim().Length > 0);
            }
            catch { }

            Term.Row("Runs", life.Sessions.ToString("N0"), Tones.White);
            Term.Row("Checked", life.Checked.ToString("N0"), Tones.White);
            Term.Row("Available", life.Available.ToString("N0"), Tones.Green);
            Term.Row("Taken", life.Taken.ToString("N0"), Tones.Red);
            Term.Row("Failed", life.Failed.ToString("N0"), Tones.Yellow);
            Term.Row("Saved names", saved.ToString("N0"), Tones.White);
            Term.Row("Skip list", _blacklist.Count.ToString("N0"), Tones.White);
            Term.Row("Folder", Paths.Root, Tones.Gray);
            Term.Blank();
            Menu(("1", "Open results folder"), ("2", "Open data folder"), ("3", "Open logs folder"), ("4", "Forget checked names"), ("5", "Back"));
            ShowNote(note);
            Term.Blank();
            note = null;

            var choice = Input.Ask("Select an option");
            if (choice == null || choice == "5") return;

            switch (choice)
            {
                case "1": note = OpenFolder(Paths.Results); break;
                case "2": note = OpenFolder(Paths.Data); break;
                case "3": note = OpenFolder(Paths.Logs); break;
                case "4":
                    Term.Blank();
                    if (Input.Bool("Forget every name checked before", false))
                    {
                        _blacklist.Clear();
                        Prepare();
                        note = new Note("Cleared. Those names can be checked again.", true);
                    }
                    break;
                case "": break;
                default: note = new Note("Pick a number from the menu.", false); break;
            }
        }
    }

    // Opens a file or folder with the system default app: Explorer on Windows, Finder on macOS.
    private static void Open(string path)
    {
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true })?.Dispose();
    }

    private static Note? OpenFolder(string path)
    {
        try
        {
            Open(path);
            return null;
        }
        catch
        {
            return new Note("Could not open it. Path: " + path, false);
        }
    }
}
