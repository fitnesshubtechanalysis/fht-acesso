using FHT.Access.Domain.Abstractions;
using FHT.Access.Domain.Entities;
using FHT.Access.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace FHT.Access.Application.Services;



/// <summary>

/// Background recognition pipeline for one gate lane (entry or exit camera).

/// </summary>

public sealed class AutomaticAccessEngine : IAsyncDisposable

{

    public const string EntryCameraOwner = "automatic-entry";

    public const string ExitCameraOwner = "automatic-exit";

    public const string CameraOwner = EntryCameraOwner;



    private readonly OperatingModeService _mode;

    private readonly AccessStateMachine _states;

    private readonly RecognitionSessionGuard _sessions;

    private readonly CameraCoordinator _camera;

    private readonly RecognitionService _recognition;

    private readonly AccessFlowService _flow;

    private readonly AccessDirection _laneDirection;
    private readonly string _cameraOwner;
    private readonly LaneRecognitionProfile _profile;
    private readonly ILogger<AutomaticAccessEngine>? _logger;

    public static readonly TimeSpan DefaultApproachHold = TimeSpan.FromMilliseconds(700);
    public static readonly TimeSpan DefaultSettleBeforeIdentify = TimeSpan.FromSeconds(1.4);
    public static readonly TimeSpan IdentifyInterval = TimeSpan.FromMilliseconds(120);
    public const int DefaultIdentifyAttempts = 16;

    public static readonly TimeSpan MinRecognizingDisplay = TimeSpan.FromSeconds(2.0);

    public static readonly TimeSpan MinPendingDisplay = TimeSpan.FromSeconds(2);



    private Func<byte[]?>? _captureJpeg;

    private Func<bool>? _hasFaceHint;

    private CancellationTokenSource? _cts;

    private Task? _loop;

    private bool _started;

    private DateTime? _approachStartedUtc;

    private DateTime? _approachLostUtc;

    private static readonly TimeSpan ApproachMissGrace = TimeSpan.FromMilliseconds(500);

    private DateTime _skipApproachUntilUtc;

    private DateTime _releaseMessageShownUtc;

    /// <summary>
    /// Aluno que acabou de ser liberado. Não abre de novo enquanto o rosto
    /// não sair da câmera — frame preso ou fundo vazio não vira outra entrada.
    /// </summary>
    private Guid? _latchedMemberId;

    private DateTime? _faceAbsentSinceUtc;

    private static readonly TimeSpan FaceAbsenceToRearm = TimeSpan.FromMilliseconds(2000);

    private TaskCompletionSource? _resultHold;

    /// <summary>How long "Entrada/Saída registrada" stays on screen.</summary>
    public TimeSpan PassageSuccessDisplay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Minimum time for "Pode passar na catraca/saída" before success screen.</summary>
    public TimeSpan PassageReleaseMinDisplay { get; set; } = TimeSpan.FromSeconds(3);

    public AccessDirection LaneDirection => _laneDirection;



    public AutomaticAccessEngine(

        OperatingModeService mode,

        AccessStateMachine states,

        RecognitionSessionGuard sessions,

        CameraCoordinator camera,

        RecognitionService recognition,

        AccessFlowService flow,
        AccessDirection laneDirection = AccessDirection.Entry,
        string? cameraOwner = null,
        LaneRecognitionProfile? profile = null,
        ILogger<AutomaticAccessEngine>? logger = null)
    {
        _mode = mode;

        _states = states;

        _sessions = sessions;

        _camera = camera;

        _recognition = recognition;

        _flow = flow;

        _laneDirection = laneDirection;

        _cameraOwner = cameraOwner ?? EntryCameraOwner;

        _profile = profile ?? (laneDirection == AccessDirection.Exit
            ? LaneRecognitionProfile.Exit
            : LaneRecognitionProfile.Entry);

        _logger = logger;

        _mode.ModeChanged += OnModeChanged;

    }



    public void BindCamera(Func<byte[]?> captureJpeg, Func<bool>? hasFaceHint = null)

    {

        _captureJpeg = captureJpeg;

        _hasFaceHint = hasFaceHint;

    }



    public void RetryFromKiosk()

    {

        _skipApproachUntilUtc = DateTime.UtcNow.AddSeconds(3);
        _approachStartedUtc = DateTime.UtcNow.Subtract(_profile.ApproachHold);

        _sessions.AllowImmediateRetry();

        if (_states.ActiveLane is null || _states.ActiveLane == _laneDirection)
            _states.ResetAutomaticIdle(_laneDirection);

        _resultHold?.TrySetResult();

    }



    public void Start()

    {

        if (_started)

            return;

        _started = true;

        _cts = new CancellationTokenSource();

        _loop = Task.Run(() => RunAsync(_cts.Token));

    }



    public async ValueTask DisposeAsync()

    {

        _mode.ModeChanged -= OnModeChanged;

        if (_cts is not null)

        {

            await _cts.CancelAsync().ConfigureAwait(false);

            _cts.Dispose();

            _cts = null;

        }



        if (_loop is not null)

        {

            try { await _loop.ConfigureAwait(false); } catch { /* ignore */ }

            _loop = null;

        }



        _camera.Release(_cameraOwner);

        _started = false;

    }



    private void OnModeChanged(object? sender, AccessOperatingMode mode)

    {

        if (mode != AccessOperatingMode.Automatic)

            _sessions.CompleteSession();

        else if (_states.ActiveLane is null || _states.ActiveLane == _laneDirection)
            _states.ResetAutomaticIdle(_laneDirection);

    }



    private async Task RunAsync(CancellationToken ct)

    {

        while (!ct.IsCancellationRequested)

        {

            try

            {

                if (!_mode.RecognitionEnabled)

                {

                    _camera.Release(_cameraOwner);

                    await Task.Delay(200, ct).ConfigureAwait(false);

                    continue;

                }



                if (_sessions.IsInCooldown || _sessions.HasActiveSession)

                {

                    if (_sessions.IsSessionExpired())

                    {

                        _sessions.CompleteSession();

                        if (_states.ActiveLane == _laneDirection)

                            _states.ResetAutomaticIdle(_laneDirection);

                    }



                    await Task.Delay(100, ct).ConfigureAwait(false);

                    continue;

                }



                var approaching = _hasFaceHint?.Invoke() ?? false;
                _sessions.MarkFacePresent(approaching);

                if (!approaching)

                {

                    _approachLostUtc ??= DateTime.UtcNow;

                    if (DateTime.UtcNow - _approachLostUtc.Value < ApproachMissGrace)

                    {

                        await Task.Delay(80, ct).ConfigureAwait(false);

                        continue;

                    }



                    _approachStartedUtc = null;

                    _faceAbsentSinceUtc ??= DateTime.UtcNow;

                    if (_latchedMemberId is not null
                        && DateTime.UtcNow - _faceAbsentSinceUtc.Value >= FaceAbsenceToRearm)
                    {
                        _latchedMemberId = null;
                    }

                    if (_states.ActiveLane == _laneDirection && _states.State != AccessUiState.AutomaticIdle)

                        _states.ResetAutomaticIdle(_laneDirection);

                    await Task.Delay(150, ct).ConfigureAwait(false);

                    continue;

                }



                _approachStartedUtc ??= DateTime.UtcNow;

                _approachLostUtc = null;

                _faceAbsentSinceUtc = null;

                var skipApproach = DateTime.UtcNow < _skipApproachUntilUtc;

                if (!skipApproach && DateTime.UtcNow - _approachStartedUtc.Value < _profile.ApproachHold)

                {

                    await Task.Delay(80, ct).ConfigureAwait(false);

                    continue;

                }

                // Já liberou esta pessoa e ela ainda está na frente da câmera.
                // Não abre o reconhecimento de novo — isso piscava a câmera e voltava ao descanso.
                if (_latchedMemberId is not null)
                {
                    await Task.Delay(200, ct).ConfigureAwait(false);
                    continue;
                }

                if (!_states.CanLaneTakeUi(_laneDirection))
                {
                    await Task.Delay(100, ct).ConfigureAwait(false);
                    continue;
                }

                if (!_sessions.TryBeginSession())

                {

                    await Task.Delay(100, ct).ConfigureAwait(false);

                    continue;

                }



                _approachStartedUtc = null;



                if (!_mode.RecognitionEnabled)

                {

                    _sessions.CompleteSession();

                    continue;

                }



                _camera.TryAcquire(CameraUsageMode.Recognition, _cameraOwner);

                _states.SetActiveLane(_laneDirection);

                _states.TransitionTo(AccessUiState.FaceDetected);

                _states.TransitionTo(AccessUiState.Recognizing);



                var recognizingStarted = DateTime.UtcNow;

                await Task.Delay(_profile.SettleBeforeIdentify, ct).ConfigureAwait(false);



                FaceMatchResult? bestMatch = null;
                var consensusHits = 0;
                Guid? consensusMemberId = null;
                const int requiredConsensus = 2;

                for (var attempt = 0; attempt < _profile.IdentifyAttempts; attempt++)
                {
                    if (!_mode.RecognitionEnabled)
                        break;

                    var frame = _captureJpeg?.Invoke();
                    if (frame is not null && frame.Length >= 100)
                    {
                        var next = await _recognition
                            .IdentifyOnlyAsync(frame, ct, _profile.FaceDetection)
                            .ConfigureAwait(false);

                        if (next is not null)
                        {
                            if (consensusMemberId == next.MemberId)
                            {
                                consensusHits++;
                                if (bestMatch is null
                                    || bestMatch.MemberId != next.MemberId
                                    || next.Score >= bestMatch.Score)
                                {
                                    bestMatch = next;
                                }
                            }
                            else
                            {
                                consensusMemberId = next.MemberId;
                                consensusHits = 1;
                                bestMatch = next;
                            }

                            if (consensusHits >= requiredConsensus || next.Score >= 0.66)
                                break;
                        }
                    }

                    await Task.Delay(IdentifyInterval, ct).ConfigureAwait(false);
                }

                // Um quadro muito parecido basta. Abaixo disso, exige o mesmo aluno duas vezes.
                if (consensusHits < requiredConsensus && (bestMatch is null || bestMatch.Score < 0.66))
                    bestMatch = null;

                if (bestMatch is not null && bestMatch.MemberId == _latchedMemberId)
                {
                    _sessions.CompleteSession();
                    if (_states.ActiveLane == _laneDirection)
                        _states.ResetAutomaticIdle(_laneDirection);
                    await Task.Delay(400, ct).ConfigureAwait(false);
                    continue;
                }



                AccessDecision decision = bestMatch is not null

                    ? await _recognition.DecideFromMatchAsync(bestMatch, ct).ConfigureAwait(false)

                    : _recognition.DecideUnknown();



                if (bestMatch is null)

                {

                    _logger?.LogInformation(

                        "Face identify failed on {Lane} after {Attempts} attempts",

                        _laneDirection,

                        _profile.IdentifyAttempts);

                }



                var remaining = TimeSpan.FromMilliseconds(400) - (DateTime.UtcNow - recognizingStarted);

                if (remaining > TimeSpan.Zero && bestMatch is null)

                    await Task.Delay(remaining, ct).ConfigureAwait(false);



                if (!_mode.RecognitionEnabled)

                {

                    _sessions.CompleteSession();

                    continue;

                }



                if (decision.ReasonCode == AccessDecisionService.ReasonMemberNotFound

                    || decision.MemberId is null && !decision.Allowed)

                {

                    _states.SetActiveLane(_laneDirection);

                    _states.TransitionTo(

                        AccessUiState.Unknown,

                        statusMessage: LaneUnknownMessage());

                    await WaitResultOrUiAsync(TimeSpan.FromSeconds(22), ct).ConfigureAwait(false);

                    if (_states.State == AccessUiState.Unknown && _states.ActiveLane == _laneDirection)

                    {

                        _sessions.CompleteSession();

                        _states.ResetAutomaticIdle(_laneDirection);

                    }

                    continue;

                }



                if (!decision.Allowed)

                {

                    await _flow.ProcessDeniedOnlyAsync(decision, _laneDirection, ct).ConfigureAwait(false);

                    _states.SetActiveLane(_laneDirection);

                    _states.TransitionTo(

                        AccessUiState.Denied,

                        statusMessage: AccessDenialMessages.ForKiosk(decision),

                        memberDisplayName: decision.MemberName,

                        memberId: decision.MemberId);

                    await Task.Delay(_sessions.ResultDisplayDuration, ct).ConfigureAwait(false);

                    _sessions.CompleteSession();

                    if (_states.ActiveLane == _laneDirection)

                        _states.ResetAutomaticIdle(_laneDirection);

                    continue;

                }



                _states.SetActiveLane(_laneDirection);
                _releaseMessageShownUtc = DateTime.UtcNow;
                if (decision.MemberId is { } releasedId)
                    _latchedMemberId = releasedId;

                var result = await _flow
                    .ProcessAuthorizedPassageAsync(
                        decision,
                        AccessFlowService.SourceFace,
                        _laneDirection,
                        onTurnstileReleased: _ =>
                        {
                            _releaseMessageShownUtc = DateTime.UtcNow;
                            _states.SetActiveLane(_laneDirection);
                            _states.TransitionTo(
                                AccessUiState.WaitingPassage,
                                statusMessage: BuildReleaseMessage(decision),
                                memberDisplayName: decision.MemberName,
                                memberId: decision.MemberId);
                            return Task.CompletedTask;
                        },
                        ct)
                    .ConfigureAwait(false);

                var confirmed = result.Passage == PassageOutcome.PassageDetected;
                var passageMissed = result.Passage == PassageOutcome.Timeout;

                if (confirmed)
                {
                    var sinceRelease = DateTime.UtcNow - _releaseMessageShownUtc;
                    if (sinceRelease < PassageReleaseMinDisplay)
                    {
                        await Task.Delay(PassageReleaseMinDisplay - sinceRelease, ct)
                            .ConfigureAwait(false);
                    }

                    _states.SetActiveLane(_laneDirection);
                    _states.TransitionTo(
                        AccessUiState.PassageConfirmed,
                        statusMessage: BuildSuccessMessage(decision),
                        memberDisplayName: decision.MemberName,
                        memberId: decision.MemberId);
                    await Task.Delay(PassageSuccessDisplay, ct).ConfigureAwait(false);
                    _sessions.CompleteSession();
                }
                else
                {
                    _states.SetActiveLane(_laneDirection);
                    _states.TransitionTo(
                        AccessUiState.Denied,
                        statusMessage: result.UiMessage,
                        memberDisplayName: decision.MemberName,
                        memberId: decision.MemberId);

                    if (passageMissed && _profile.ImmediateRetryAfterPassageFailure)
                    {
                        await Task.Delay(_profile.PassageFailureDisplay, ct).ConfigureAwait(false);
                        // A pessoa não girou o braço. Solta o reconhecimento para a próxima
                        // aproximação — sem isso a tela de descanso ficava presa até ela sumir da câmera.
                        _latchedMemberId = null;
                        _faceAbsentSinceUtc = null;
                        _sessions.AllowImmediateRetry();
                    }
                    else
                    {
                        await Task.Delay(_sessions.ResultDisplayDuration, ct).ConfigureAwait(false);
                        _sessions.CompleteSession();
                    }
                }

                if (_states.ActiveLane == _laneDirection)
                    _states.ResetAutomaticIdle(_laneDirection);
            }

            catch (OperationCanceledException) when (ct.IsCancellationRequested)

            {

                break;

            }

            catch (Exception ex)

            {

                _logger?.LogWarning(ex, "AutomaticAccessEngine loop error ({Lane})", _laneDirection);

                _sessions.CompleteSession();

                try

                {

                    if (_states.ActiveLane == _laneDirection)

                        _states.ResetAutomaticIdle(_laneDirection);

                }

                catch { /* ignore */ }

                await Task.Delay(500, CancellationToken.None).ConfigureAwait(false);

            }

        }

    }



    private string LaneUnknownMessage()

        => _laneDirection == AccessDirection.Exit

            ? "Não foi possível identificar seu rosto na saída."

            : "Não foi possível identificar seu rosto.";



    private static bool IsEmployee(AccessDecision decision) =>
        string.Equals(decision.OperationalStatus, "employee", StringComparison.OrdinalIgnoreCase);

    private string BuildReleaseMessage(AccessDecision decision)
    {
        if (decision.Kind == AccessDecisionKind.AllowTolerance)
            return $"Olá, {decision.MemberName}!\n\n{decision.PublicMessage}";

        if (_laneDirection == AccessDirection.Entry)
            return WelcomeEntry(decision.MemberName);

        if (IsEmployee(decision))
        {
            var name = string.IsNullOrWhiteSpace(decision.MemberName) ? "Profissional" : decision.MemberName;
            return $"Funcionário / Profissional\n{name}\n\nPode passar na saída.";
        }

        return $"Olá, {decision.MemberName}!\n\nPode passar na saída.";
    }

    private static string WelcomeEntry(string? name)
    {
        var who = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        return who is null
            ? "Seja bem-vindo, entrada liberada!"
            : $"Olá, {who}!\nSeja bem-vindo, entrada liberada!";
    }

    private string BuildSuccessMessage(AccessDecision decision)

    {

        if (decision.Kind == AccessDecisionKind.AllowTolerance)

            return $"Olá, {decision.MemberName}!\n\n{decision.PublicMessage}";

        if (IsEmployee(decision))
        {
            var name = string.IsNullOrWhiteSpace(decision.MemberName) ? "Profissional" : decision.MemberName;
            return _laneDirection == AccessDirection.Exit
                ? $"Funcionário / Profissional\n{name}\n\nSaída registrada."
                : $"Funcionário / Profissional\n{name}\n\nEntrada registrada.";
        }

        return _laneDirection == AccessDirection.Exit
            ? $"Olá, {decision.MemberName}!\n\nSaída registrada.\nAté breve!"
            : $"Olá, {decision.MemberName}!\n\nEntrada registrada.\nTenha um ótimo treino!";

    }



    private async Task WaitResultOrUiAsync(TimeSpan duration, CancellationToken ct)

    {

        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _resultHold = hold;

        try

        {

            using var reg = ct.Register(() => hold.TrySetCanceled(ct));

            await Task.WhenAny(Task.Delay(duration, ct), hold.Task).ConfigureAwait(false);

        }

        finally

        {

            if (ReferenceEquals(_resultHold, hold))

                _resultHold = null;

        }

    }

}

