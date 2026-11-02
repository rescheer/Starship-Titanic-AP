using System.Diagnostics;

namespace StarshipTitanicAp;

/// <summary>Where the running game keeps its files, as set in the ScummVM config beside scummvm.exe.</summary>
internal sealed record GameInstall(string GameFolder, string SaveFolder, string Target)
{
    private const string ConfigFileName = "scummvm-titanic.ini";

    public string NewGamePath => Path.Combine(GameFolder, "newgame.st");

    public string SavePath(int slot) => Path.Combine(SaveFolder, $"{Target}.{slot:D3}");

    /// <summary>Null, with <paramref name="failureReason"/> set, if the config can't be read or has no Starship
    /// Titanic section.</summary>
    public static GameInstall? Find(int processId, out string failureReason)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            string exeFolder = Path.GetDirectoryName(process.MainModule?.FileName) ?? "";
            string configPath = Path.Combine(exeFolder, ConfigFileName);
            if (!File.Exists(configPath))
            {
                failureReason = $"{ConfigFileName} not found in {exeFolder}";
                return null;
            }

            string? section = null;
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string rawLine in File.ReadLines(configPath))
            {
                string line = rawLine.Trim();
                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    if (values.GetValueOrDefault("gameid") == "titanic")
                        break;
                    section = line[1..^1];
                    values.Clear();
                }
                else if (line.IndexOf('=') is > 0 and var equals)
                {
                    values[line[..equals]] = line[(equals + 1)..];
                }
            }

            if (values.GetValueOrDefault("gameid") != "titanic" || section is null
                || !values.TryGetValue("path", out string? path) || !values.TryGetValue("savepath", out string? savePath))
            {
                failureReason = $"{ConfigFileName} has no complete Starship Titanic section";
                return null;
            }

            failureReason = "";
            return new GameInstall(Path.GetFullPath(path, exeFolder), Path.GetFullPath(savePath, exeFolder), section);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            failureReason = ex.Message;
            return null;
        }
    }
}
