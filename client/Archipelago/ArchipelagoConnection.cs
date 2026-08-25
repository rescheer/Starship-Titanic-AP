using System.Linq;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Models;
using Archipelago.MultiClient.Net.Packets;

namespace StarshipTitanicAp;

public enum ApConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    ConnectionFailed,
}

/// <summary>
/// Thin wrapper around an Archipelago.MultiClient.Net session. Owns the
/// connect/disconnect lifecycle and reports state via <see cref="StateChanged"/>.
///
/// StateChanged can fire from a background thread (the connect attempt runs
/// via Task.Run, and socket callbacks arrive on the library's own thread) -
/// callers on a UI thread must marshal back themselves (e.g. via
/// Control.BeginInvoke), the same way MainForm does for everything else.
/// </summary>
public sealed class ArchipelagoConnection : IDisposable
{
    // Reported to the AP server as the game this client implements; must
    // match the name used by the Starship Titanic AP world.
    private const string GameName = "Starship Titanic";

    // Detecting a dead connection: the library's SocketClosed event only
    // fires for a socket the local machine actually knows is closed (a
    // clean close handshake, or a failed send). If the server process
    // dies without either side sending a close frame, the local socket
    // can sit there looking "open" indefinitely - and a blocking send
    // against it can hang for a long time (however long the OS takes to
    // notice the peer is gone), not just fail to be detected. That hang
    // was happening on the calling thread, which for every send in this
    // class used to be the UI thread (WinForms Timer ticks call straight
    // into SendLocationCheck/SendCommand) - so a killed server didn't
    // just go undetected, it froze the whole app.
    //
    // Fix: every actual socket write now runs on a background thread via
    // TrySendAsync, bounded by SendTimeoutMs. The calling thread is never
    // blocked, and a send that doesn't complete in time is itself treated
    // as proof the connection is dead - which restores real dead-
    // connection detection without needing a synthetic heartbeat probe at
    // all (an earlier attempt at one kept causing side effects - see git
    // history / prior conversation - and is not worth repeating here).
    private const int SendTimeoutMs = 5000;

    private ArchipelagoSession? _session;
    private readonly LocationCheckQueue _checkQueue = new();

    public ApConnectionState State { get; private set; } = ApConnectionState.Disconnected;
    public string StatusMessage { get; private set; } = "Not connected";
    public ArchipelagoSession? Session => _session;
    public bool IsConnected => _session is not null;
    public int PendingCheckCount => _checkQueue.Count;

    /// <summary>
    /// Drops every queued-but-unsent location check without sending it -
    /// see LocationCheckQueue.Clear for why this exists (stale checks
    /// surviving a deliberate server reset during testing).
    /// </summary>
    public void ClearPendingChecks() => _checkQueue.Clear();

    /// <summary>
    /// The slot data the server sent back on login (fill_slot_data() in
    /// the .apworld) - e.g. this world's "progressive_class_upgrade_item",
    /// "second_class_tier", "first_class_tier" keys (see
    /// ClassUpgradeTracker). Null until a successful connection.
    /// </summary>
    public IReadOnlyDictionary<string, object>? SlotData { get; private set; }

    // Every item name received this connection, in order, including the
    // replayed history of everything received in past sessions (the
    // server resends all of it on every reconnect - see the note on the
    // ItemReceived subscription below). Cleared at the start of each
    // ConnectAsync. Guarded by a lock since it's written from whatever
    // thread the library's ItemReceived event fires on and read from
    // GetReceivedItemNames() on the UI thread's tick loop.
    private readonly object _itemsLock = new();
    private readonly List<string> _receivedItemNames = new();

    // Defense-in-depth against any future resync replay (server-initiated
    // desync recovery, not just the client-initiated Sync heartbeat that
    // caused this exact bug once already - see the comment above
    // ArchipelagoSession? _session for the full story and why there's no
    // active heartbeat anymore). A ReceivedItems packet reset to index 0
    // re-delivers the full history through ItemReceived; without this,
    // every already-recorded item would silently get counted again.
    // (ItemId, LocationId) uniquely identifies a specific granted item, so
    // legitimate duplicates (the same item genuinely placed at two
    // different locations) still count correctly - only an exact replay
    // of the same (item, location) pair gets filtered.
    private readonly HashSet<(long ItemId, long LocationId)> _seenItemLocationPairs = new();

    /// <summary>
    /// Snapshot of every item name received so far this connection, in
    /// order. Deliberately raw names, not counts or classifications -
    /// interpreting what they mean (e.g. how many class upgrades) is
    /// game-specific and belongs in something like ClassUpgradeTracker,
    /// not here.
    /// </summary>
    public string[] GetReceivedItemNames()
    {
        lock (_itemsLock)
            return _receivedItemNames.ToArray();
    }

    public event Action<ApConnectionState, string>? StateChanged;

    /// <summary>
    /// Fires for every server log-line the AP client library surfaces via
    /// session.MessageLog (item sends/receives, hints, chat, join/leave,
    /// etc.), already formatted as plain text via LogMessage.ToString().
    /// Like StateChanged, this can fire from a background thread.
    /// </summary>
    public event Action<string>? MessageReceived;

    /// <summary>
    /// Fires once per item as it's received (including the replayed
    /// history on every reconnect - see GetReceivedItemNames). Just the
    /// item's display name; for anything beyond "an item with this name
    /// arrived", use GetReceivedItemNames() instead of trying to
    /// accumulate state from this event yourself. Can fire from a
    /// background thread.
    /// </summary>
    public event Action<string>? ItemReceived;

    public async Task ConnectAsync(string server, string slot, string password)
    {
        if (string.IsNullOrWhiteSpace(server))
        {
            RaiseState(ApConnectionState.ConnectionFailed, "Server address is required");
            return;
        }
        if (string.IsNullOrWhiteSpace(slot))
        {
            RaiseState(ApConnectionState.ConnectionFailed, "Slot name is required");
            return;
        }

        DisconnectInternal(); // tear down any previous session first
        lock (_itemsLock)
        {
            _receivedItemNames.Clear(); // fresh history for this connection attempt
            _seenItemLocationPairs.Clear();
        }

        RaiseState(ApConnectionState.Connecting, $"Connecting to {server}...");

        ArchipelagoSession session;
        try
        {
            session = ArchipelagoSessionFactory.CreateSession(server);
        }
        catch (Exception ex)
        {
            RaiseState(ApConnectionState.ConnectionFailed, $"Invalid server address: {ex.Message}");
            return;
        }

        session.Socket.SocketClosed += reason =>
        {
            // Only surface an unexpected drop as a state change if this is
            // still the active session (Disconnect() already nulls _session
            // out before tearing the socket down).
            if (ReferenceEquals(_session, session))
            {
                _session = null;
                SlotData = null;
                RaiseState(ApConnectionState.Disconnected, $"Disconnected: {reason}");
            }
        };

        // Subscribed here, before TryConnectAndLogin, rather than after a
        // successful login - the library replays the full history of
        // already-received items through this same event on every
        // (re)connect, and that replay can start as part of the login
        // call itself, before it returns. Subscribing early is how you're
        // meant to catch it (per the library's own docs).
        session.Items.ItemReceived += helper =>
        {
            // DequeueItem() returns an ItemInfo - ItemName is null if it
            // couldn't be resolved (e.g. DataPackage not loaded yet), in
            // which case there's nothing meaningful to record.
            ItemInfo item = helper.DequeueItem();
            string? name = item.ItemName;
            if (name is null)
                return;

            lock (_itemsLock)
            {
                if (!_seenItemLocationPairs.Add((item.ItemId, item.LocationId)))
                    return; // exact replay of an already-recorded (item, location) pair - see the field's doc comment
                _receivedItemNames.Add(name);
            }
            ItemReceived?.Invoke(name);
        };

        LoginResult result;
        try
        {
            result = await Task.Run(() => session.TryConnectAndLogin(
                GameName,
                slot,
                ItemsHandlingFlags.AllItems,
                password: string.IsNullOrEmpty(password) ? null : password));
        }
        catch (Exception ex)
        {
            result = new LoginFailure(ex.GetBaseException().Message);
        }

        if (result is LoginSuccessful success)
        {
            _session = session;
            SlotData = success.SlotData;
            session.MessageLog.OnMessageReceived += message => MessageReceived?.Invoke(message.ToString());

            await FlushPendingChecksAsync(session);

            RaiseState(ApConnectionState.Connected, $"Connected as {slot}");
        }
        else
        {
            var failure = (LoginFailure)result;
            string detail = failure.Errors.Any()
                ? string.Join("; ", failure.Errors)
                : string.Join("; ", failure.ErrorCodes);
            if (string.IsNullOrEmpty(detail))
                detail = "Login failed";

            try { await session.Socket.DisconnectAsync(); } catch { /* best-effort cleanup */ }
            RaiseState(ApConnectionState.ConnectionFailed, detail);
        }
    }

    /// <summary>
    /// Sends a chat message to the server as a SayPacket. commandText is
    /// sent exactly as given - no '!' is added, so pass it already
    /// prefixed if you want the AP server's command processor to treat it
    /// as a command (e.g. "!hint") rather than plain chat.
    ///
    /// The actual send happens on a background thread (see TrySendAsync) -
    /// this returns as soon as it's handed off, not once delivery is
    /// confirmed, so the return value only means "there's a session to
    /// send through", not "the server has it". Returns false if there's
    /// no active session at all.
    /// </summary>
    public bool SendCommand(string commandText)
    {
        if (_session is not { } session)
            return false;

        _ = TrySendAsync(session, () => session.Socket.SendPacket(new SayPacket { Text = commandText }));
        return true;
    }

    /// <summary>
    /// Reports a completed location check to the server. Safe to call
    /// repeatedly for the same id - the server treats duplicate checks as
    /// a no-op, so callers don't need to track what's already been sent.
    ///
    /// Always queues the id first (see LocationCheckQueue), then attempts
    /// the actual send on a background thread, removing it from the queue
    /// only once that send is confirmed to have gone through. If there's
    /// no session, or the send fails or times out, it's simply left
    /// queued - retried automatically on next connect. Returns false only
    /// for the immediately-known "not connected" case; true means "handed
    /// off", not "confirmed delivered".
    /// </summary>
    public bool SendLocationCheck(long locationId)
    {
        if (_session is not { } session)
        {
            _checkQueue.Enqueue(locationId);
            return false;
        }

        _checkQueue.Enqueue(locationId); // optimistic - removed below once the send actually confirms
        _ = SendLocationCheckAsync(session, locationId);
        return true;
    }

    private async Task SendLocationCheckAsync(ArchipelagoSession session, long locationId)
    {
        bool ok = await TrySendAsync(session, () => session.Locations.CompleteLocationChecks(locationId));
        if (ok)
            _checkQueue.Remove(locationId);
    }

    /// <summary>
    /// Attempts to send every queued check now that we're connected.
    /// Awaited right as the session is established, before the Connected
    /// state is raised, so anything still pending after this genuinely
    /// couldn't be delivered (rather than a timing fluke) - same intent
    /// as before, just each send is now bounded by SendTimeoutMs instead
    /// of running synchronously and unboundedly on the caller's thread.
    /// Stops at the first failure/timeout rather than churning through
    /// the rest of the batch against a connection that's likely already
    /// gone again.
    /// </summary>
    private async Task FlushPendingChecksAsync(ArchipelagoSession session)
    {
        foreach (long id in _checkQueue.Snapshot())
        {
            if (!ReferenceEquals(_session, session))
                return; // session already moved on - let the next connect retry

            bool ok = await TrySendAsync(session, () => session.Locations.CompleteLocationChecks(id));
            if (!ok)
                return;

            _checkQueue.Remove(id);
        }
    }

    /// <summary>
    /// Runs a blocking send on a background thread with a bounded
    /// timeout, so a socket that's silently hung (the OS hasn't yet
    /// noticed the peer is gone) can never freeze the calling thread -
    /// which for every caller in this class is the UI thread. If the send
    /// doesn't complete within SendTimeoutMs, or throws, the connection is
    /// declared dead: on a healthy connection a local send call completes
    /// near-instantly, so either outcome is good evidence something's
    /// actually wrong, not just slow.
    /// </summary>
    private async Task<bool> TrySendAsync(ArchipelagoSession session, Action send)
    {
        if (!ReferenceEquals(_session, session))
            return false; // already moved on to a different (or no) session

        try
        {
            Task sendTask = Task.Run(send);
            Task finished = await Task.WhenAny(sendTask, Task.Delay(SendTimeoutMs));

            if (finished != sendTask)
            {
                HandleDeadConnection(session, "Connection lost (server not responding)");
                return false;
            }

            await sendTask; // observe/rethrow any exception the send itself threw
            return true;
        }
        catch
        {
            HandleDeadConnection(session, "Connection lost (failed to reach server)");
            return false;
        }
    }

    private void HandleDeadConnection(ArchipelagoSession session, string message)
    {
        if (!ReferenceEquals(_session, session))
            return; // a newer connection already took over - not this one's news to report

        _session = null;
        SlotData = null;
        RaiseState(ApConnectionState.Disconnected, message);

        // Best-effort, fire-and-forget cleanup - we already know this
        // socket isn't behaving, so there's nothing to gain from waiting
        // on it further.
#pragma warning disable CS4014
        try { session.Socket.DisconnectAsync(); } catch { /* best-effort */ }
#pragma warning restore CS4014
    }

    public void Disconnect()
    {
        bool wasConnected = _session is not null;
        DisconnectInternal();
        if (wasConnected)
            RaiseState(ApConnectionState.Disconnected, "Disconnected");
    }

    private void DisconnectInternal()
    {
        if (_session is { } session)
        {
            _session = null;
            SlotData = null;

            // Called from sync contexts (Disconnect(), Dispose(), and the
            // top of ConnectAsync before the new session exists) where we
            // can't await, so this is intentionally fire-and-forget - we
            // don't need to know the socket finished closing before moving on.
#pragma warning disable CS4014
            try { session.Socket.DisconnectAsync(); } catch { /* best-effort */ }
#pragma warning restore CS4014
        }
    }

    private void RaiseState(ApConnectionState state, string message)
    {
        State = state;
        StatusMessage = message;
        StateChanged?.Invoke(state, message);
    }

    public void Dispose() => DisconnectInternal();
}
