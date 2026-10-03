using System.Text.Json;
using AsterPlay.Models;

namespace AsterPlay.Services;

public static class AppStateStore
{
    private static readonly string DirectoryPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AsterPlay");

    private static readonly string SessionPath = Path.Combine(DirectoryPath, "session.json");

    public static void Save(EmbySession session)
    {
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(SessionPath,
            JsonSerializer.Serialize(session, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static EmbySession? Load()
    {
        if (!File.Exists(SessionPath))
            return null;

        try
        {
            return JsonSerializer.Deserialize<EmbySession>(File.ReadAllText(SessionPath));
        }
        catch
        {
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
}
