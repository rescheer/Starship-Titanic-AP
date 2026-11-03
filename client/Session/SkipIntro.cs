namespace StarshipTitanicAp;

/// <summary>The Skip Intro option: when the player starts a new game while connected, swaps the
/// untouched new game for a generated save at the Embarkation Lobby. Only an untouched new game qualifies, so this can't replace a game the
/// player chose to load: the game refuses to save with the PET off, which leaves newgame.st as the only way to have it
/// off in Home, and the tray check rules out the intro having started.</summary>
internal sealed class SkipIntro
{
    /// <summary>Above the slots the PET's save screen and the game's startup dialog normally reach.</summary>
    private const int SaveSlot = 42;

    private const int HomeRoom = 3;
    private const string HomeRoomName = "Home";
    private const string CdRomTrayName = "newTray";
    private const string CdRomTrayClass = "CCDROMTray";
    private const string NoCd = "None";

    private readonly SessionContext _ctx;

    private bool _decided;
    private bool _loadRequested;

    public SkipIntro(SessionContext ctx)
    {
        _ctx = ctx;
    }

    /// <summary>Forgets the decision made for the current game tree. Call when the tree is rebuilt (save load).</summary>
    public void Reset()
    {
        _decided = false;
        _loadRequested = false;
    }

    /// <summary>Whether the seed has the Skip Intro option on. False for seeds without the slot data key.</summary>
    public static bool IsEnabled(IReadOnlyDictionary<string, object>? slotData)
    {
        if (slotData is null || !slotData.TryGetValue("skip_intro", out object? raw))
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

    /// <summary>Starts the load if this tree qualifies. True once it's been requested and until the tree is rebuilt (see
    /// <see cref="Reset"/>), when nothing else may walk a tree the game is about to free.</summary>
    public bool Update(long gameManager, RoomNodeView? rnv)
    {
        if (_loadRequested)
            return true;

        if (_decided || !IsEnabled(_ctx.Ap.SlotData) || !SaveLoadHook.IsInstalled)
            return false;
        if (rnv?.Room != HomeRoom || GameState.ReadPetActive(_ctx.Mem, gameManager) != false)
            return false;
        if (_ctx.Mem.ReadInt32(gameManager + GameOffsets.GameMode) != GameOffsets.GameModeInteractive)
            return false;
        if (_ctx.Project is not { } project || _ctx.Ap.SeedName is not { } seedName || _ctx.Ap.SlotName is not { } slotName)
            return false;

        _decided = true;

        // Searched from the room, as the tree holds more objects than FindDescendant visits and Home is late in it.
        if (GameState.FindRoomByName(_ctx.Mem, project, HomeRoomName) is not { } home
            || GameState.FindDescendant(_ctx.Mem, home, CdRomTrayName, CdRomTrayClass) is not { } tray)
        {
            Fail("the CD tray wasn't found");
            return false;
        }
        string? insertedCd = GameState.ReadCString(_ctx.Mem, tray + GameOffsets.CdRomTrayInsertedCdOffset);
        if (insertedCd != NoCd)
        {
            _ctx.Report.AppendLog($"Skip Intro: skipped, as the CD tray holds \"{insertedCd}\" and the intro may have started");
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
            if (!SkipIntroSave.FitsPatch(newGame))
            {
                Fail("this copy of the game's newgame.st isn't supported");
                return false;
            }

            string savePath = install.SavePath(SaveSlot);
            string tempPath = savePath + ".tmp";
            File.WriteAllBytes(tempPath, SkipIntroSave.Build(newGame, SaveSeedTag.ComputeSeedTag(seedName, slotName)));
            File.Move(tempPath, savePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or FormatException)
        {
            Fail(ex.Message);
            return false;
        }

        if (!RefreshSaveList())
        {
            Fail("couldn't update the game's list of saves");
            return false;
        }
        if (!RequestLoad(gameManager))
        {
            Fail("couldn't ask the game to load the save");
            return false;
        }

        _loadRequested = true;
        _ctx.Report.ReportAction(true, "Skip Intro: loading the Embarkation Lobby");
        return true;
    }

    /// <summary>ScummVM only opens saves on the list it made when it first looked in the folder, and the game aborts on
    /// one it can't open, so a save new since then has to be listed first. Clears the cached folder path the way
    /// DefaultSaveFileManager::updateSavefilesList does, which rebuilds the list the next time a save is opened.</summary>
    private bool RefreshSaveList()
    {
        long? systemRef = _ctx.Mem.ReadInt64(_ctx.Mem.ModuleBase + GameOffsets.SystemPointerRef);
        long? system = systemRef is null or 0 ? null : _ctx.Mem.ReadInt64(systemRef.Value);
        long? manager = system is null or 0 ? null : _ctx.Mem.ReadInt64(system.Value + GameOffsets.SaveFileManager);
        if (manager is null or 0
            || _ctx.Mem.ReadInt64(manager.Value) != _ctx.Mem.ModuleBase + GameOffsets.SaveFileManagerVtable)
            return false;

        long directory = manager.Value + GameOffsets.CachedSaveDirectory;
        long? text = _ctx.Mem.ReadInt64(directory + GameOffsets.CStringDataPtrOffset);
        return text is not null && GameState.LooksLikeHeapPointer(text.Value)
            && _ctx.Mem.WriteInt32(directory + GameOffsets.CStringSizeOffset, 0)
            && _ctx.Mem.WriteByte(text.Value, 0);
    }

    /// <summary>What CMainGameWindow::loadGame does, in the same order. The game picks it up on its own thread the next
    /// time it draws, which it only does once something on screen has changed, so the screen is marked as changed too.
    /// Called only in interactive mode, where setMode just sets the value.</summary>
    private bool RequestLoad(long gameManager)
    {
        long? step1 = _ctx.Mem.ReadInt64(_ctx.Mem.ModuleBase + GameOffsets.Step1);
        long? window = step1 is null or 0 ? null : _ctx.Mem.ReadInt64(step1.Value + GameOffsets.Step2);
        if (window is null or 0
            || !_ctx.Mem.WriteInt32(window.Value + GameOffsets.PendingLoadSlot, SaveSlot)
            || !_ctx.Mem.WriteInt32(gameManager + GameOffsets.GameMode, GameOffsets.GameModePendingLoad))
            return false;

        // The load is requested now whatever this returns: without it, the next change on screen starts it instead.
        GameActions.MarkAllDirty(_ctx.Mem, gameManager);
        return true;
    }

    private void Fail(string reason)
    {
        string message = $"Skip Intro failed: {reason}. Playing the normal intro instead.";
        _ctx.Report.ReportAction(false, message);
        _ctx.Report.AppendServerLog($"CLIENT: {message}", bold: true);
    }
}
