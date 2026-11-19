using System.Text;
using System.Text.Json;

namespace Aether;

public static class Webhook
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(10) };

    public static bool IsValid(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && (uri.Host.EndsWith("discord.com", StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith("discordapp.com", StringComparison.OrdinalIgnoreCase))
            && uri.AbsolutePath.StartsWith("/api/webhooks/", StringComparison.OrdinalIgnoreCase);
    }

    public static async Task SendAsync(string url, string username, int foundThisSession)
    {
        try
        {
            var payload = new
            {
                username = "Aether",
                embeds = new[]
                {
                    new
                    {
                        title = "Username available",
                        description = "`" + username + "`",
                        color = 0xEF4444,
                        timestamp = DateTime.UtcNow.ToString("o"),
                        fields = new[]
                        {
                            new { name = "Found this session", value = foundThisSession.ToString(), inline = true }
                        },
                        footer = new { text = "Aether, made by slizsy" }
                    }
                }
            };
            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var res = await Client.PostAsync(url, content);
        }
        catch { }
    }
}
