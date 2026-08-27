using System.Net;
using FHT.Access.Domain.Abstractions;
using FHT.Access.Domain.Entities;
using FHT.Access.Domain.Enums;
using Toletus.LiteNet3;
using Toletus.LiteNet3.Handler.Responses.NotificationsResponses;
using Toletus.LiteNet3.Handler.Responses.NotificationsResponses.Base;

namespace FHT.Access.Toletus;

/// <summary>
/// LiteNet3 adapter — discovery → CreateFromBase → ConnectAsync(NIC name) → Connected → Release.
/// Does not treat the board as LiteNet2 / fire-and-forget Release by IP.
/// </summary>
public sealed class ToletusLiteNetTurnstile : ITurnstile
{
    public static readonly TimeSpan DiscoveryWait = TimeSpan.FromSeconds(2.5);

    private readonly object _sync = new();
    private readonly Action<string>? _info;
    private readonly Action<string>? _error;
    private LiteNet3Board? _board;
    private string? _nicName;
    private TurnstileConnectionState _state = TurnstileConnectionState.Disconnected;

    public ToletusLiteNetTurnstile(
        Action<string>? information = null,
        Action<string>? error = null)
    {
        _info = information;
        _error = error;
    }

    public TurnstileConnectionState State
    {
        get
        {
            lock (_sync) return _state;
        }
    }

    public event EventHandler<TurnstileConnectionState>? StateChanged;
    public event EventHandler<PassageOutcome>? PassageReceived;

    public async Task ConnectAsync(TurnstileConfig config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        await DisconnectAsync(ct).ConfigureAwait(false);
        SetState(TurnstileConnectionState.Discovering);

        try
        {
            var nic = LocalNetworkResolver.Resolve(config.NetworkInterface, config.BoardIp);
            _nicName = nic.Name;
            Log(
                $"NIC escolhida: Name='{nic.Name}' IPv4={nic.Ipv4} Desc='{nic.Description}' "
                + $"(config NetworkInterface='{config.NetworkInterface}', BoardIp='{config.BoardIp}')");

            var discovered = await DiscoverAsync(nic, config, ct).ConfigureAwait(false);
            SetState(TurnstileConnectionState.Connecting);
            var match = SelectBoard(discovered, config);
            if (match is null)
            {
                throw new InvalidOperationException(
                    $"LiteNet3 não descoberta após Search em {nic.Ipv4} "
                    + $"(BoardIp={config.BoardIp}, Serial={config.Serial}). "
                    + $"Encontradas: {Summarize(discovered)}");
            }

            if (string.IsNullOrWhiteSpace(match.Serial))
            {
                throw new InvalidOperationException(
                    $"Placa em {match.Ip} respondeu discovery sem Serial — não é possível ConnectAsync.");
            }

            Log(
                $"Placa descoberta: Ip={match.Ip} Serial={match.Serial} Id={match.Id} "
                + $"Alias={match.Alias} ConnectedFlag={match.Connected}");

            // Persist discovered serial/IP back into config for Admin save.
            config.Serial = match.Serial;
            config.BoardIp = match.Ip.ToString();
            config.NetworkInterface = nic.Name;

            var board = LiteNet3Board.CreateFromBase(match);
            board.OnReleaseResponse = OnBoardReleaseResponse;

            // Preview the WS URI the vendor will open (port is private; log after connect via ServerUri).
            Log(
                $"ConnectAsync('{nic.Name}'): boardIp={board.Ip} serial={board.Serial} "
                + $"— SetServer UDP → placa deve conectar em ws://{nic.Ipv4}:<porta_dinamica>/");

            try
            {
                await board.ConnectAsync(nic.Name, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogError(
                    $"ConnectAsync timeout/erro: {ex.GetType().Name}: {ex.Message} | "
                    + $"NetworkIp={board.NetworkIp} ServerUri={board.ServerUri} Connected={board.Connected}");
                throw;
            }

            Log(
                $"Após ConnectAsync: Connected={board.Connected} NetworkIp={board.NetworkIp} "
                + $"ServerUri={board.ServerUri} (URI WebSocket / porta dinâmica)");

            if (!board.Connected)
            {
                throw new TimeoutException(
                    "LiteNet3 ConnectAsync retornou sem Connected==true. "
                    + $"NetworkIp={board.NetworkIp} ServerUri={board.ServerUri}. "
                    + "Verifique firewall (entrada TCP na porta dinâmica em 192.168.0.120) e SetServer.");
            }

            if (board.NetworkIp is null || !board.NetworkIp.Equals(nic.Ipv4))
            {
                throw new InvalidOperationException(
                    $"WebSocket escutou em {board.NetworkIp} em vez de {nic.Ipv4} ({nic.Name}).");
            }

            Log($"Conexão recebida da placa — Connected=true, ServerUri={board.ServerUri}");

            lock (_sync)
            {
                _board = board;
            }

            SetState(TurnstileConnectionState.Connected);
        }
        catch (Exception ex)
        {
            SetState(TurnstileConnectionState.Error);
            LogError($"Erro de conexão LiteNet3: {ex}");
            throw;
        }
    }

    public Task DisconnectAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        LiteNet3Board? board;
        lock (_sync)
        {
            board = _board;
            _board = null;
        }

        if (board is not null)
        {
            board.OnReleaseResponse = null;
            try
            {
                board.Close();
            }
            catch (Exception ex)
            {
                Log($"Close: {ex.Message}");
            }
        }

        _nicName = null;
        SetState(TurnstileConnectionState.Disconnected);
        return Task.CompletedTask;
    }

    public Task ReleaseEntryAsync(string? top = null, string? bottom = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var board = RequireConnectedBoard();
        Log($"ReleaseEntry serial={board.Serial} ip={board.Ip} top={top}");
        SetState(TurnstileConnectionState.WaitingPassage);
        board.ReleaseEntry(top, bottom);
        return Task.CompletedTask;
    }

    public Task ReleaseExitAsync(string? top = null, string? bottom = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var board = RequireConnectedBoard();
        Log($"ReleaseExit serial={board.Serial} ip={board.Ip} top={top}");
        SetState(TurnstileConnectionState.WaitingPassage);
        board.ReleaseExit(top, bottom);
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
    }

    private LiteNet3Board RequireConnectedBoard()
    {
        lock (_sync)
        {
            if (_board is null
                || !_board.Connected
                || State is TurnstileConnectionState.Disconnected or TurnstileConnectionState.Error)
            {
                throw new InvalidOperationException(
                    "LiteNet3 não conectada (Connected!=true). Conecte antes de liberar.");
            }

            return _board;
        }
    }

    private async Task<List<LiteNet3BoardBase>> DiscoverAsync(
        LocalNetworkResolver.NicInfo nic,
        TurnstileConfig config,
        CancellationToken ct)
    {
        Log($"Discovery: LiteNetUtil.Search({nic.Ipv4}) via NIC '{nic.Name}' — aguardando {DiscoveryWait.TotalSeconds:0.0}s por UDP :7878");

        // Search returns the live DiscoveredBoards list; responses arrive asynchronously.
        var boards = LiteNetUtil.Search(nic.Ipv4) ?? [];
        try
        {
            await Task.Delay(DiscoveryWait, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }

        // Second pass by NIC name (vendor helper) if still empty.
        if (boards.Count == 0)
        {
            Log($"Discovery vazio após Search(IP); tentando LiteNetUtil.Search('{nic.Name}', id:null)");
            var one = LiteNetUtil.Search(nic.Name, id: null);
            if (one is not null)
                boards = [one];
            else
            {
                boards = LiteNetUtil.Search(nic.Ipv4) ?? [];
                await Task.Delay(DiscoveryWait, ct).ConfigureAwait(false);
            }
        }

        Log($"Discovery resultado: {boards.Count} placa(s) — {Summarize(boards)}");
        return boards.ToList();
    }

    private static LiteNet3BoardBase? SelectBoard(
        IReadOnlyList<LiteNet3BoardBase> discovered,
        TurnstileConfig config)
    {
        if (discovered.Count == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(config.Serial))
        {
            var bySerial = discovered.FirstOrDefault(b =>
                string.Equals(b.Serial, config.Serial, StringComparison.OrdinalIgnoreCase));
            if (bySerial is not null)
                return bySerial;
        }

        if (!string.IsNullOrWhiteSpace(config.BoardIp)
            && IPAddress.TryParse(config.BoardIp, out var wantIp))
        {
            var byIp = discovered.FirstOrDefault(b => b.Ip is not null && b.Ip.Equals(wantIp));
            if (byIp is not null)
                return byIp;
        }

        // Single board on the link — take it.
        return discovered.Count == 1 ? discovered[0] : discovered.FirstOrDefault(b =>
            !string.IsNullOrWhiteSpace(b.Serial));
    }

    private static string Summarize(IEnumerable<LiteNet3BoardBase> boards)
        => string.Join("; ", boards.Select(b => $"ip={b.Ip} serial={b.Serial} id={b.Id}"));

    private void OnBoardReleaseResponse(LiteNet3BoardBase board, ReleaseBase response)
    {
        var outcome = response switch
        {
            PassageResponse => PassageOutcome.PassageDetected,
            TimeoutResponse => PassageOutcome.Timeout,
            _ => PassageOutcome.Unknown
        };

        Log($"PassageResponse: {response.GetType().Name} => {outcome}");
        PassageReceived?.Invoke(this, outcome);

        if (State == TurnstileConnectionState.WaitingPassage)
            SetState(TurnstileConnectionState.Connected);
    }

    private void SetState(TurnstileConnectionState state)
    {
        lock (_sync)
        {
            if (_state == state)
                return;
            _state = state;
        }

        StateChanged?.Invoke(this, state);
    }

    private void Log(string message) => _info?.Invoke($"[Toletus] {message}");

    private void LogError(string message) => _error?.Invoke($"[Toletus] {message}");
}
