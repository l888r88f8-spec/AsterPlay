using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AsterPlay.Models;

namespace AsterPlay.Services;

public static class AppStateStore
{
    private const int CurrentFormatVersion = 2;

    private static readonly byte[] Entropy =
        Encoding.UTF8.GetBytes("AsterPlay.Session.v1");

    private static readonly string DirectoryPath =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AsterPlay");

    private static readonly string SessionPath =
        Path.Combine(DirectoryPath, "session.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static void Save(EmbySession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (string.IsNullOrWhiteSpace(session.AccessToken))
            throw new InvalidOperationException("Cannot persist an empty Emby access token.");

        Directory.CreateDirectory(DirectoryPath);

        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(session.AccessToken),
            Entropy,
            DataProtectionScope.CurrentUser);

        var stored = new StoredSession
        {
            FormatVersion = CurrentFormatVersion,
            ServerUrl = session.ServerUrl,
            UserId = session.UserId,
            DeviceId = session.DeviceId,
            UserName = session.UserName,
            AccessTokenProtected = Convert.ToBase64String(protectedBytes)
        };

        WriteAtomically(JsonSerializer.Serialize(stored, JsonOptions));
    }

    public static EmbySession? Load()
    {
        if (!File.Exists(SessionPath))
            return null;

        try
        {
            var json = File.ReadAllText(SessionPath);

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (TryReadEncryptedSession(json, root, out var encryptedSession))
                return encryptedSession;

            if (TryReadLegacyPlaintextSession(json, root, out var legacySession))
            {
                // One-time migration. The plaintext token is read into memory,
                // immediately re-protected with CurrentUser DPAPI, and the file
                // is atomically replaced before control returns to startup.
                Save(legacySession);
                PlaybackLog.Write(
                    "SessionSecurity",
                    "Migrated legacy plaintext session token to DPAPI-protected storage.");
                return legacySession;
            }

            PlaybackLog.Write(
                "SessionSecurity",
                "Session file exists but does not contain a supported token format.");
            return null;
        }
        catch (CryptographicException ex)
        {
            PlaybackLog.Write(
                "SessionSecurity",
                $"Unable to decrypt session token for the current Windows user: {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            PlaybackLog.Error("SessionLoad", ex);
            return null;
        }
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(SessionPath))
                File.Delete(SessionPath);
        }
        catch
        {
        }
    }

    private static bool TryReadEncryptedSession(
        string json,
        JsonElement root,
        out EmbySession session)
    {
        session = null!;

        if (!root.TryGetProperty("AccessTokenProtected", out var protectedProperty) ||
            protectedProperty.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var stored = JsonSerializer.Deserialize<StoredSession>(json, JsonOptions);
        if (stored is null ||
            string.IsNullOrWhiteSpace(stored.AccessTokenProtected))
        {
            return false;
        }

        var protectedBytes = Convert.FromBase64String(stored.AccessTokenProtected);
        var plainBytes = ProtectedData.Unprotect(
            protectedBytes,
            Entropy,
            DataProtectionScope.CurrentUser);

        try
        {
            session = new EmbySession
            {
                ServerUrl = stored.ServerUrl,
                UserId = stored.UserId,
                DeviceId = stored.DeviceId,
                UserName = stored.UserName,
                AccessToken = Encoding.UTF8.GetString(plainBytes)
            };

            return !string.IsNullOrWhiteSpace(session.AccessToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainBytes);
        }
    }

    private static bool TryReadLegacyPlaintextSession(
        string json,
        JsonElement root,
        out EmbySession session)
    {
        session = null!;

        if (!root.TryGetProperty("AccessToken", out var tokenProperty) ||
            tokenProperty.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(tokenProperty.GetString()))
        {
            return false;
        }

        var legacy = JsonSerializer.Deserialize<EmbySession>(json, JsonOptions);
        if (legacy is null || string.IsNullOrWhiteSpace(legacy.AccessToken))
            return false;

        session = legacy;
        return true;
    }

    private static void WriteAtomically(string json)
    {
        Directory.CreateDirectory(DirectoryPath);

        var tempPath = SessionPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            File.WriteAllText(tempPath, json, Encoding.UTF8);
            File.Move(tempPath, SessionPath, overwrite: true);
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
    }

    private sealed class StoredSession
    {
        [JsonPropertyName("FormatVersion")]
        public int FormatVersion { get; set; } = CurrentFormatVersion;

        [JsonPropertyName("ServerUrl")]
        public string ServerUrl { get; set; } = "";

        [JsonPropertyName("UserId")]
        public string UserId { get; set; } = "";

        [JsonPropertyName("DeviceId")]
        public string DeviceId { get; set; } = "";

        [JsonPropertyName("UserName")]
        public string UserName { get; set; } = "";

        [JsonPropertyName("AccessTokenProtected")]
        public string AccessTokenProtected { get; set; } = "";
    }
}
