using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace StarshipTitanicAp;

/// <summary>Where the running game keeps its files, as set in the ScummVM config the game was started with.</summary>
internal sealed record GameInstall(string GameFolder, string SaveFolder, string Target)
{
    /// <summary>The Steam release's config, beside its scummvm.exe.</summary>
    private const string SteamConfigFileName = "scummvm-titanic.ini";
    private const string DefaultConfigFileName = "scummvm.ini";
    private const string GameId = "titanic";

    public string NewGamePath => Path.Combine(GameFolder, "newgame.st");

    public string SavePath(int slot) => Path.Combine(SaveFolder, $"{Target}.{slot:D3}");

    /// <summary>ScummVM's own folder under %APPDATA%, where it keeps its default config and saves on Windows.</summary>
    private static string AppDataFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScummVM");

    /// <summary>Null, with <paramref name="failureReason"/> set, if the config can't be found or read or has no Starship
    /// Titanic section.</summary>
    public static GameInstall? Find(int processId, out string failureReason)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            string exeFolder = Path.GetDirectoryName(process.MainModule?.FileName) ?? "";
            CommandLine commandLine = CommandLine.Parse(ReadCommandLine(process));

            string? configPath = FindConfig(exeFolder, commandLine, out string searched);
            if (configPath is null)
            {
                failureReason = $"no ScummVM config found (looked for {searched})";
                return null;
            }
            string configFolder = Path.GetDirectoryName(configPath) ?? exeFolder;

            Dictionary<string, Dictionary<string, string>> sections = ReadIni(configPath);
            string? target = PickTarget(sections, commandLine.Target);
            if (target is null || !sections[target].TryGetValue("path", out string? path))
            {
                failureReason = $"{configPath} has no complete Starship Titanic section";
                return null;
            }

            // Where ScummVM looks for saves: the game's own setting, then the global one, then its default.
            string savePath = commandLine.SavePath
                ?? sections[target].GetValueOrDefault("savepath")
                ?? sections.GetValueOrDefault("scummvm")?.GetValueOrDefault("savepath")
                ?? Path.Combine(AppDataFolder, "Saved games");

            failureReason = "";
            return new GameInstall(Path.GetFullPath(path, configFolder), Path.GetFullPath(savePath, configFolder), target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or InvalidOperationException or Win32Exception)
        {
            failureReason = ex.Message;
            return null;
        }
    }

    /// <summary>The config ScummVM loaded: the one named on its command line, else the Steam release's, else a
    /// portable one beside the exe, else the default under %APPDATA%.</summary>
    private static string? FindConfig(string exeFolder, CommandLine commandLine, out string searched)
    {
        var candidates = new List<string>();
        if (commandLine.ConfigPath is { } named)
        {
            // Relative to the working directory, which GOG's and Steam's launchers both set to the exe's folder.
            candidates.Add(Path.GetFullPath(named, exeFolder));
        }
        else
        {
            candidates.Add(Path.Combine(exeFolder, SteamConfigFileName));
            candidates.Add(Path.Combine(exeFolder, DefaultConfigFileName));
            candidates.Add(Path.Combine(AppDataFolder, DefaultConfigFileName));
        }

        searched = string.Join(", ", candidates);
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>The section to use: the target on the command line, else the one last picked in ScummVM's launcher,
    /// else the first Starship Titanic section. Only sections for this game qualify.</summary>
    private static string? PickTarget(Dictionary<string, Dictionary<string, string>> sections, string? commandLineTarget)
    {
        bool IsTitanic(string? name) =>
            name is not null && sections.TryGetValue(name, out var values) && values.GetValueOrDefault("gameid") == GameId;

        if (IsTitanic(commandLineTarget))
            return commandLineTarget;
        string? lastSelected = sections.GetValueOrDefault("scummvm")?.GetValueOrDefault("lastselectedgame");
        if (IsTitanic(lastSelected))
            return lastSelected;
        return sections.Keys.FirstOrDefault(IsTitanic);
    }

    private static Dictionary<string, Dictionary<string, string>> ReadIni(string path)
    {
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string>? current = null;
        foreach (string rawLine in File.ReadLines(path))
        {
            string line = rawLine.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                string name = line[1..^1];
                if (!sections.TryGetValue(name, out current))
                {
                    current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    sections[name] = current;
                }
            }
            else if (current is not null && line.IndexOf('=') is > 0 and var equals)
            {
                current[line[..equals].Trim()] = line[(equals + 1)..].Trim();
            }
        }
        return sections;
    }

    /// <summary>The ScummVM options that change where the game's files are.</summary>
    private sealed record CommandLine(string? ConfigPath, string? SavePath, string? Target)
    {
        public static CommandLine Parse(string? commandLine)
        {
            string[] args = SplitArgs(commandLine);
            string? config = null, savePath = null, target = null;

            // args[0] is the exe. Options taking a value use --name=value, except -c, which also takes the next argument.
            for (int i = 1; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg == "-c" && i + 1 < args.Length)
                    config = args[++i];
                else if (arg.StartsWith("-c", StringComparison.Ordinal) && arg.Length > 2 && arg[2] != '-')
                    config = arg[2..];
                else if (arg.StartsWith("--config=", StringComparison.Ordinal))
                    config = arg["--config=".Length..];
                else if (arg.StartsWith("--savepath=", StringComparison.Ordinal))
                    savePath = arg["--savepath=".Length..];
                else if (!arg.StartsWith('-'))
                    target = arg;
            }
            return new CommandLine(config, savePath, target);
        }

        private static string[] SplitArgs(string? commandLine)
        {
            if (string.IsNullOrWhiteSpace(commandLine))
                return [];
            IntPtr argv = CommandLineToArgvW(commandLine, out int count);
            if (argv == IntPtr.Zero)
                return [];
            try
            {
                var args = new string[count];
                for (int i = 0; i < count; i++)
                    args[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size)) ?? "";
                return args;
            }
            finally
            {
                LocalFree(argv);
            }
        }
    }

    /// <summary>The game's command line, or null if it can't be read.</summary>
    private static string? ReadCommandLine(Process process)
    {
        const int ProcessCommandLineInformation = 60;
        int length = 0x1000;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            IntPtr buffer = Marshal.AllocHGlobal(length);
            try
            {
                int status = NtQueryInformationProcess(process.Handle, ProcessCommandLineInformation, buffer, length, out int needed);
                if (status == 0)
                {
                    // A UNICODE_STRING: ushort Length (in bytes), ushort MaximumLength, then the buffer pointer.
                    int bytes = (ushort)Marshal.ReadInt16(buffer);
                    IntPtr text = Marshal.ReadIntPtr(buffer, IntPtr.Size);
                    return Marshal.PtrToStringUni(text, bytes / 2);
                }
                if (needed <= length)
                    return null;
                length = needed;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        return null;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr process, int infoClass, IntPtr info, int length, out int returnLength);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW(string commandLine, out int count);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
