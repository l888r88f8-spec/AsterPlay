using System.Text.Json;
using AsterPlay.Models;
using AsterPlay.Services;

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

var sessionPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "AsterPlay",
    "session.json");

var token = "ASTERPLAY-DPAPI-SMOKE-" + Guid.NewGuid().ToString("N");
var expected = new EmbySession
{
    ServerUrl = "https://example.invalid",
    UserId = "user-123",
    DeviceId = "device-123",
    UserName = "smoke",
    AccessToken = token
};

AppStateStore.Clear();
AppStateStore.Save(expected);

var encryptedJson = File.ReadAllText(sessionPath);
Require(!encryptedJson.Contains(token, StringComparison.Ordinal),
    "New session file leaked plaintext token.");
Require(!encryptedJson.Contains("\"AccessToken\"", StringComparison.Ordinal),
    "New session file still contains legacy AccessToken field.");
Require(encryptedJson.Contains("\"AccessTokenProtected\"", StringComparison.Ordinal),
    "New session file lacks AccessTokenProtected.");
Require(encryptedJson.Contains("\"FormatVersion\": 2", StringComparison.Ordinal),
    "New session file lacks format version 2.");

var loaded = AppStateStore.Load();
Require(loaded is not null, "Encrypted session failed to load.");
Require(loaded!.AccessToken == token, "Decrypted token does not match.");
Require(loaded.ServerUrl == expected.ServerUrl, "ServerUrl changed.");
Require(loaded.UserId == expected.UserId, "UserId changed.");
Require(loaded.DeviceId == expected.DeviceId, "DeviceId changed.");
Require(loaded.UserName == expected.UserName, "UserName changed.");

// Simulate the legacy v1 file that serialized EmbySession directly.
Directory.CreateDirectory(Path.GetDirectoryName(sessionPath)!);
File.WriteAllText(
    sessionPath,
    JsonSerializer.Serialize(expected, new JsonSerializerOptions { WriteIndented = true }));

var legacyBefore = File.ReadAllText(sessionPath);
Require(legacyBefore.Contains(token, StringComparison.Ordinal),
    "Legacy fixture did not contain the expected plaintext token.");

var migrated = AppStateStore.Load();
Require(migrated is not null, "Legacy session failed to load.");
Require(migrated!.AccessToken == token, "Legacy migration changed the token.");

var legacyAfter = File.ReadAllText(sessionPath);
Require(!legacyAfter.Contains(token, StringComparison.Ordinal),
    "Legacy migration left plaintext token on disk.");
Require(!legacyAfter.Contains("\"AccessToken\"", StringComparison.Ordinal),
    "Legacy migration left legacy AccessToken field.");
Require(legacyAfter.Contains("\"AccessTokenProtected\"", StringComparison.Ordinal),
    "Legacy migration did not write AccessTokenProtected.");

AppStateStore.Clear();
Console.WriteLine("Session DPAPI smoke test passed.");
