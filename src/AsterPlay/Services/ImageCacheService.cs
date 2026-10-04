using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;

namespace AsterPlay.Services;

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
    private readonly ConcurrentDictionary<string, WeakReference<BitmapSource>> _memory = new();
    private readonly ConcurrentDictionary<string, Lazy<Task<BitmapSource?>>> _inflight = new();
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

    public async Task<BitmapSource?> GetAsync(string? url, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var key = CreateCacheKey(url);

        if (_memory.TryGetValue(key, out var weak) &&
            weak.TryGetTarget(out var cached))
        {
            return cached;
        }

        var lazy = _inflight.GetOrAdd(
            key,
            _ => new Lazy<Task<BitmapSource?>>(
                () => LoadOrDownloadAsync(url, key),
                LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            var image = await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (image is not null)
                _memory[key] = new WeakReference<BitmapSource>(image);
            return image;
        }
        finally
        {
            if (lazy.IsValueCreated && lazy.Value.IsCompleted)
                _inflight.TryRemove(new KeyValuePair<string, Lazy<Task<BitmapSource?>>>(key, lazy));
        }
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
                try { File.Delete(file); }
                catch { }
            }
        }).ConfigureAwait(false);
    }

    private async Task<BitmapSource?> LoadOrDownloadAsync(string url, string key)
    {
        var cachePath = Path.Combine(_cacheRoot, key + ".img");

        var diskImage = await TryLoadDiskAsync(cachePath).ConfigureAwait(false);
        if (diskImage is not null)
        {
            Touch(cachePath);
            PlaybackLog.Write("ImageCache", $"disk hit: {key[..12]}");
            return diskImage;
        }

        await _downloadGate.WaitAsync().ConfigureAwait(false);

        try
        {
            // Another merged request may have completed while this request was
            // waiting for a download slot.
            diskImage = await TryLoadDiskAsync(cachePath).ConfigureAwait(false);
            if (diskImage is not null)
                return diskImage;

            PlaybackLog.Write("ImageCache", $"download: {key[..12]}");

            using var response = await _http.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength is > MaxImageBytes)
                throw new InvalidDataException("Image exceeds the 32 MB cache limit.");

            await using var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var buffer = new MemoryStream();

            var chunk = new byte[64 * 1024];
            var total = 0;

            while (true)
            {
                var read = await source.ReadAsync(chunk).ConfigureAwait(false);
                if (read <= 0)
                    break;

                total += read;
                if (total > MaxImageBytes)
                    throw new InvalidDataException("Image exceeds the 32 MB cache limit.");

                await buffer.WriteAsync(chunk.AsMemory(0, read)).ConfigureAwait(false);
            }

            if (buffer.Length == 0)
                return null;

            var bytes = buffer.ToArray();
            var bitmap = Decode(bytes);
            if (bitmap is null)
                return null;

            var tempPath = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(tempPath, bytes).ConfigureAwait(false);
                File.Move(tempPath, cachePath, overwrite: true);
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch { }
            }

            return bitmap;
        }
        catch (Exception ex)
        {
            PlaybackLog.Write(
                "ImageCache",
                $"load failed: key={key[..12]}, error={ex.GetType().Name}: {ex.Message}");
            return null;
        }
        finally
        {
            _downloadGate.Release();
        }
    }

    private static async Task<BitmapSource?> TryLoadDiskAsync(string path)
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

            var bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
            return Decode(bytes);
        }
        catch
        {
            try { File.Delete(path); }
            catch { }
            return null;
        }
    }

    private static BitmapSource? Decode(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
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
                var remove = file.LastWriteTimeUtc < cutoff ||
                             retainedBytes + file.Length > MaxDiskCacheBytes;

                if (remove)
                {
                    try { file.Delete(); }
                    catch { }
                    continue;
                }

                retainedBytes += file.Length;
            }

            PlaybackLog.Write(
                "ImageCache",
                $"cleanup complete: retained={retainedBytes / (1024d * 1024d):0.0} MB");
        }
        catch (Exception ex)
        {
            PlaybackLog.Error("ImageCacheCleanup", ex);
        }

        await Task.CompletedTask;
    }

    private static void Touch(string path)
    {
        try { File.SetLastWriteTimeUtc(path, DateTime.UtcNow); }
        catch { }
    }

    private static string CreateCacheKey(string url)
    {
        var canonical = Canonicalize(url);
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    private static string Canonicalize(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return url;

        var query = uri.Query
            .TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(part =>
            {
                var name = part.Split('=', 2)[0];
                return !string.Equals(name, "api_key", StringComparison.OrdinalIgnoreCase) &&
                       !string.Equals(name, "X-Emby-Token", StringComparison.OrdinalIgnoreCase);
            });

        var builder = new UriBuilder(uri)
        {
            Query = string.Join("&", query),
            Fragment = ""
        };

        return builder.Uri.AbsoluteUri;
    }
}
