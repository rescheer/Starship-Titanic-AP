namespace StarshipTitanicAp;

/// <summary>The Quick Start option: when the player starts a new game while connected, swaps the untouched new game for
/// a generated save at the Embarkation Lobby. Only an untouched new game qualifies, so this can't replace a game the
/// player chose to load: the game refuses to save with the PET off, which leaves newgame.st as the only way to have it
/// off in Home, and the tray check rules out the intro having started.</summary>
internal sealed class QuickStart
{
    /// <summary>Above the slots the PET's save screen and the game's startup dialog normally reach.</summary>
    private const int SaveSlot = 42;

    private const int HomeRoom = 3;
    private const string CdRomTrayName = "newTray";
    private const string CdRomTrayClass = "CCDROMTray";
    private const string NoCd = "None";
    private const int LoadTimeoutTicks = 100; // 100 * 50ms = 5s

    private readonly SessionContext _ctx;

    private bool _decided;
    private int? _loadRequestedTick;

    public QuickStart(SessionContext ctx)
    {
        _ctx = ctx;
    }

    /// <summary>Forgets the decision made for the current game tree. Call when the tree is rebuilt (save load).</summary>
    public void Reset()
    {
        _decided = false;
        _loadRequestedTick = null;
    }

    /// <summary>Whether the seed has the Quick Start option on. False for seeds without the slot data key.</summary>
    public static bool IsEnabled(IReadOnlyDictionary<string, object>? slotData)
    {
        if (slotData is null || !slotData.TryGetValue("quick_start", out object? raw))
            return false;
        try
        {
            return Convert.ToBoolean(raw);
        }
        catch
        {
            return false; // malformed slot data, treat as missing
        }
    }

    /// <summary>Starts the load if this tree qualifies. True while the load is pending, when nothing else should act on
    /// a tree that's about to be replaced.</summary>
    public bool Update(long gameManager, RoomNodeView? rnv)
    {
        if (_loadRequestedTick is { } requestedTick)
        {
            if (_ctx.TickCount - requestedTick < LoadTimeoutTicks)
                return true;

            _loadRequestedTick = null;
            Fail("the game didn't load the save");
            return false;
        }

        if (_decided || !IsEnabled(_ctx.Ap.SlotData) || !SaveLoadHook.IsInstalled)
            return false;
        if (rnv?.Room != HomeRoom || GameState.ReadPetActive(_ctx.Mem, gameManager) != false)
            return false;
        if (_ctx.Mem.ReadInt32(gameManager + GameOffsets.GameMode) != GameOffsets.GameModeInteractive)
            return false;
        if (_ctx.Project is not { } project || _ctx.Ap.SeedName is not { } seedName || _ctx.Ap.SlotName is not { } slotName)
            return false;

        _decided = true;

        if (GameState.FindDescendant(_ctx.Mem, project, CdRomTrayName, CdRomTrayClass) is not { } tray)
        {
            Fail("the CD tray wasn't found");
            return false;
        }
        string? insertedCd = GameState.ReadCString(_ctx.Mem, tray + GameOffsets.CdRomTrayInsertedCdOffset);
        if (insertedCd != NoCd)
        {
            _ctx.Report.AppendLog($"Quick Start: skipped, as the CD tray holds \"{insertedCd}\" and the intro may have started");
            return false;
        }

        GameInstall? install = GameInstall.Find(_ctx.Mem.ProcessId, out string failureReason);
        if (install is null)
        {
            Fail(failureReason);
            return false;
        }

        try
        {
            byte[] newGame = File.ReadAllBytes(install.NewGamePath);
            if (!QuickStartSave.FitsPatch(newGame))
            {
                Fail("this copy of the game's newgame.st isn't supported");
                return false;
            }

            string savePath = install.SavePath(SaveSlot);
            string tempPath = savePath + ".tmp";
            File.WriteAllBytes(tempPath, QuickStartSave.Build(newGame, SaveSeedTag.ComputeSeedTag(seedName, slotName)));
            File.Move(tempPath, savePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or FormatException)
        {
            Fail(ex.Message);
            return false;
        }

        if (!RequestLoad(gameManager))
        {
            Fail("couldn't ask the game to load the save");
            return false;
        }

        _loadRequestedTick = _ctx.TickCount;
        _ctx.Report.ReportAction(true, "Quick Start: loading the Embarkation Lobby");
        return true;
    }

    /// <summary>What CMainGameWindow::loadGame does, in the same order. The game picks it up on its own thread, after
    /// drawing a frame. Called only in interactive mode, where setMode just sets the value.</summary>
    private bool RequestLoad(long gameManager)
    {
        long? step1 = _ctx.Mem.ReadInt64(_ctx.Mem.ModuleBase + GameOffsets.Step1);
        long? window = step1 is null or 0 ? null : _ctx.Mem.ReadInt64(step1.Value + GameOffsets.Step2);
        return window is not null and not 0
            && _ctx.Mem.WriteInt32(window.Value + GameOffsets.PendingLoadSlot, SaveSlot)
            && _ctx.Mem.WriteInt32(gameManager + GameOffsets.GameMode, GameOffsets.GameModePendingLoad);
    }

    private void Fail(string reason)
    {
        string message = $"Quick Start failed: {reason}. Playing the normal intro instead.";
        _ctx.Report.ReportAction(false, message);
        _ctx.Report.AppendServerLog($"CLIENT: {message}", bold: true);
    }
}
