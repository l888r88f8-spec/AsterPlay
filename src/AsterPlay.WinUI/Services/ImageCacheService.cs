using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using AsterPlay.Services;

namespace AsterPlay.WinUI.Services;

public sealed class ImageCacheService
{
    private const long MaxDiskCacheBytes = 512L * 1024L * 1024L;
    private static readonly TimeSpan MaxDiskAge = TimeSpan.FromDays(60);
    private const int MaxImageBytes = 32 * 1024 * 1024;

    private readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(12)
    };

    private readonly string _cacheRoot;
    private readonly ConcurrentDictionary<string, WeakReference<byte[]>> _memory = new();
    private readonly ConcurrentDictionary<string, Lazy<Task<byte[]?>>> _inflight = new();
    private readonly SemaphoreSlim _downloadGate = new(6, 6);

    public static ImageCacheService Shared { get; } = new();

    private ImageCacheService()
    {
        _cacheRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AsterPlay",
            "cache",
            "images");

        Directory.CreateDirectory(_cacheRoot);
        _ = Task.Run(CleanupAsync);
    }

    public async Task<byte[]?> GetBytesAsync(
        string? url,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var key = CreateCacheKey(url);

        if (_memory.TryGetValue(key, out var weak) &&
            weak.TryGetTarget(out var cached))
        {
            PlaybackLog.Write("WinUIImageCache", $"memory hit: {key[..12]}");
            return cached;
        }

        var lazy = _inflight.GetOrAdd(
            key,
            _ => new Lazy<Task<byte[]?>>(
                () => LoadAndRememberAsync(url, key),
                LazyThreadSafetyMode.ExecutionAndPublication));

        return await lazy.Value.WaitAsync(cancellationToken);
    }

    public async Task PreloadAsync(
        IEnumerable<string?> urls,
        CancellationToken cancellationToken = default)
    {
        var targets = urls
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => url!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (targets.Length == 0)
            return;

        await Task.WhenAll(
            targets.Select(url => GetBytesAsync(url, cancellationToken)));
    }

    public async Task ClearAsync()
    {
        _memory.Clear();

        if (!Directory.Exists(_cacheRoot))
            return;

        await Task.Run(() =>
        {
            foreach (var file in Directory.EnumerateFiles(_cacheRoot, "*.img"))
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                }
            }
        });
    }

    public void Invalidate(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        var key = CreateCacheKey(url);
        _memory.TryRemove(key, out _);

        try
        {
            var path = Path.Combine(_cacheRoot, key + ".img");
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private async Task<byte[]?> LoadAndRememberAsync(
        string url,
        string key)
    {
        try
        {
            var bytes = await LoadOrDownloadAsync(url, key);
            if (bytes is not null)
                _memory[key] = new WeakReference<byte[]>(bytes);

            return bytes;
        }
        finally
        {
            _inflight.TryRemove(key, out _);
        }
    }

    private async Task<byte[]?> LoadOrDownloadAsync(
        string url,
        string key)
    {
        var cachePath = Path.Combine(_cacheRoot, key + ".img");

        var diskBytes = await TryLoadDiskAsync(cachePath);
        if (diskBytes is not null)
        {
            Touch(cachePath);
            PlaybackLog.Write("WinUIImageCache", $"disk hit: {key[..12]}");
            return diskBytes;
        }

        await _downloadGate.WaitAsync();

        try
        {
            diskBytes = await TryLoadDiskAsync(cachePath);
            if (diskBytes is not null)
            {
                Touch(cachePath);
                return diskBytes;
            }

            PlaybackLog.Write("WinUIImageCache", $"download: {key[..12]}");

            using var response = await _http.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength is > MaxImageBytes)
            {
                throw new InvalidDataException(
                    "Image exceeds the 32 MB cache limit.");
            }

            await using var source =
                await response.Content.ReadAsStreamAsync();
            using var buffer = new MemoryStream();

            var chunk = new byte[64 * 1024];
            var total = 0;

            while (true)
            {
                var read = await source.ReadAsync(chunk);
                if (read <= 0)
                    break;

                total += read;
                if (total > MaxImageBytes)
                {
                    throw new InvalidDataException(
                        "Image exceeds the 32 MB cache limit.");
                }

                await buffer.WriteAsync(chunk.AsMemory(0, read));
            }

            if (buffer.Length == 0)
                return null;

            var bytes = buffer.ToArray();

            var tempPath =
                cachePath + "." +
                Guid.NewGuid().ToString("N") +
                ".tmp";

            try
            {
                await File.WriteAllBytesAsync(tempPath, bytes);
                File.Move(tempPath, cachePath, overwrite: true);
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch
                {
                }
            }

            return bytes;
        }
        catch (Exception ex)
        {
            PlaybackLog.Write(
                "WinUIImageCache",
                $"load failed: key={key[..12]}, error={ex.GetType().Name}: {ex.Message}");
            return null;
        }
        finally
        {
            _downloadGate.Release();
        }
    }

    private static async Task<byte[]?> TryLoadDiskAsync(
        string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            var info = new FileInfo(path);
            if (DateTime.UtcNow - info.LastWriteTimeUtc > MaxDiskAge)
            {
                File.Delete(path);
                return null;
            }

            if (info.Length <= 0 || info.Length > MaxImageBytes)
            {
                File.Delete(path);
                return null;
            }

            return await File.ReadAllBytesAsync(path);
        }
        catch
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
            }

            return null;
        }
    }

    private async Task CleanupAsync()
    {
        try
        {
            if (!Directory.Exists(_cacheRoot))
                return;

            var files = new DirectoryInfo(_cacheRoot)
                .EnumerateFiles("*.img")
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ToList();

            var cutoff = DateTime.UtcNow - MaxDiskAge;
            long retainedBytes = 0;

            foreach (var file in files)
            {
                var remove =
                    file.LastWriteTimeUtc < cutoff ||
                    file.Length <= 0 ||
                    file.Length > MaxImageBytes ||
                    retainedBytes + file.Length > MaxDiskCacheBytes;

                if (remove)
                {
                    try
                    {
                        file.Delete();
                    }
                    catch
                    {
                    }

                    continue;
                }

                retainedBytes += file.Length;
            }

            PlaybackLog.Write(
                "WinUIImageCache",
                $"cleanup complete: retained={retainedBytes / (1024d * 1024d):0.0} MB");
        }
        catch (Exception ex)
        {
            PlaybackLog.Error("WinUIImageCacheCleanup", ex);
        }

        await Task.CompletedTask;
    }

    private static void Touch(string path)
    {
        try
        {
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        }
        catch
        {
        }
    }

    private static string CreateCacheKey(string url)
    {
        var canonical = Canonicalize(url);
        return Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    private static string Canonicalize(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return url;

        var query = uri.Query
            .TrimStart('?')
            .Split(
                '&',
                StringSplitOptions.RemoveEmptyEntries)
            .Where(part =>
            {
                var name = part.Split('=', 2)[0];
                return !string.Equals(
                           name,
                           "api_key",
                           StringComparison.OrdinalIgnoreCase) &&
                       !string.Equals(
                           name,
                           "X-Emby-Token",
                           StringComparison.OrdinalIgnoreCase);
            });

        var builder = new UriBuilder(uri)
        {
            Query = string.Join("&", query),
            Fragment = ""
        };

        return builder.Uri.AbsoluteUri;
    }
}
