using System.Text.Json;

namespace StarshipTitanicAp;

/// <summary>Persists the last-used Archipelago server/slot/password to a small JSON file in the user's local app data folder.</summary>
public static class ConnectionSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "StarshipTitanicAp",
        "connection.json");

    public sealed class Data
    {
        public string Server { get; set; } = "";
        public string Slot { get; set; } = "";
        public string Password { get; set; } = "";
    }

    /// <summary>Loads the last-saved connection info, or an empty Data if none has been saved yet or the file couldn't be read.</summary>
    public static Data Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new Data();

            string json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<Data>(json) ?? new Data();
        }
        catch
        {
            return new Data();
        }
    }

    /// <summary>Best-effort save; failure to persist is silently ignored.</summary>
    public static void Save(Data data)
    {
        try
        {
            string? dir = Path.GetDirectoryName(FilePath);
            if (dir is not null)
                Directory.CreateDirectory(dir);

            string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // Ignored.
        }
    }
}
