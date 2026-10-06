using System.Text;
using System.Text.Json;

namespace AsterPlay.Services;

public sealed record ServerProfile(string Name, string Url)
{
    // Name is the label explicitly entered by the user. ServerName is the
    // name discovered from Emby's public system info. Never overwrite Name
    // during login or background server-name refresh.
    public string ServerName { get; init; } = "";

    public string DisplayName =>
        !string.IsNullOrWhiteSpace(Name)
            ? Name
            : !string.IsNullOrWhiteSpace(ServerName)
                ? ServerName
                : Url;
}

public static class ServerProfileStore
{
    private static readonly string DirectoryPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AsterPlay");

    private static readonly string FilePath = Path.Combine(DirectoryPath, "servers.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static IReadOnlyList<ServerProfile> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return Array.Empty<ServerProfile>();

            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<List<ServerProfile>>(json, JsonOptions)
                   ?? new List<ServerProfile>();
        }
        catch (Exception ex)
        {
            PlaybackLog.Error("ServerProfileLoad", ex);
            return Array.Empty<ServerProfile>();
        }
    }

    public static void AddOrUpdate(string url, string? name = null)
    {
        url = NormalizeUrl(url);
        if (string.IsNullOrWhiteSpace(url))
            return;

        var profiles = Load().ToList();
        var index = profiles.FindIndex(x =>
            string.Equals(NormalizeUrl(x.Url), url, StringComparison.OrdinalIgnoreCase));

        var existingServerName = index >= 0
            ? profiles[index].ServerName
            : "";

        var profile = new ServerProfile(
            (name ?? "").Trim(),
            url)
        {
            ServerName = existingServerName
        };

        if (index >= 0)
            profiles[index] = profile;
        else
            profiles.Add(profile);

        Save(profiles);
    }

    public static void UpdateServerName(string url, string? serverName)
    {
        url = NormalizeUrl(url);
        serverName = (serverName ?? "").Trim();

        if (string.IsNullOrWhiteSpace(url) ||
            string.IsNullOrWhiteSpace(serverName))
        {
            return;
        }

        var profiles = Load().ToList();
        var index = profiles.FindIndex(x =>
            string.Equals(NormalizeUrl(x.Url), url, StringComparison.OrdinalIgnoreCase));

        if (index >= 0)
        {
            profiles[index] = profiles[index] with
            {
                ServerName = serverName
            };
        }
        else
        {
            profiles.Add(new ServerProfile("", url)
            {
                ServerName = serverName
            });
        }

        Save(profiles);
    }

    public static void Remove(string url)
    {
        var normalized = NormalizeUrl(url);
        var profiles = Load()
            .Where(x => !string.Equals(
                NormalizeUrl(x.Url),
                normalized,
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        Save(profiles);
    }

    private static void Save(IReadOnlyList<ServerProfile> profiles)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(profiles, JsonOptions), Encoding.UTF8);
            File.Move(temp, FilePath, true);
        }
        catch (Exception ex)
        {
            PlaybackLog.Error("ServerProfileSave", ex);
        }
    }

    private static string NormalizeUrl(string? url) =>
        (url ?? "").Trim().TrimEnd('/');

}
