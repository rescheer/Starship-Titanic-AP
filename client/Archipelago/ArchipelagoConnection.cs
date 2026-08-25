using System.Linq;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
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

    // The library's SocketClosed event only fires for a socket the local
    // machine actually knows is closed (a clean close handshake, or a
    // failed send). If the server process dies or the network just goes
    // away without either side sending a close frame, the local socket can
    // sit there looking "open" indefinitely. To catch that, we actively
    // probe the server on a timer - if the probe itself fails to send, we
    // treat the connection as dead ourselves rather than trusting the
    // socket's own idea of its state.
    //
    // NOTE: we deliberately do NOT treat "no response to the probe" as
    // evidence of a dead connection. SyncPacket only makes the server send
    // a ReceivedItems packet back when there's actually something to
    // (re)sync - a quiet room with nothing new can go a long time with a
    // perfectly healthy connection and zero incoming packets, so waiting
    // for a reply produced constant false-positive disconnects.
    private const int HeartbeatIntervalSeconds = 10;

    private ArchipelagoSession? _session;
    private System.Threading.Timer? _heartbeatTimer;
    private readonly LocationCheckQueue _checkQueue = new();

    public ApConnectionState State { get; private set; } = ApConnectionState.Disconnected;
    public string StatusMessage { get; private set; } = "Not connected";
    public ArchipelagoSession? Session => _session;
    public bool IsConnected => _session is not null;
    public int PendingCheckCount => _checkQueue.Count;

    public event Action<ApConnectionState, string>? StateChanged;

    /// <summary>
    /// Fires for every server log-line the AP client library surfaces via
    /// session.MessageLog (item sends/receives, hints, chat, join/leave,
    /// etc.), already formatted as plain text via LogMessage.ToString().
    /// Like StateChanged, this can fire from a background thread.
    /// </summary>
    public event Action<string>? MessageReceived;

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
                StopHeartbeat();
                RaiseState(ApConnectionState.Disconnected, $"Disconnected: {reason}");
            }
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

        if (result is LoginSuccessful)
        {
            _session = session;
            session.MessageLog.OnMessageReceived += message => MessageReceived?.Invoke(message.ToString());

            FlushPendingChecks(session);

            _heartbeatTimer = new System.Threading.Timer(
                _ => HeartbeatTick(session),
                null,
                TimeSpan.FromSeconds(HeartbeatIntervalSeconds),
                TimeSpan.FromSeconds(HeartbeatIntervalSeconds));

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
    /// Returns false if there's no active session or the send failed.
    /// </summary>
    public bool SendCommand(string commandText)
    {
        if (_session is not { } session)
            return false;

        try
        {
            session.Socket.SendPacket(new SayPacket { Text = commandText });
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Reports a completed location check to the server. Safe to call
    /// repeatedly for the same id - the server treats duplicate checks as
    /// a no-op, so callers don't need to track what's already been sent.
    /// If there's no active session, or the send fails, the id is queued
    /// (see LocationCheckQueue) and retried automatically on next connect.
    /// Returns false if it had to be queued rather than sent immediately.
    /// </summary>
    public bool SendLocationCheck(long locationId)
    {
        if (_session is not { } session)
        {
            _checkQueue.Enqueue(locationId);
            return false;
        }

        try
        {
            session.Locations.CompleteLocationChecks(locationId);
            return true;
        }
        catch
        {
            _checkQueue.Enqueue(locationId);
            return false;
        }
    }

    /// <summary>
    /// Attempts to send every queued check now that we're connected. Runs
    /// synchronously right as the session is established, before the
    /// Connected state is raised, so anything still pending after this
    /// genuinely couldn't be delivered (rather than a timing fluke).
    /// </summary>
    private void FlushPendingChecks(ArchipelagoSession session)
    {
        _checkQueue.Flush(id =>
        {
            if (!ReferenceEquals(_session, session))
                return false; // session already moved on - let the next connect retry

            try
            {
                session.Locations.CompleteLocationChecks(id);
                return true;
            }
            catch
            {
                return false;
            }
        });
    }

    /// <summary>
    /// Runs on a thread-pool timer thread every HeartbeatIntervalSeconds
    /// while connected. If a probe packet fails to even send, we declare
    /// the connection dead ourselves - this is what catches a server that
    /// vanished without a clean close.
    /// </summary>
    private void HeartbeatTick(ArchipelagoSession session)
    {
        // A newer ConnectAsync call (or Disconnect()) may have already
        // moved on from this session - a timer for an old session should
        // do nothing rather than race with the current one.
        if (!ReferenceEquals(_session, session))
            return;

        try
        {
            // SyncPacket ("please resend my items") is a normal, documented
            // part of the protocol - a lightweight way to actively write to
            // the socket so a truly-dead connection reveals itself via a
            // failed send. We intentionally don't wait for or require a
            // reply - see the class-level note on HeartbeatIntervalSeconds.
            session.Socket.SendPacket(new SyncPacket());
        }
        catch
        {
            _session = null;
            StopHeartbeat();
            RaiseState(ApConnectionState.Disconnected, "Connection lost (failed to reach server)");
        }
    }

    private void StopHeartbeat()
    {
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;
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
            StopHeartbeat();

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
