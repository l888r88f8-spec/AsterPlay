using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AsterPlay.Services.Danmaku;

public static class DanmakuMatchOverrideStore
{
    private static readonly object Sync = new();

    private static readonly string DirectoryPath =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AsterPlay");

    private static readonly string SettingsPath =
        Path.Combine(
            DirectoryPath,
            "danmaku-match-overrides.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static bool TryGet(
        string baseUrl,
        string itemId,
        out DanmakuMatchCandidate? candidate)
    {
        candidate = null;

        if (string.IsNullOrWhiteSpace(baseUrl) ||
            string.IsNullOrWhiteSpace(itemId))
        {
            return false;
        }

        lock (Sync)
        {
            var data = LoadUnsafe();
            if (!data.TryGetValue(
                    BuildKey(baseUrl, itemId),
                    out var stored))
            {
                return false;
            }

            candidate = new DanmakuMatchCandidate(
                stored.EpisodeId,
                stored.AnimeTitle ?? "",
                stored.EpisodeTitle ?? "",
                stored.SeasonNumber,
                stored.EpisodeNumber,
                stored.Score,
                "manual");

            return candidate.EpisodeId > 0;
        }
    }

    public static void Save(
        string baseUrl,
        string itemId,
        DanmakuMatchCandidate candidate)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) ||
            string.IsNullOrWhiteSpace(itemId) ||
            candidate.EpisodeId <= 0)
        {
            return;
        }

        lock (Sync)
        {
            var data = LoadUnsafe();
            data[BuildKey(baseUrl, itemId)] =
                new StoredManualMatch
                {
                    EpisodeId = candidate.EpisodeId,
                    AnimeTitle = candidate.AnimeTitle,
                    EpisodeTitle = candidate.EpisodeTitle,
                    SeasonNumber = candidate.SeasonNumber,
                    EpisodeNumber = candidate.EpisodeNumber,
                    Score = candidate.Score
                };

            SaveUnsafe(data);
        }
    }

    public static bool Remove(
        string baseUrl,
        string itemId)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) ||
            string.IsNullOrWhiteSpace(itemId))
        {
            return false;
        }

        lock (Sync)
        {
            var data = LoadUnsafe();
            if (!data.Remove(BuildKey(baseUrl, itemId)))
                return false;

            SaveUnsafe(data);
            return true;
        }
    }

    private static Dictionary<string, StoredManualMatch> LoadUnsafe()
    {
        if (!File.Exists(SettingsPath))
        {
            return new Dictionary<string, StoredManualMatch>(
                StringComparer.Ordinal);
        }

        try
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<
                       Dictionary<string, StoredManualMatch>>(
                       json,
                       JsonOptions)
                   ?? new Dictionary<string, StoredManualMatch>(
                       StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            PlaybackLog.Error("DanmakuMatchOverrideLoad", ex);
            return new Dictionary<string, StoredManualMatch>(
                StringComparer.Ordinal);
        }
    }

    private static void SaveUnsafe(
        Dictionary<string, StoredManualMatch> data)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);

            var tempPath = SettingsPath + ".tmp";
            File.WriteAllText(
                tempPath,
                JsonSerializer.Serialize(
                    data,
                    JsonOptions));

            File.Move(
                tempPath,
                SettingsPath,
                overwrite: true);
        }
        catch (Exception ex)
        {
            PlaybackLog.Error("DanmakuMatchOverrideSave", ex);
        }
    }

    private static string BuildKey(
        string baseUrl,
        string itemId)
    {
        var normalized =
            $"{baseUrl.Trim().TrimEnd('/').ToLowerInvariant()}\n{itemId.Trim()}";
        var hash = SHA256.HashData(
            Encoding.UTF8.GetBytes(normalized));

        return Convert.ToHexString(hash);
    }

    private sealed class StoredManualMatch
    {
        public long EpisodeId { get; set; }
        public string? AnimeTitle { get; set; }
        public string? EpisodeTitle { get; set; }
        public int SeasonNumber { get; set; }
        public int EpisodeNumber { get; set; }
        public double Score { get; set; }
    }
}
