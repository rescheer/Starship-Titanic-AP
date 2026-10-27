namespace StarshipTitanicAp;

/// <summary>Where session features send their output. Implemented by <see cref="ClientSession"/>, which turns these
/// into its UI events.</summary>
internal interface ISessionReporter
{
    void ReportAction(bool ok, string what);
    void AppendLog(string line);
    void AppendServerLog(string line, bool bold = false);
    void NotifyPendingChecksChanged();

    /// <summary>Items were moved in the game tree; any item listing is stale.</summary>
    void NotifyItemsChanged();

    /// <summary>The mail system's contents were changed; re-read them on the next poll.</summary>
    void NotifyMailChanged();
}

/// <summary>What a session feature needs from its <see cref="ClientSession"/>: the game and AP handles, the
/// addresses resolved for the current game tree, and somewhere to report to. The session keeps it up to date.</summary>
internal sealed class SessionContext
{
    public SessionContext(MemoryReader mem, ArchipelagoConnection ap, ClientOptions options, ISessionReporter report)
    {
        Mem = mem;
        Ap = ap;
        Options = options;
        Report = report;
    }

    public MemoryReader Mem { get; }
    public ArchipelagoConnection Ap { get; }
    public ClientOptions Options { get; }
    public ISessionReporter Report { get; }

    /// <summary>Ticks since the session started (only counted while in game).</summary>
    public int TickCount { get; set; }

    public long? GameManager { get; set; }
    public long? Project { get; set; }

    /// <summary>The PET control (CPetControl), which doubles as the player's inventory.</summary>
    public long? InventoryRoom { get; set; }

    public long? MailManRoom { get; set; }

    /// <summary>The engine name (see <see cref="RoomNames"/>) of the room the player is in.</summary>
    public string? CurrentRoomName { get; set; }
}
