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

/// <summary>Thin wrapper around an Archipelago.MultiClient.Net session, owning the connect/disconnect lifecycle.</summary>
public sealed class ArchipelagoConnection : IDisposable
{
    private const string GameName = "Starship Titanic";

    private const int SendTimeoutMs = 5000;

    private ArchipelagoSession? _session;
    private readonly LocationCheckQueue _checkQueue = new();

    public ApConnectionState State { get; private set; } = ApConnectionState.Disconnected;
    public string StatusMessage { get; private set; } = "Not connected";
    public ArchipelagoSession? Session => _session;
    public bool IsConnected => _session is not null;
    public int PendingCheckCount => _checkQueue.Count;

    /// <summary>Snapshot of every location name currently queued but not yet confirmed sent.</summary>
    public string[] GetPendingCheckNames() => _checkQueue.Snapshot().Select(c => c.LocationName).ToArray();

    /// <summary>Resolves an AP location name to its numeric id via the connected session's data package.</summary>
    public long? ResolveLocationId(string locationName)
    {
        if (_session is not { } session)
            return null;

        long id = session.Locations.GetLocationIdFromName(GameName, locationName);
        return id == -1 ? null : id;
    }

    /// <summary>Resolves a location id back to its AP display name via the connected session's data package.</summary>
    public string? ResolveLocationName(long locationId)
    {
        return _session?.Locations.GetLocationNameFromId(locationId);
    }

    /// <summary>Checked/total location counts for this slot.</summary>
    public (int Checked, int Total)? GetLocationCheckSummary()
    {
        if (_session is not { } session)
            return null;

        return (session.Locations.AllLocationsChecked.Count, session.Locations.AllLocations.Count);
    }

    /// <summary>Checked/total location counts for this slot, scoped to the given location names.</summary>
    public (int Checked, int Total)? GetLocationCheckSummary(IReadOnlyCollection<string> locationNames)
    {
        if (_session is not { } session)
            return null;

        var checkedIds = session.Locations.AllLocationsChecked;
        int total = 0;
        int checkedCount = 0;
        foreach (string name in locationNames)
        {
            long id = session.Locations.GetLocationIdFromName(GameName, name);
            if (id == -1)
                continue;
            total++;
            if (checkedIds.Contains(id))
                checkedCount++;
        }
        return (checkedCount, total);
    }

    /// <summary>Whether the server already has this location marked checked for this slot.</summary>
    public bool IsLocationChecked(string locationName)
    {
        if (_session is not { } session)
            return false;

        long id = session.Locations.GetLocationIdFromName(GameName, locationName);
        return id != -1 && session.Locations.AllLocationsChecked.Contains(id);
    }

    /// <summary>Drops every queued-but-unsent location check without sending it.</summary>
    public void ClearPendingChecks() => _checkQueue.Clear();

    /// <summary>The slot data the server sent back on login.</summary>
    public IReadOnlyDictionary<string, object>? SlotData { get; private set; }

    /// <summary>The server's seed_name, captured once login succeeds.</summary>
    public string? SeedName { get; private set; }

    private bool _pendingChecksFlushedThisSession;

    private readonly object _itemsLock = new();
    private readonly List<string> _receivedItemNames = new();

    private readonly HashSet<(long ItemId, long LocationId)> _seenItemLocationPairs = new();

    /// <summary>Snapshot of every item name received so far this connection, in order.</summary>
    public string[] GetReceivedItemNames()
    {
        lock (_itemsLock)
            return _receivedItemNames.ToArray();
    }

    public event Action<ApConnectionState, string>? StateChanged;

    /// <summary>Fires for every server log-line the AP client library surfaces, formatted as plain text.</summary>
    public event Action<string>? MessageReceived;

    /// <summary>Fires once per item as it's received.</summary>
    public event Action<string>? ItemReceived;

    /// <summary>Fires when a location check is newly added to the pending-check queue.</summary>
    public event Action<string>? CheckQueued;

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

        DisconnectInternal();
        lock (_itemsLock)
        {
            _receivedItemNames.Clear();
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
            if (ReferenceEquals(_session, session))
            {
                _session = null;
                SlotData = null;
                SeedName = null;
                _pendingChecksFlushedThisSession = false;
                RaiseState(ApConnectionState.Disconnected, $"Disconnected: {reason}");
            }
        };

        session.Items.ItemReceived += helper =>
        {
            ItemInfo item = helper.DequeueItem();
            string? name = item.ItemName;
            if (name is null)
                return;

            lock (_itemsLock)
            {
                if (!_seenItemLocationPairs.Add((item.ItemId, item.LocationId)))
                    return;
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
            SeedName = session.RoomState.Seed;
            session.MessageLog.OnMessageReceived += message => MessageReceived?.Invoke(message.ToString());

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

    /// <summary>Sends a chat message to the server as a SayPacket.</summary>
    public bool SendCommand(string commandText)
    {
        if (_session is not { } session)
            return false;

        _ = TrySendAsync(session, () => session.Socket.SendPacket(new SayPacket { Text = commandText }));
        return true;
    }

    /// <summary>
    /// Reports a completed location check to the server, identified by its AP location name.
    /// "The End - Return Home" (<see cref="LocationChecks.GoalLocationName"/>) is an event location with no
    /// network id - it's resolved locally during generation for logic purposes only, so it can never be sent
    /// as a normal location check. It's routed to <see cref="ArchipelagoSession.SetGoalAchieved"/> instead,
    /// which is the actual signal the server needs to mark the slot's goal as done.
    /// </summary>
    public bool SendLocationCheck(string locationName)
    {
        if (_checkQueue.Enqueue(locationName, SeedName))
            CheckQueued?.Invoke(locationName);

        if (_session is not { } session)
            return false;

        _ = SendLocationCheckAsync(session, locationName);
        return true;
    }

    private async Task SendLocationCheckAsync(ArchipelagoSession session, string locationName)
    {
        bool ok;
        if (string.Equals(locationName, LocationChecks.GoalLocationName, StringComparison.Ordinal))
            ok = await TrySendAsync(session, () => session.SetGoalAchieved());
        else
        {
            long? locationId = ResolveLocationId(locationName);
            if (locationId is null)
                return;
            ok = await TrySendAsync(session, () => session.Locations.CompleteLocationChecks(locationId.Value));
        }

        if (ok)
            _checkQueue.Remove(locationName);
    }

    /// <summary>Tells this connection it's now safe to replay pending checks against the currently connected seed.</summary>
    public void NotifyGameVerifiedForSeed()
    {
        if (_session is not { } session)
            return;
        if (_pendingChecksFlushedThisSession)
            return;
        _pendingChecksFlushedThisSession = true;

        _ = FlushPendingChecksAsync(session);
    }

    /// <summary>Attempts to send every queued check tagged for the currently connected seed.</summary>
    private async Task FlushPendingChecksAsync(ArchipelagoSession session)
    {
        foreach (PendingCheck check in _checkQueue.Snapshot())
        {
            if (!ReferenceEquals(_session, session))
                return;

            // A null SeedName means the check was queued with no save seed set (e.g. before a seed tag was
            // ever recorded) - never assume that's safe to flush. Only send checks explicitly tagged with
            // the currently connected seed.
            if (check.SeedName is null || !string.Equals(check.SeedName, SeedName, StringComparison.Ordinal))
                continue;

            bool ok;
            if (string.Equals(check.LocationName, LocationChecks.GoalLocationName, StringComparison.Ordinal))
                ok = await TrySendAsync(session, () => session.SetGoalAchieved());
            else
            {
                long id = session.Locations.GetLocationIdFromName(GameName, check.LocationName);
                if (id == -1)
                    continue;
                ok = await TrySendAsync(session, () => session.Locations.CompleteLocationChecks(id));
            }

            if (!ok)
                return;

            _checkQueue.Remove(check.LocationName);
        }
    }

    /// <summary>Runs a blocking send on a background thread with a bounded timeout.</summary>
    private async Task<bool> TrySendAsync(ArchipelagoSession session, Action send)
    {
        if (!ReferenceEquals(_session, session))
            return false;

        try
        {
            Task sendTask = Task.Run(send);
            Task finished = await Task.WhenAny(sendTask, Task.Delay(SendTimeoutMs));

            if (finished != sendTask)
            {
                HandleDeadConnection(session, "Connection lost (server not responding)");
                return false;
            }

            await sendTask;
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
            return;

        _session = null;
        SlotData = null;
        SeedName = null;
        _pendingChecksFlushedThisSession = false;
        RaiseState(ApConnectionState.Disconnected, message);

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
            SeedName = null;
            _pendingChecksFlushedThisSession = false;

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
