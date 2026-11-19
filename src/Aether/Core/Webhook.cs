using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace Aether;

public sealed record Hit(string Name, int Found, int Done, int Total, string Source);

public readonly record struct WebhookResult(bool Ok, string Message);

// Posts finds to a Discord webhook from a background queue, so checks never wait on Discord.
// Finds that pile up are batched (up to 10 per message) and rate limits are retried.
public sealed class Webhook
{
    public const string Avatar = "https://raw.githubusercontent.com/slizsy/Aether/main/assets/avatar.png";

    private const string BotName = "Aether";
    private const int Green = 0x22C55E;
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
    private readonly Channel<Hit> _queue = Channel.CreateUnbounded<Hit>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Task _worker;
    private int _sent;
    private int _failed;
    private int _warned;

    public Webhook(string url)
    {
        _url = url;
        _worker = Task.Run(WorkAsync);
    }

    public int Sent => _sent;
    public int Failed => _failed;
    public string? LastError { get; private set; }

    public static string Clean(string url) => url.Trim().Trim('"', '\'', '<', '>');

    public static bool IsValid(string url) => UrlShape.IsMatch(Clean(url));

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(10) };
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

    public static async Task<WebhookResult> TestAsync(string url)
    {
        var check = await CheckAsync(url);
        if (!check.Ok) return check;

        var payload = Message(
            "Aether is connected",
            new
            {
                color = Red,
                author = new { name = "Aether", icon_url = Avatar },
                title = "Webhook connected",
                description = "Available usernames will show up here the moment they are found.",
                footer = Footer(),
                timestamp = Now()
            });
        var sent = await PostAsync(url, payload);
        return sent.Ok ? new WebhookResult(true, $"Test message sent to \"{check.Message}\". Check your Discord channel.") : sent;
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
            null,
            new
            {
                color = available > 0 ? Green : Gray,
                author = new { name = "Aether", icon_url = Avatar },
                title = stopped ? "Run stopped" : "Run finished",
                description = list,
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
        var batch = new List<Hit>(10);
        while (await reader.WaitToReadAsync())
        {
            batch.Clear();
            while (batch.Count < 10 && reader.TryRead(out var hit)) batch.Add(hit);
            if (batch.Count == 0) continue;

            string names = string.Join(", ", batch.Select(h => $"`{h.Name}`"));
            string content = batch.Count == 1 ? $"{names} is available" : $"{names} are available";
            var embeds = batch.Select(h => (object)new
            {
                color = Green,
                author = new { name = "Username available", icon_url = Avatar },
                description = $"# `{h.Name}`\nGrab it before someone else does.",
                fields = new[]
                {
                    Field("Pattern", h.Source),
                    Field("Found", $"#{h.Found} this run"),
                    Field("Progress", $"{h.Done:N0} / {h.Total:N0}"),
                },
                footer = Footer(),
                timestamp = Now()
            }).ToArray();

            await SendAsync(new { username = BotName, avatar_url = Avatar, content, embeds });
        }
    }

    private async Task SendAsync(object payload)
    {
        var result = await PostAsync(_url, payload);
        if (result.Ok)
        {
            Interlocked.Increment(ref _sent);
            return;
        }
        Interlocked.Increment(ref _failed);
        LastError = result.Message;
        if (Interlocked.Exchange(ref _warned, 1) == 0)
            Term.Warn("Webhook failed: " + result.Message);
    }

    private static async Task<WebhookResult> PostAsync(string url, object payload)
    {
        url = Clean(url);
        string target = url + (url.Contains('?') ? "&" : "?") + "wait=true";
        string json = JsonSerializer.Serialize(payload, Json);

        for (int attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var res = await Client.PostAsync(target, content);
                if (res.IsSuccessStatusCode) return new WebhookResult(true, "sent");

                string body = await res.Content.ReadAsStringAsync();
                if (res.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    await Task.Delay(RetryAfter(res, body));
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

    private static object Message(string? content, object embed) =>
        new { username = BotName, avatar_url = Avatar, content, embeds = new[] { embed } };

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
