using System.Text;
using System.Text.Json;

namespace AsterPlay.Services;

public sealed record ServerProfile(string Name, string Url)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Url : Name;
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
                   ?? Array.Empty<ServerProfile>();
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

        var profile = new ServerProfile(
            string.IsNullOrWhiteSpace(name) ? GetDefaultName(url) : name.Trim(),
            url);

        if (index >= 0)
            profiles[index] = profile;
        else
            profiles.Add(profile);

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

    private static string GetDefaultName(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";

        return url;
    }
}
