namespace StarshipTitanicAp;

/// <summary>The client engine: attaches to the game, polls it every tick, and keeps it in sync with the AP server.
/// Owns no UI - forms subscribe to its events and read its state. Events are always raised on the UI thread.</summary>
internal sealed partial class ClientSession : IDisposable
{
    private const string ProcessName = "scummvm";
    private const int RoomNodeViewIntervalMs = 50;   // ~20Hz
    private const int InventoryIntervalTicks = 20;   // every 20 * 50ms = 1s
    private const int MailIntervalTicks = 20;        // every 20 * 50ms = 1s

    private const int SaveSeedGuardBeamBridgeMissLimit = 100; // 100 * 50ms = 5s
    private const int SaveSeedGuardTagMismatchLimit = 40; // 40 * 50ms = 2s of consecutive mismatched reads before blocking
    private const int SaveSeedGuardRecheckIntervalTicks = 40; // every 40 * 50ms = 2s, once verified

    private readonly MemoryReader _mem = new();
    private readonly ArchipelagoConnection _apConnection = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = RoomNodeViewIntervalMs };
    private int _tickCount;

    private readonly SynchronizationContext _uiContext;
    private readonly int _uiThreadId;

    private RoomNodeView? _lastRoomNodeView;
    private uint? _lastRoomFlags;
    private int? _lastPassengerClass;

    /// <summary>Display-only cache of the last RNV reported through <see cref="RoomNodeViewChanged"/>, updated every
    /// tick regardless of the save/seed guard. <see cref="_lastRoomNodeView"/> only advances once the guard is Ok.</summary>
    private RoomNodeView? _lastDisplayedRnv;

    private List<GameState.MailItem>? _lastMailItems;

    private readonly HashSet<string> _sentRoomVisitChecks = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<RoomNodeView> _sentPointOfInterestChecks = new();
    private int _lastItemsReceivedCount = -1;
    private int _lastTableAccessItemsCount = -1;
    private int _lastStateroomItemsReceivedCount = -1;

    private long? _currentGameManager;
    private long? _currentProject;
    private long? _currentInventoryRoom;
    private long? _currentMailManRoom;

    private int? _pendingDirtyReassertTick;
    private const int DirtyReassertDelayTicks = 4; // 4 * 50ms = 200ms

    /// <summary>Items still Restored at the RNV they were just left from, awaiting a delayed re-check (see
    /// TryUnrestoreItemsLeavingRnv).</summary>
    private readonly List<(string ItemName, long ItemAddress, int DueTick)> _pendingUnrestoreChecks = new();
    private const int UnrestoreCheckDelayTicks = InventoryIntervalTicks + 4; // let ReconcileTrackedItems run at least once, plus buffer

    /// <summary>The (Room, Node, View) shown during the bellbot's Titania's Eye (Light) dialogue/hand-off
    /// cutscene.</summary>
    private static readonly RoomNodeView Eye1HandoffCutsceneRnv = new(7, 11, 1);

    private bool _conversationsAddrShown;
    private string? _currentRoomName;

    private SaveSeedGuardState _saveSeedGuardState = SaveSeedGuardState.Unverified;
    private int _saveSeedGuardBeamBridgeMisses;
    private int _saveSeedGuardTagMismatches;

    private bool _readyToPlayLogged;

    /// <summary>Must be created on the UI thread, after the first control exists (so a WinForms
    /// SynchronizationContext is installed).</summary>
    public ClientSession()
    {
        _uiContext = SynchronizationContext.Current
            ?? throw new InvalidOperationException("ClientSession must be created on the UI thread");
        _uiThreadId = Environment.CurrentManagedThreadId;
        Connection = ConnectionSettings.Load();

        _timer.Tick += (_, _) => OnTick();
        _apConnection.StateChanged += (state, message) => RunOnUiThread(() => OnApStateChanged(state, message));
        _apConnection.MessageReceived += text => RunOnUiThread(() => OnApMessageReceived(text));
        _apConnection.ItemReceived += _ => RunOnUiThread(() => ItemReceived?.Invoke());
        _apConnection.CheckQueued += name =>
        {
            if (_apConnection.State != ApConnectionState.Connected)
                AppendServerLog($"CLIENT: Location check queued: {name}");
        };
    }

    // --- State for the UI ---

    internal MemoryReader Memory => _mem;
    internal ArchipelagoConnection Ap => _apConnection;
    internal ClientOptions Options { get; } = new();

    /// <summary>The server/slot/password last connected with (or loaded from <see cref="ConnectionSettings"/>).</summary>
    internal ConnectionSettings.Data Connection { get; private set; }

    /// <summary>One-line description of the attach state, e.g. "Attached (PID 1234, base 0x...)".</summary>
    internal string AttachStatus { get; private set; } = "Not attached";

    internal long? GameManager => _currentGameManager;
    internal long? Project => _currentProject;
    internal long? InventoryRoom => _currentInventoryRoom;
    internal long? MailManRoom => _currentMailManRoom;
    internal string? CurrentRoomName => _currentRoomName;
    internal SaveSeedGuardState SaveSeedGuardState => _saveSeedGuardState;

    // --- Events for the UI ---

    /// <summary>A user-facing action (or automatic sync step) finished: (succeeded, description).</summary>
    internal event Action<bool, string>? ActionReported;

    /// <summary>A PET talk command was captured by TextCommandHook (with how it was handled).</summary>
    internal event Action<string>? CommandCaptured;

    /// <summary>A line for the diagnostic log.</summary>
    internal event Action<string>? DebugLog;

    /// <summary>A line for the player-facing server log: (text, bold).</summary>
    internal event Action<string, bool>? ServerLog;

    /// <summary>A tracked game address was resolved or cleared: (label, address or null).</summary>
    internal event Action<string, long?>? AddressChanged;

    /// <summary>Attached or detached (or an attach attempt failed); see <see cref="AttachStatus"/>.</summary>
    internal event Action? AttachChanged;

    internal event Action<ApConnectionState, string>? ApStateChanged;
    internal event Action? ItemReceived;
    internal event Action? PendingChecksChanged;

    /// <summary>Items were moved around in the game tree; any item listing is stale.</summary>
    internal event Action? ItemsChanged;

    /// <summary>The mail system's contents changed. Null while CMailMan isn't resolved.</summary>
    internal event Action<List<GameState.MailItem>?>? MailChanged;

    /// <summary>The player's (Room, Node, View) changed, or null with a reason while it can't be read.</summary>
    internal event Action<RoomNodeView?, string?>? RoomNodeViewChanged;

    internal event Action<uint>? RoomFlagsChanged;
    internal event Action<int>? PassengerClassChanged;

    /// <summary>Raised once per tick that got far enough to evaluate the save/seed guard.</summary>
    internal event Action? Ticked;

    // --- Lifecycle ---

    /// <summary>Starts ticking and makes the first attach attempt. Subscribe to events first.</summary>
    internal void Start()
    {
        _timer.Start();
        Attach();
    }

    internal void Connect(string server, string slot, string password)
    {
        Connection = new ConnectionSettings.Data { Server = server.Trim(), Slot = slot.Trim(), Password = password };
        _ = _apConnection.ConnectAsync(Connection.Server, Connection.Slot, Connection.Password);
    }

    public void Dispose()
    {
        UninstallHooks();
        _timer.Stop();
        _timer.Dispose();
        _mem.Dispose();
        _apConnection.Dispose();
    }

    /// <summary>Handles ArchipelagoConnection.StateChanged.</summary>
    private void OnApStateChanged(ApConnectionState state, string message)
    {
        ApStateChanged?.Invoke(state, message);

        switch (state)
        {
            case ApConnectionState.Connecting:
                _readyToPlayLogged = false;
                break;
            case ApConnectionState.Connected:
                _sentRoomVisitChecks.Clear();
                _sentPointOfInterestChecks.Clear();
                _saveSeedGuardState = SaveSeedGuardState.Unverified;
                _saveSeedGuardBeamBridgeMisses = 0;
                _saveSeedGuardTagMismatches = 0;
                _lastItemsReceivedCount = -1;
                _lastTableAccessItemsCount = -1;
                _lastStateroomItemsReceivedCount = -1;
                if (_currentRoomName is not null)
                    TrySendRoomVisitCheck(_currentRoomName);
                if (_lastRoomNodeView is not null)
                    TrySendPointOfInterestCheck(_lastRoomNodeView.Value);
                NotifyPendingChecksChanged();
                ConnectionSettings.Save(Connection);
                AppendServerLog($"CLIENT: Connected to Archipelago ({message})");
                CheckReadyToPlay();
                break;
            case ApConnectionState.ConnectionFailed:
                _readyToPlayLogged = false;
                AppendServerLog($"CLIENT: Failed to connect to Archipelago: {message}");
                break;
            case ApConnectionState.Disconnected:
            default:
                _readyToPlayLogged = false;
                AppendServerLog($"CLIENT: Disconnected from Archipelago ({message})");
                break;
        }
    }

    /// <summary>Handles ArchipelagoConnection.MessageReceived.</summary>
    private void OnApMessageReceived(string text)
    {
        AppendLog($"AP: {text}");
        AppendServerLog(text);

        if (!_mem.IsAttached)
            return;

        bool ok = _currentInventoryRoom is not null && GameActions.DisplayMessageSmart(_mem, _currentInventoryRoom.Value, text);

        if (!ok)
            ReportAction(false, $"Failed to display AP message: \"{text}\"");
    }

    /// <summary>Logs "Ready to Play!" to the server log the moment both the game attach and the AP connection are simultaneously up.</summary>
    private void CheckReadyToPlay()
    {
        if (_readyToPlayLogged)
            return;
        if (!_mem.IsAttached || _apConnection.State != ApConnectionState.Connected)
            return;

        _readyToPlayLogged = true;
        AppendServerLog("Ready to Play!", bold: true);
    }

    // --- Raising events ---

    private void RunOnUiThread(Action action)
    {
        if (Environment.CurrentManagedThreadId == _uiThreadId)
            action();
        else
            _uiContext.Post(_ => action(), null);
    }

    private void ReportAction(bool ok, string what) => ActionReported?.Invoke(ok, what);

    private void ReportCapturedCommand(string command) => CommandCaptured?.Invoke(command);

    private void AppendLog(string line) => RunOnUiThread(() => DebugLog?.Invoke(line));

    private void AppendServerLog(string line, bool bold = false) => RunOnUiThread(() => ServerLog?.Invoke(line, bold));

    private void ReportAddress(string label, long? value) => AddressChanged?.Invoke(label, value);

    private void NotifyPendingChecksChanged() => PendingChecksChanged?.Invoke();

    private void NotifyItemsChanged() => ItemsChanged?.Invoke();
}
