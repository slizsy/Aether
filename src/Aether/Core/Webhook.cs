using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace Aether;

public sealed record Hit(string Name, int Found, int Done, int Total, string Source)
{
    public long FoundAt { get; } = Environment.TickCount64;
}

public readonly record struct WebhookResult(bool Ok, string Message);

// Posts finds to a Discord webhook from a background queue, so checks never wait on Discord.
// Finds that pile up are batched (up to 10 per message) and rate limits are retried.
public sealed class Webhook
{
    public const string Avatar = "https://raw.githubusercontent.com/slizsy/Aether/main/assets/avatar.png";

    private const string BotName = "Aether";
    private const int Green = 0x22C55E;
    private const int Gold = 0xF5B700;
    private const int Red = 0xEF4444;
    private const int Gray = 0x52525B;

    private static readonly Regex UrlShape = new(
        @"^https://(?:ptb\.|canary\.)?discord(?:app)?\.com/api/(?:v\d+/)?webhooks/\d+/[\w-]+/?(?:\?.*)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HttpClient Client = CreateClient();
    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _url;
    private readonly string _ping;
    private readonly Channel<Hit> _queue = Channel.CreateUnbounded<Hit>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Task _worker;
    private int _sent;
    private int _failed;
    private int _warned;
    private int _paced;
    private long _readyAt;
    private long _delayTotal;
    private long _delayMax;
    private int _delayCount;

    public Webhook(string url, string ping)
    {
        _url = url;
        _ping = ping;
        _worker = Task.Run(WorkAsync);
    }

    public int Sent => _sent;
    public int Failed => _failed;
    public string? LastError { get; private set; }

    // How long finds took to reach Discord, from the moment they were found.
    public double AverageDelay => _delayCount == 0 ? 0 : _delayTotal / (double)_delayCount / 1000;
    public double MaxDelay => _delayMax / 1000.0;

    public static string Clean(string url) => url.Trim().Trim('"', '\'', '<', '>');

    public static bool IsValid(string url) => UrlShape.IsMatch(Clean(url));

    private static HttpClient CreateClient()
    {
        // Keep the connection to Discord open between finds, so a new find never waits on a handshake.
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(10),
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Aether/2.1 (+https://github.com/slizsy/Aether)");
        return client;
    }

    // Checks the webhook exists and opens the connection early, so the first find posts instantly.
    public static async Task<WebhookResult> CheckAsync(string url)
    {
        try
        {
            using var res = await Client.GetAsync(Clean(url));
            string body = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode) return new WebhookResult(false, Explain(res.StatusCode, body));
            using var doc = JsonDocument.Parse(body);
            string name = doc.RootElement.TryGetProperty("name", out var n) ? n.GetString() ?? "webhook" : "webhook";
            return new WebhookResult(true, name);
        }
        catch (Exception ex)
        {
            return new WebhookResult(false, Network(ex));
        }
    }

    public static async Task<WebhookResult> TestAsync(string url, string ping)
    {
        var check = await CheckAsync(url);
        if (!check.Ok) return check;

        string who = PingName(ping);
        var hello = new
        {
            username = BotName,
            avatar_url = Avatar,
            content = "Aether is connected",
            allowed_mentions = NoMentions,
            embeds = new[]
            {
                new
                {
                    color = Red,
                    author = new { name = "Aether", icon_url = Avatar },
                    title = "Webhook connected",
                    description = "Available usernames will show up here the moment they are found."
                        + (who != "" ? $"\nEach alert pings **{who}**." : "")
                        + "\nHere is what an alert looks like:",
                    thumbnail = new { url = Avatar },
                    footer = Footer(),
                    timestamp = Now()
                }
            }
        };
        var sent = await PostAsync(url, hello);
        if (!sent.Ok) return sent;

        var example = Single(new Hit("abc", 1, 120, 1000, "Letters Only"), "", "Example alert, nothing was found yet");
        sent = await PostAsync(url, example);
        return sent.Ok ? new WebhookResult(true, $"Test sent to \"{check.Message}\". Check your Discord channel.") : sent;
    }

    public void Post(Hit hit) => _queue.Writer.TryWrite(hit);

    // Sends whatever is still queued, then a summary of the run.
    public async Task FinishAsync(int done, int total, int available, int taken, int failed, TimeSpan time, bool stopped, IReadOnlyCollection<string> names)
    {
        _queue.Writer.TryComplete();
        await Task.WhenAny(_worker, Task.Delay(TimeSpan.FromSeconds(30)));

        string list = names.Count == 0
            ? "Nothing available this time."
            : "```\n" + string.Join("\n", names.Take(40)) + (names.Count > 40 ? $"\n+{names.Count - 40} more" : "") + "\n```";

        var payload = Message(
            available > 0 ? $"Run {(stopped ? "stopped" : "finished")}: {available:N0} available" : null,
            new
            {
                color = available > 0 ? Green : Gray,
                author = new { name = "Aether", icon_url = Avatar },
                title = stopped ? "Run stopped" : "Run finished",
                description = list,
                thumbnail = new { url = Avatar },
                fields = new[]
                {
                    Field("Available", available.ToString("N0")),
                    Field("Taken", taken.ToString("N0")),
                    Field("Failed", failed.ToString("N0")),
                    Field("Checked", $"{done:N0} / {total:N0}"),
                    Field("Time", time.ToString(@"hh\:mm\:ss")),
                    Field("Speed", $"{done / Math.Max(0.001, time.TotalSeconds):0.0}/s"),
                },
                footer = Footer(),
                timestamp = Now()
            });
        await SendAsync(payload);
    }

    private async Task WorkAsync()
    {
        var reader = _queue.Reader;
        var batch = new List<Hit>(50);
        while (await reader.WaitToReadAsync())
        {
            long wait = Interlocked.Read(ref _readyAt) - Environment.TickCount64;
            if (wait > 0)
            {
                if (wait > 1500 && Interlocked.Exchange(ref _paced, 1) == 0)
                    Term.Line(("  [~] ", Tones.Gray), ("Discord is pacing alerts. Names found meanwhile are sent together.", Tones.Gray));
                await Task.Delay((int)Math.Min(wait, 60_000));
            }

            batch.Clear();
            while (batch.Count < 50 && reader.TryRead(out var hit)) batch.Add(hit);
            if (batch.Count == 0) continue;

            if (await SendAsync(batch.Count == 1 ? Single(batch[0], _ping) : Group(batch, _ping)))
            {
                long now = Environment.TickCount64;
                foreach (var hit in batch)
                {
                    long delay = now - hit.FoundAt;
                    _delayTotal += delay;
                    _delayCount++;
                    if (delay > _delayMax) _delayMax = delay;
                }
            }
        }
    }

    private const string ClaimHint = "Claim it in **User Settings > My Account > Username**.";

    private static bool Rare(string name) => name.Length <= 3;

    private static object Single(Hit h, string ping, string? note = null)
    {
        bool rare = Rare(h.Name);
        return new
        {
            username = BotName,
            avatar_url = Avatar,
            content = note ?? $"{PingText(ping)}`{h.Name}` is available",
            allowed_mentions = Mentions(ping),
            embeds = new[]
            {
                new
                {
                    color = rare ? Gold : Green,
                    author = new { name = rare ? "Rare username available" : "Username available", icon_url = Avatar },
                    description = $"# `{h.Name}`\n{ClaimHint}",
                    thumbnail = new { url = Avatar },
                    fields = new[]
                    {
                        Field("Length", $"{h.Name.Length} characters"),
                        Field("Pattern", h.Source),
                        Field("Found", $"#{h.Found} this run"),
                        Field("Progress", Progress(h)),
                    },
                    footer = Footer(),
                    timestamp = Now()
                }
            }
        };
    }

    private static object Group(List<Hit> hits, string ping)
    {
        var last = hits[^1];
        bool rare = hits.Any(h => Rare(h.Name));
        var ordered = hits.OrderBy(h => h.Name.Length).ThenBy(h => h.Name).ToList();
        string preview = string.Join(", ", ordered.Take(20).Select(h => $"`{h.Name}`")) + (hits.Count > 20 ? $" +{hits.Count - 20} more" : "");
        return new
        {
            username = BotName,
            avatar_url = Avatar,
            content = $"{PingText(ping)}{preview} are available",
            allowed_mentions = Mentions(ping),
            embeds = new[]
            {
                new
                {
                    color = rare ? Gold : Green,
                    author = new { name = $"{hits.Count} usernames available" + (rare ? ", including rare ones" : ""), icon_url = Avatar },
                    description = "## " + string.Join("  ", ordered.Select(h => $"`{h.Name}`")) + "\nClaim one in **User Settings > My Account > Username**.",
                    thumbnail = new { url = Avatar },
                    fields = new[]
                    {
                        Field("Pattern", last.Source),
                        Field("Found", $"#{hits[0].Found} to #{last.Found} this run"),
                        Field("Progress", Progress(last)),
                    },
                    footer = Footer(),
                    timestamp = Now()
                }
            }
        };
    }

    private static string Progress(Hit h) =>
        $"{h.Done:N0} / {h.Total:N0} ({(h.Total == 0 ? 100 : h.Done * 100 / h.Total)}%)";

    // ping is "", "here", "everyone" or a Discord user ID.
    public static string NormalizePing(string? raw)
    {
        var p = (raw ?? "").Trim().ToLowerInvariant().Trim('<', '>', '@', '!');
        if (p is "" or "none" or "-" or "no") return "";
        if (p is "here" or "everyone") return p;
        return p.All(char.IsDigit) && p.Length is >= 15 and <= 21 ? p : "";
    }

    public static string PingName(string ping) => ping switch
    {
        "" => "",
        "here" or "everyone" => "@" + ping,
        _ => "you"
    };

    private static string PingText(string ping) => ping switch
    {
        "" => "",
        "here" or "everyone" => $"@{ping} ",
        _ => $"<@{ping}> "
    };

    private static readonly object NoMentions = new { parse = Array.Empty<string>() };

    private static object Mentions(string ping) => ping switch
    {
        "" => NoMentions,
        "here" or "everyone" => new { parse = new[] { "everyone" } },
        _ => (object)new { parse = Array.Empty<string>(), users = new[] { ping } }
    };

    private async Task<bool> SendAsync(object payload)
    {
        long wait = Interlocked.Read(ref _readyAt) - Environment.TickCount64;
        if (wait > 0) await Task.Delay((int)Math.Min(wait, 60_000));

        var result = await PostAsync(_url, payload, wait: false, limit: Hold);
        if (result.Ok)
        {
            Interlocked.Increment(ref _sent);
            return true;
        }
        Interlocked.Increment(ref _failed);
        LastError = result.Message;
        if (Interlocked.Exchange(ref _warned, 1) == 0)
            Term.Warn("Webhook failed: " + result.Message);
        return false;
    }

    // Remembers when Discord's rate limit window opens again.
    private void Hold(TimeSpan delay)
    {
        long at = Environment.TickCount64 + (long)delay.TotalMilliseconds;
        long current;
        do current = Interlocked.Read(ref _readyAt);
        while (at > current && Interlocked.CompareExchange(ref _readyAt, at, current) != current);
    }

    private static async Task<WebhookResult> PostAsync(string url, object payload, bool wait = true, Action<TimeSpan>? limit = null)
    {
        url = Clean(url);
        string target = wait ? url + (url.Contains('?') ? "&" : "?") + "wait=true" : url;
        string json = JsonSerializer.Serialize(payload, Json);

        for (int attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var res = await Client.PostAsync(target, content);
                if (limit != null && Header(res, "X-RateLimit-Remaining") == "0"
                    && double.TryParse(Header(res, "X-RateLimit-Reset-After"), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var reset))
                    limit(TimeSpan.FromSeconds(reset));
                if (res.IsSuccessStatusCode) return new WebhookResult(true, "sent");

                string body = await res.Content.ReadAsStringAsync();
                if (res.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    var retry = RetryAfter(res, body);
                    limit?.Invoke(retry);
                    await Task.Delay(retry);
                    continue;
                }
                if ((int)res.StatusCode >= 500)
                {
                    await Task.Delay(500 * attempt);
                    continue;
                }
                return new WebhookResult(false, Explain(res.StatusCode, body));
            }
            catch (Exception ex)
            {
                if (attempt == 5) return new WebhookResult(false, Network(ex));
                await Task.Delay(500 * attempt);
            }
        }
        return new WebhookResult(false, "Discord kept rate limiting the webhook.");
    }

    private static string? Header(HttpResponseMessage res, string name) =>
        res.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private static object Message(string? content, object embed) =>
        new { username = BotName, avatar_url = Avatar, content, allowed_mentions = NoMentions, embeds = new[] { embed } };

    private static object Field(string name, string value) => new { name, value, inline = true };

    private static object Footer() => new { text = "Aether  •  made by slizsy", icon_url = Avatar };

    private static string Now() => DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");

    private static TimeSpan RetryAfter(HttpResponseMessage res, string body)
    {
        double seconds = 1;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("retry_after", out var r)) seconds = r.GetDouble();
        }
        catch
        {
            if (res.Headers.RetryAfter?.Delta is { } d) seconds = d.TotalSeconds;
        }
        return TimeSpan.FromSeconds(Math.Clamp(seconds, 0.25, 30));
    }

    private static string Explain(HttpStatusCode status, string body)
    {
        string? message = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("message", out var m)) message = m.GetString();
        }
        catch { }

        return (int)status switch
        {
            401 => "the webhook token is wrong. Copy the full URL again from Discord.",
            404 => "webhook not found. It was deleted or the URL is incomplete.",
            _ => $"Discord answered {(int)status}" + (message != null ? $": {message}" : ".")
        };
    }

    private static string Network(Exception ex) => ex switch
    {
        TaskCanceledException => "Discord did not answer in time.",
        HttpRequestException => "could not reach Discord. Check your internet connection.",
        _ => ex.Message
    };
}
