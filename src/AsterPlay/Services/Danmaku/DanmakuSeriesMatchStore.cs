using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AsterPlay.Models.Danmaku;

namespace AsterPlay.Services.Danmaku;

public static class DanmakuSeriesMatchStore
{
    private static readonly object Sync = new();

    private static readonly string DirectoryPath =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AsterPlay");

    private static readonly string SettingsPath =
        Path.Combine(
            DirectoryPath,
            "danmaku-series-overrides.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static bool TryGet(
        string baseUrl,
        DanmakuContext context,
        out DanmakuSeriesMatchBinding? binding)
    {
        binding = null;

        var seriesKey = GetSeriesKey(context);
        if (string.IsNullOrWhiteSpace(baseUrl) ||
            string.IsNullOrWhiteSpace(seriesKey))
        {
            return false;
        }

        lock (Sync)
        {
            var data = LoadUnsafe();
            if (!data.TryGetValue(
                    BuildKey(
                        baseUrl,
                        seriesKey,
                        context.SeasonNumber),
                    out var stored))
            {
                return false;
            }

            binding = new DanmakuSeriesMatchBinding(
                stored.AnimeId,
                stored.AnimeTitle ?? "",
                stored.LogVarSeasonNumber,
                stored.EpisodeOffset);

            return !string.IsNullOrWhiteSpace(binding.AnimeTitle);
        }
    }

    public static void Save(
        string baseUrl,
        DanmakuContext context,
        DanmakuSeriesMatchBinding binding)
    {
        var seriesKey = GetSeriesKey(context);
        if (string.IsNullOrWhiteSpace(baseUrl) ||
            string.IsNullOrWhiteSpace(seriesKey) ||
            string.IsNullOrWhiteSpace(binding.AnimeTitle))
        {
            return;
        }

        lock (Sync)
        {
            var data = LoadUnsafe();
            data[BuildKey(
                baseUrl,
                seriesKey,
                context.SeasonNumber)] =
                new StoredSeriesMatch
                {
                    AnimeId = binding.AnimeId,
                    AnimeTitle = binding.AnimeTitle,
                    LogVarSeasonNumber =
                        binding.LogVarSeasonNumber,
                    EpisodeOffset =
                        binding.EpisodeOffset
                };

            SaveUnsafe(data);
        }
    }

    public static bool Remove(
        string baseUrl,
        DanmakuContext context)
    {
        var seriesKey = GetSeriesKey(context);
        if (string.IsNullOrWhiteSpace(baseUrl) ||
            string.IsNullOrWhiteSpace(seriesKey))
        {
            return false;
        }

        lock (Sync)
        {
            var data = LoadUnsafe();
            if (!data.Remove(
                    BuildKey(
                        baseUrl,
                        seriesKey,
                        context.SeasonNumber)))
            {
                return false;
            }

            SaveUnsafe(data);
            return true;
        }
    }

    private static string GetSeriesKey(
        DanmakuContext context)
    {
        if (DanmakuApiSupport.IsEpisode(context))
        {
            if (!string.IsNullOrWhiteSpace(context.SeriesId))
                return $"series:{context.SeriesId.Trim()}";

            if (!string.IsNullOrWhiteSpace(context.SeriesName))
                return $"series-name:{context.SeriesName.Trim()}";
        }

        return string.IsNullOrWhiteSpace(context.ItemId)
            ? ""
            : $"item:{context.ItemId.Trim()}";
    }

    private static Dictionary<string, StoredSeriesMatch> LoadUnsafe()
    {
        if (!File.Exists(SettingsPath))
        {
            return new Dictionary<string, StoredSeriesMatch>(
                StringComparer.Ordinal);
        }

        try
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<
                       Dictionary<string, StoredSeriesMatch>>(
                       json,
                       JsonOptions)
                   ?? new Dictionary<string, StoredSeriesMatch>(
                       StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            PlaybackLog.Error("DanmakuSeriesMatchLoad", ex);
            return new Dictionary<string, StoredSeriesMatch>(
                StringComparer.Ordinal);
        }
    }

    private static void SaveUnsafe(
        Dictionary<string, StoredSeriesMatch> data)
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
            PlaybackLog.Error("DanmakuSeriesMatchSave", ex);
        }
    }

    private static string BuildKey(
        string baseUrl,
        string seriesKey,
        int? seasonNumber)
    {
        var normalized =
            $"{baseUrl.Trim().TrimEnd('/').ToLowerInvariant()}\n" +
            $"{seriesKey.Trim()}\n" +
            $"season:{seasonNumber.GetValueOrDefault(0)}";

        var hash = SHA256.HashData(
            Encoding.UTF8.GetBytes(normalized));

        return Convert.ToHexString(hash);
    }

    private sealed class StoredSeriesMatch
    {
        public long AnimeId { get; set; }
        public string? AnimeTitle { get; set; }
        public int LogVarSeasonNumber { get; set; }
        public int EpisodeOffset { get; set; }
    }
}
