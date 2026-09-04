namespace StarshipTitanicAp;

/// <summary>Main application window.</summary>
public sealed partial class MainForm : Form
{
    private const string ProcessName = "scummvm";
    private const int RoomNodeViewIntervalMs = 50;   // ~20Hz
    private const int InventoryIntervalTicks = 20;   // every 20 * 50ms = 1s
    private const int MailIntervalTicks = 20;        // every 20 * 50ms = 1s

    private const int SaveSeedGuardBeamBridgeMissLimit = 100; // 100 * 50ms = 5s
    private const int SaveSeedGuardTagMismatchLimit = 40; // 40 * 50ms = 2s of consecutive mismatched reads before blocking - gives a mid-load save time to finish initializing
    private const int SaveSeedGuardRecheckIntervalTicks = 40; // every 40 * 50ms = 2s, once verified

    private readonly MemoryReader _mem = new();
    private readonly ArchipelagoConnection _apConnection = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = RoomNodeViewIntervalMs };
    private int _tickCount;

    private RoomNodeView? _lastRoomNodeView;
    private uint? _lastRoomFlags;
    private int? _lastPassengerClass;

    /// <summary>Display-only cache of the last RNV shown on the Live tab - updates every tick regardless of the
    /// save/seed guard, independent of <see cref="_lastRoomNodeView"/> which only advances once the guard is Ok so
    /// that AP-facing writes (location checks, item restoration, mail delivery, etc.) still correctly detect and
    /// react to every room change that happened while unverified, once verification completes.</summary>
    private RoomNodeView? _lastDisplayedRnv;

    private List<CarryItemLocation>? _lastInventory;
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
    /// TryUnrestoreItemsLeavingRnv) so a genuine pickup - whose RNV change and inventory reparent aren't
    /// necessarily on the same tick - gets a chance to complete before being reverted as "abandoned".</summary>
    private readonly List<(string ItemName, long ItemAddress, int DueTick)> _pendingUnrestoreChecks = new();
    private const int UnrestoreCheckDelayTicks = InventoryIntervalTicks + 4; // let ReconcileTrackedItems run at least once, plus buffer

    private bool _conversationsAddrShown;
    private string? _currentRoomName;

    private SaveSeedGuardState _saveSeedGuardState = SaveSeedGuardState.Unverified;
    private int _saveSeedGuardBeamBridgeMisses;
    private int _saveSeedGuardTagMismatches;

    // --- Top bar (shared across tabs) ---
    private readonly Button _btnAttach = new() { Text = "Attach", Width = 110, Height = 40 };
    private readonly Label _lblStatus = new() { Text = "Not attached", AutoSize = true };
    private readonly Label _lblApTopStatus = new() { Text = "AP: Not connected", AutoSize = true, ForeColor = Color.DimGray };
    private readonly Label _lblActionResult = new() { Text = "", AutoSize = true, ForeColor = Color.DarkSlateGray };

    private readonly bool _isDebug;

    public MainForm(bool isDebug)
    {
        _isDebug = isDebug;
        Text = AppInfo.TitleBarText;
        Width = 750;   // 500 * 1.5
        Height = 660;  // 600 * 1.10
        StartPosition = FormStartPosition.CenterScreen;

        BuildLayout();

        ConnectionSettings.Data savedConnection = ConnectionSettings.Load();
        _txtApServer.Text = savedConnection.Server;
        _txtApSlot.Text = savedConnection.Slot;
        _txtApPassword.Text = savedConnection.Password;
        UpdatePendingChecksLabel();
        UpdateApTopStatus();
        UpdateFooterAttachStatus();
        UpdateFooterApStatus();
        UpdateApToggleButton();

        _btnAttach.Click += (_, _) => AttemptAttach();
        _btnDetach.Click += (_, _) => DoDetach();
        _timer.Tick += (_, _) => OnTick();
        _timer.Start();

        _btnApConnect.Click += (_, _) => DoApConnect();
        _btnApDisconnect.Click += (_, _) => _apConnection.Disconnect();
        _btnClearPendingChecks.Click += (_, _) => DoClearPendingChecks();
        _apConnection.StateChanged += OnApStateChanged;
        _apConnection.MessageReceived += OnApMessageReceived;

        if (!_isDebug)
            WireNormalUiEvents();

        _btnSetClass.Click += (_, _) => DoSetClass();
        _btnMoveItem.Click += (_, _) => DoMoveToCustomAddress();
        _btnForceReconcileItems.Click += (_, _) => DoForceReconcileItems();
        _btnScanShipSettings.Click += (_, _) => DoScanShipSettings();
        _btnFindGetLiftEye2.Click += (_, _) => DoFindGetLiftEye2();
        _btnClearGhostFuseSockets.Click += (_, _) => DoClearGhostFuseSockets();
        _btnDiffSnapshotA.Click += (_, _) => DoDiffSnapshotA();
        _btnDiffSnapshotB.Click += (_, _) => DoDiffSnapshotB();
        _btnValueScanUseCurrentRoomFlags.Click += (_, _) => DoUseCurrentRoomFlagsForValueScan();
        _btnValueScan.Click += (_, _) => DoValueScan();
        _btnDumpMemory.Click += (_, _) => DoDumpMemory();
        _btnListRoomGlyphs.Click += (_, _) => DoListRoomGlyphs();
        _btnMoveToInventory.Click += (_, _) => DoMoveSelectedTo(MoveDestination.Inventory);
        _btnMoveToMailman.Click += (_, _) => DoMoveSelectedTo(MoveDestination.Mailman);
        _btnMoveToHiddenRoom.Click += (_, _) => DoMoveSelectedTo(MoveDestination.HiddenRoom);
        _btnMarkAllDirty.Click += (_, _) => DoMarkAllDirty();
        _btnResetPet.Click += (_, _) => DoResetPetControl();
        _btnInstallHook.Click += (_, _) => DoInstallHook();
        _btnUninstallHook.Click += (_, _) => DoUninstallHook();
        _btnInstallClassLockHook.Click += (_, _) => DoInstallClassLockHook();
        _btnUninstallClassLockHook.Click += (_, _) => DoUninstallClassLockHook();
        _btnInstallMaitreDHook.Click += (_, _) => DoInstallMaitreDHook();
        _btnUninstallMaitreDHook.Click += (_, _) => DoUninstallMaitreDHook();
        _btnInstallGetLiftEye2GateHook.Click += (_, _) => DoInstallGetLiftEye2GateHook();
        _btnUninstallGetLiftEye2GateHook.Click += (_, _) => DoUninstallGetLiftEye2GateHook();
        _btnInstallRoomAssignHook.Click += (_, _) => DoInstallRoomAssignHook();
        _btnUninstallRoomAssignHook.Click += (_, _) => DoUninstallRoomAssignHook();
        _btnDisplayMessageText.Click += (_, _) => DoDisplayMessageText();
        _btnSetDestToCurrentRoom.Click += (_, _) => DoSetMailDestToCurrentRoom();
        _btnCopyInventoryAddress.Click += (_, _) => DoCopyInventoryAddress();
        _btnRefreshAllItems.Click += (_, _) => DoRefreshAllItems();
        _btnExportParentSnapshot.Click += (_, _) => DoExportParentSnapshot();
        _btnCopyParentAddress.Click += (_, _) => DoCopyParentAddress();
        _btnViewFields.Click += (_, _) => DoViewFields();
        _btnForceTagSaveSeed.Click += (_, _) => DoForceTagSaveSeed();

        _lvMail.SelectedIndexChanged += (_, _) => UpdateMailButtonState();
        _lvInventory.SelectedIndexChanged += (_, _) =>
        {
            bool hasSelection = _lvInventory.SelectedItems.Count > 0;
            _btnCopyInventoryAddress.Enabled = hasSelection;
            _btnCopyParentAddress.Enabled = hasSelection;
            _btnMoveToInventory.Enabled = hasSelection;
            _btnMoveToMailman.Enabled = hasSelection;
            _btnMoveToHiddenRoom.Enabled = hasSelection;
            _btnViewFields.Enabled = hasSelection;
        };
        _lvAddresses.SelectedIndexChanged += (_, _) => _btnCopyAddress.Enabled = _lvAddresses.SelectedItems.Count > 0;
        _btnCopyAddress.Click += (_, _) => DoCopyAddressRow();
        _btnClearLog.Click += (_, _) => _txtLog.Clear();

        AttemptAttach();
    }

    private void BuildLayout()
    {
        if (!_isDebug)
        {
            BuildNormalLayout();
            return;
        }

        var topPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 78,
            Padding = new Padding(8),
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = false,
        };
        topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        topPanel.Controls.Add(_btnAttach, 0, 0);

        var infoPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Margin = new Padding(12, 6, 0, 0),
        };
        infoPanel.Controls.Add(_lblStatus);
        infoPanel.Controls.Add(_lblApTopStatus);
        topPanel.Controls.Add(infoPanel, 1, 0);

        var feedbackPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 26,
            Padding = new Padding(8, 0, 8, 0),
        };
        feedbackPanel.Controls.Add(_lblActionResult);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildArchipelagoTab());
        tabs.TabPages.Add(BuildLiveTab());
        tabs.TabPages.Add(BuildItemsTab());
        tabs.TabPages.Add(BuildMailTab());
        tabs.TabPages.Add(BuildPetTab());
        tabs.TabPages.Add(BuildAddressesTab());
        tabs.TabPages.Add(BuildDebugTab());
        tabs.TabPages.Add(BuildLogTab());

        Controls.Add(tabs);
        Controls.Add(feedbackPanel);
        Controls.Add(topPanel);
    }

    private static Label SectionLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font(Control.DefaultFont, FontStyle.Bold),
        Margin = new Padding(0, 6, 0, 4),
    };

    private static Label HelpLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(380, 0),
        ForeColor = Color.DimGray,
        Margin = new Padding(0, 0, 0, 4),
    };

    private static Panel Spacer() => new() { Height = 10, Width = 1 };

    private bool RequireAttachedAndResolved(out long gameManager)
    {
        gameManager = 0;
        if (!_mem.IsAttached)
        {
            ShowActionResult(false, "Not attached");
            return false;
        }
        if (_currentGameManager is null)
        {
            ShowActionResult(false, "gameManager not resolved yet (menu / loading?)");
            return false;
        }
        gameManager = _currentGameManager.Value;
        return true;
    }

    private void ShowActionResult(bool ok, string what)
    {
        string ts = DateTime.Now.ToString("HH:mm:ss");
        _lblActionResult.ForeColor = ok ? Color.DarkGreen : Color.DarkRed;
        _lblActionResult.Text = $"[{ts}] {what}: {(ok ? "OK" : "FAILED")}";
        AppendLog(_lblActionResult.Text);
    }

    /// <summary>Surfaces a PET talk command captured by the hook in the top-level feedback line.</summary>
    private void ShowCapturedCommand(string command)
    {
        string ts = DateTime.Now.ToString("HH:mm:ss");
        _lblActionResult.ForeColor = Color.DarkSlateBlue;
        _lblActionResult.Text = $"[{ts}] PET command captured: {command}";
        AppendLog(_lblActionResult.Text);
    }

    /// <summary>Appends a line to the Log tab's history.</summary>
    private void AppendLog(string line)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => AppendLog(line)));
            return;
        }

        _txtLog.AppendText(line + Environment.NewLine);

        const int maxLines = 2000;
        string[] lines = _txtLog.Lines;
        if (lines.Length > maxLines)
        {
            _txtLog.Lines = lines[(lines.Length - maxLines / 2)..];
            _txtLog.SelectionStart = _txtLog.TextLength;
        }
    }

    private static bool TryParseHex(string text, out long value)
    {
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            text = text[2..];
        return long.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out value);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (TextCommandHook.IsInstalled)
        {
            TextCommandHook.Uninstall(_mem);
        }
        if (ClassUpgradeHook.IsInstalled)
        {
            ClassUpgradeHook.Uninstall(_mem);
        }
        if (MaitreDHook.IsInstalled)
        {
            MaitreDHook.Uninstall(_mem);
        }
        if (GetLiftEye2GateHook.IsInstalled)
        {
            GetLiftEye2GateHook.Uninstall(_mem);
        }
        _timer.Stop();
        _mem.Dispose();
        _apConnection.Dispose();
        base.OnFormClosed(e);
    }
}
