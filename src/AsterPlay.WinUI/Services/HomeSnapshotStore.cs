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


internal static class HomeSnapshotComparer
{
    public static bool SnapshotDataEquals(
        HomeSnapshot? cached,
        HomeSnapshot live)
    {
        if (cached is null)
            return false;

        return ItemsEqual(cached.Latest, live.Latest) &&
               ItemsEqual(cached.Resume, live.Resume) &&
               ItemsEqual(cached.Views, live.Views) &&
               SectionsEqual(cached.Sections, live.Sections);
    }

    public static bool ItemsEqual(
        IReadOnlyList<EmbyItem>? left,
        IReadOnlyList<EmbyItem>? right)
    {
        left ??= Array.Empty<EmbyItem>();
        right ??= Array.Empty<EmbyItem>();

        if (left.Count != right.Count)
            return false;

        for (var index = 0; index < left.Count; index++)
        {
            if (!ItemEquals(left[index], right[index]))
                return false;
        }

        return true;
    }

    public static bool SectionEquals(
        HomeSectionSnapshot? left,
        HomeSectionSnapshot? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null)
            return false;

        return left.TotalCount == right.TotalCount &&
               ItemEquals(left.Library, right.Library) &&
               ItemsEqual(left.Items, right.Items);
    }

    public static bool SectionsEqual(
        IReadOnlyList<HomeSectionSnapshot>? left,
        IReadOnlyList<HomeSectionSnapshot>? right)
    {
        left ??= Array.Empty<HomeSectionSnapshot>();
        right ??= Array.Empty<HomeSectionSnapshot>();

        if (left.Count != right.Count)
            return false;

        for (var index = 0; index < left.Count; index++)
        {
            if (!SectionEquals(left[index], right[index]))
                return false;
        }

        return true;
    }

    public static bool ItemEquals(
        EmbyItem? left,
        EmbyItem? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null)
            return false;

        return
            StringEquals(left.Id, right.Id) &&
            StringEquals(left.Name, right.Name) &&
            StringEquals(left.Type, right.Type) &&
            StringEquals(left.MediaType, right.MediaType) &&
            StringEquals(left.CollectionType, right.CollectionType) &&
            StringEquals(left.Overview, right.Overview) &&
            left.ProductionYear == right.ProductionYear &&
            left.CommunityRating == right.CommunityRating &&
            left.CriticRating == right.CriticRating &&
            StringEquals(left.OfficialRating, right.OfficialRating) &&
            left.RunTimeTicks == right.RunTimeTicks &&
            StringEquals(left.SeriesId, right.SeriesId) &&
            StringEquals(left.SeriesName, right.SeriesName) &&
            left.SeasonCount == right.SeasonCount &&
            left.ChildCount == right.ChildCount &&
            left.ParentIndexNumber == right.ParentIndexNumber &&
            left.IndexNumber == right.IndexNumber &&
            left.PrimaryImageAspectRatio == right.PrimaryImageAspectRatio &&
            StringEquals(left.ParentBackdropItemId, right.ParentBackdropItemId) &&
            StringSequenceEquals(left.Genres, right.Genres) &&
            StringSequenceEquals(left.BackdropImageTags, right.BackdropImageTags) &&
            StringSequenceEquals(left.ParentBackdropImageTags, right.ParentBackdropImageTags) &&
            DictionaryEquals(left.ImageTags, right.ImageTags) &&
            UserDataEquals(left.UserData, right.UserData);
    }

    private static bool UserDataEquals(
        EmbyUserData? left,
        EmbyUserData? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null)
            return false;

        return left.IsFavorite == right.IsFavorite &&
               left.Played == right.Played &&
               left.PlayCount == right.PlayCount &&
               left.PlayedPercentage == right.PlayedPercentage &&
               left.PlaybackPositionTicks == right.PlaybackPositionTicks &&
               left.LastPlayedDate == right.LastPlayedDate;
    }

    private static bool StringSequenceEquals(
        IReadOnlyList<string>? left,
        IReadOnlyList<string>? right)
    {
        left ??= Array.Empty<string>();
        right ??= Array.Empty<string>();

        if (left.Count != right.Count)
            return false;

        for (var index = 0; index < left.Count; index++)
        {
            if (!StringEquals(left[index], right[index]))
                return false;
        }

        return true;
    }

    private static bool DictionaryEquals(
        IReadOnlyDictionary<string, string>? left,
        IReadOnlyDictionary<string, string>? right)
    {
        left ??= new Dictionary<string, string>();
        right ??= new Dictionary<string, string>();

        if (left.Count != right.Count)
            return false;

        foreach (var pair in left)
        {
            if (!right.TryGetValue(pair.Key, out var value) ||
                !StringEquals(pair.Value, value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool StringEquals(string? left, string? right) =>
        string.Equals(
            left ?? "",
            right ?? "",
            StringComparison.Ordinal);
}
