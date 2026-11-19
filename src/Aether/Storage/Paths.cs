namespace Aether;

public static class Paths
{
    public static readonly string Root = ResolveRoot();
    public static readonly string Data = Path.Combine(Root, "data");
    public static readonly string Results = Path.Combine(Root, "results");
    public static readonly string Logs = Path.Combine(Root, "logs");
    public static readonly string Config = Path.Combine(Data, "config.json");
    public static readonly string Proxies = Path.Combine(Data, "proxies.txt");
    public static readonly string Usernames = Path.Combine(Data, "usernames.txt");
    public static readonly string Blacklist = Path.Combine(Data, "blacklist.txt");
    public static readonly string Valids = Path.Combine(Data, "valids.txt");
    public static readonly string Stats = Path.Combine(Data, "stats.json");

    // Files live next to the app. If that folder is read-only (say, the app sits in a
    // system folder), fall back to the user's own app data folder.
    private static string ResolveRoot()
    {
        string dir = AppContext.BaseDirectory;
        var process = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(process))
        {
            var name = Path.GetFileNameWithoutExtension(process);
            var parent = Path.GetDirectoryName(process);
            if (!name.Equals("dotnet", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(parent))
                dir = parent;
        }

        if (CanWrite(dir)) return dir;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(home)) home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, "Aether");
    }

    private static bool CanWrite(string dir)
    {
        try
        {
            var probe = Path.Combine(dir, ".aether-write-test");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void EnsureAll()
    {
        Directory.CreateDirectory(Data);
        Directory.CreateDirectory(Results);
        Directory.CreateDirectory(Logs);
    }
}
