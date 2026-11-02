using System.Diagnostics;

namespace StarshipTitanicAp;

/// <summary>Has the game write a save to a slot of its own every so often, and keeps it in slot data storage (see
/// <see cref="SaveSnapshot"/>). Only for seeds with Quick Start on, which is what restores it, so other seeds don't use the
/// storage at all. The game does the saving on its own thread, as it does for ScummVM's autosave, and only when it says it
/// can, so this never adds a save the game couldn't have made itself.</summary>
internal sealed class SaveSnapshotSync
{
    /// <summary>Above <see cref="QuickStart"/>'s slot.</summary>
    private const int SaveSlot = 43;
    private const string SaveDescription = "AP snapshot";

    private const long IntervalMs = 30_000;
    private const long BusyRetryMs = 2_000;

    private readonly SessionContext _ctx;

    private long _nextAttemptMs;
    private bool _inFlight;
    private int _treeGeneration;
    private GameInstall? _install;
    private byte[]? _newGame;
    private bool _failureReported;

    public SaveSnapshotSync(SessionContext ctx)
    {
        _ctx = ctx;
    }

    /// <summary>Call when the tree is rebuilt. A snapshot still being sent came from the old one, and is dropped.</summary>
    public void Reset()
    {
        _treeGeneration++;
        _nextAttemptMs = Environment.TickCount64 + IntervalMs;
    }

    /// <summary>Takes and sends a snapshot if one is due. Only call with the save known to belong to this seed.</summary>
    public async void Sync()
    {
        long now = Environment.TickCount64;
        if (_inFlight || now < _nextAttemptMs)
            return;
        if (!_ctx.Ap.IsConnected || !QuickStart.IsEnabled(_ctx.Ap.SlotData) || _ctx.Project is not { } project)
            return;

        // A call from a remote thread would race the whole save against the game
        if (!GameThreadHook.IsInstalled || !LoadFiles())
        {
            _nextAttemptMs = now + IntervalMs;
            return;
        }
        if (GameActions.CanSaveGame(_ctx.Mem) != true)
        {
            _nextAttemptMs = now + BusyRetryMs;
            return;
        }
        _nextAttemptMs = now + IntervalMs;

        var timer = Stopwatch.StartNew();
        if (!GameActions.SaveGame(_ctx.Mem, project, SaveSlot, SaveDescription))
        {
            ReportFailure("the game couldn't be asked to save");
            return;
        }
        long saveMs = timer.ElapsedMilliseconds;

        int generation = _treeGeneration;
        string path = _install!.SavePath(SaveSlot);
        byte[] newGame = _newGame!;
        _inFlight = true;
        try
        {
            string snapshot = await Task.Run(() => SaveSnapshot.Create(newGame, File.ReadAllBytes(path)));
            if (generation != _treeGeneration || snapshot == _ctx.Ap.GetStoredString(SaveSnapshot.StorageKey))
                return;

            if (_ctx.Ap.SetStoredString(SaveSnapshot.StorageKey, snapshot))
                _ctx.Report.AppendLog($"Save snapshot stored: {snapshot.Length} characters, game paused {saveMs} ms");
            else
                _ctx.Report.AppendLog("Save snapshot couldn't be stored - will retry");
            _failureReported = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            ReportFailure(ex.Message);
        }
        finally
        {
            _inFlight = false;
        }
    }

    private bool LoadFiles()
    {
        if (_newGame is not null)
            return true;

        _install = GameInstall.Find(_ctx.Mem.ProcessId, out string failureReason);
        if (_install is null)
        {
            ReportFailure(failureReason);
            return false;
        }

        try
        {
            byte[] newGame = File.ReadAllBytes(_install.NewGamePath);
            if (!QuickStartSave.FitsPatch(newGame))
            {
                ReportFailure("this copy of the game's newgame.st isn't supported");
                return false;
            }
            _newGame = newGame;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ReportFailure(ex.Message);
            return false;
        }
    }

    private void ReportFailure(string reason)
    {
        if (_failureReported)
            return;
        _failureReported = true;
        _ctx.Report.ReportAction(false, $"Save snapshot failed: {reason}");
    }
}
