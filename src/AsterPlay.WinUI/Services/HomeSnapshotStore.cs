using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AsterPlay.Models;
using AsterPlay.Services;

namespace AsterPlay.WinUI.Services;

internal static class HomeSnapshotStore
{
    private const int CurrentVersion = 1;
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);

    private static readonly string CacheDirectory =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AsterPlay",
            "cache",
            "home");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public static HomeSnapshot? Load(string serverUrl, string userId)
    {
        if (string.IsNullOrWhiteSpace(serverUrl) ||
            string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        var path = GetPath(serverUrl, userId);
        if (!File.Exists(path))
        {
            StartupDiagnostics.Write("HomeSnapshot: miss");
            return null;
        }

        try
        {
            using var timing = StartupDiagnostics.Measure("HomeSnapshot.Load");
            var snapshot = JsonSerializer.Deserialize<HomeSnapshot>(
                File.ReadAllText(path),
                JsonOptions);

            if (snapshot is null ||
                snapshot.Version != CurrentVersion ||
                DateTimeOffset.UtcNow - snapshot.SavedAtUtc > MaxAge)
            {
                TryDelete(path);
                return null;
            }

            StartupDiagnostics.Write(
                $"HomeSnapshot: hit; age={(DateTimeOffset.UtcNow - snapshot.SavedAtUtc).TotalMinutes:0.0} min, " +
                $"latest={snapshot.Latest.Count}, resume={snapshot.Resume.Count}, views={snapshot.Views.Count}, sections={snapshot.Sections.Count}");
            return snapshot;
        }
        catch (Exception ex)
        {
            PlaybackLog.Error("WinUIHomeSnapshotLoad", ex);
            TryDelete(path);
            return null;
        }
    }

    public static void Save(
        string serverUrl,
        string userId,
        HomeSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(serverUrl) ||
            string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(CacheDirectory);

            snapshot.Version = CurrentVersion;
            snapshot.SavedAtUtc = DateTimeOffset.UtcNow;

            var path = GetPath(serverUrl, userId);
            var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var json = JsonSerializer.Serialize(snapshot, JsonOptions);

            try
            {
                File.WriteAllText(tempPath, json, Encoding.UTF8);
                File.Move(tempPath, path, overwrite: true);
            }
            finally
            {
                TryDelete(tempPath);
            }
        }
        catch (Exception ex)
        {
            PlaybackLog.Error("WinUIHomeSnapshotSave", ex);
        }
    }

    private static string GetPath(string serverUrl, string userId)
    {
        var identity =
            serverUrl.Trim().TrimEnd('/').ToLowerInvariant() +
            "\n" +
            userId.Trim().ToLowerInvariant();

        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(identity)))
            .ToLowerInvariant();

        return Path.Combine(CacheDirectory, hash + ".json");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }
}

internal sealed class HomeSnapshot
{
    public int Version { get; set; } = 1;
    public DateTimeOffset SavedAtUtc { get; set; }
    public List<EmbyItem> Latest { get; set; } = [];
    public List<EmbyItem> Resume { get; set; } = [];
    public List<EmbyItem> Views { get; set; } = [];
    public List<HomeSectionSnapshot> Sections { get; set; } = [];
}

internal sealed class HomeSectionSnapshot
{
    public EmbyItem Library { get; set; } = new();
    public int TotalCount { get; set; }
    public List<EmbyItem> Items { get; set; } = [];
}
