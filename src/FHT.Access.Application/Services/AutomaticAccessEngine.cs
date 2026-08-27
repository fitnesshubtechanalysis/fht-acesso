using FHT.Access.Domain.Abstractions;
using FHT.Access.Domain.Entities;
using FHT.Access.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace FHT.Access.Application.Services;

/// <summary>
/// Background recognition pipeline. Runs only while OperatingMode is Automatic.
/// Does not own WPF windows — UI observes AccessStateMachine.
/// </summary>
public sealed class AutomaticAccessEngine : IAsyncDisposable
{
    public const string CameraOwner = "automatic-engine";

    private readonly OperatingModeService _mode;
    private readonly AccessStateMachine _states;
    private readonly RecognitionSessionGuard _sessions;
    private readonly CameraCoordinator _camera;
    private readonly RecognitionService _recognition;
    private readonly AccessFlowService _flow;
    private readonly ILogger<AutomaticAccessEngine>? _logger;

    /// <summary>Person must stay in motion this long before the recognition screen opens.</summary>
    public static readonly TimeSpan ApproachHold = TimeSpan.FromMilliseconds(700);

    /// <summary>Time on the recognition UI so the person can look at the camera before matching.</summary>
    public static readonly TimeSpan SettleBeforeIdentify = TimeSpan.FromSeconds(1.4);

    public static readonly TimeSpan IdentifyInterval = TimeSpan.FromMilliseconds(250);
    public const int IdentifyAttempts = 16;

    /// <summary>Minimum time the recognition-in-progress screen stays visible.</summary>
    public static readonly TimeSpan MinRecognizingDisplay = TimeSpan.FromSeconds(4.5);

    /// <summary>How long the pending-passage screen stays visible at minimum.</summary>
    public static readonly TimeSpan MinPendingDisplay = TimeSpan.FromSeconds(2);

    private Func<byte[]?>? _captureJpeg;
    private Func<bool>? _hasFaceHint;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private bool _started;
    private DateTime? _approachStartedUtc;
    private DateTime _skipApproachUntilUtc;
    private TaskCompletionSource? _resultHold;

    public AutomaticAccessEngine(
        OperatingModeService mode,
        AccessStateMachine states,
        RecognitionSessionGuard sessions,
        CameraCoordinator camera,
        RecognitionService recognition,
        AccessFlowService flow,
        ILogger<AutomaticAccessEngine>? logger = null)
    {
        _mode = mode;
        _states = states;
        _sessions = sessions;
        _camera = camera;
        _recognition = recognition;
        _flow = flow;
        _logger = logger;
        _mode.ModeChanged += OnModeChanged;
    }

    public void BindCamera(Func<byte[]?> captureJpeg, Func<bool>? hasFaceHint = null)
    {
        _captureJpeg = captureJpeg;
        _hasFaceHint = hasFaceHint;
    }

    /// <summary>Kiosk "Tentar novamente": fecha o resultado e libera reconhecimento na hora.</summary>
    public void RetryFromKiosk()
    {
        _skipApproachUntilUtc = DateTime.UtcNow.AddSeconds(3);
        _approachStartedUtc = DateTime.UtcNow.Subtract(ApproachHold);
        _sessions.AllowImmediateRetry();
        _states.ResetAutomaticIdle();
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

        _camera.Release(CameraOwner);
        _started = false;
    }

    private void OnModeChanged(object? sender, AccessOperatingMode mode)
    {
        if (mode != AccessOperatingMode.Automatic)
        {
            _sessions.CompleteSession();
        }
        else
        {
            _states.ResetAutomaticIdle();
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!_mode.RecognitionEnabled)
                {
                    _camera.Release(CameraOwner);
                    await Task.Delay(200, ct).ConfigureAwait(false);
                    continue;
                }

                _camera.TryAcquire(CameraUsageMode.Monitoring, CameraOwner);

                if (_sessions.IsInCooldown || _sessions.HasActiveSession)
                {
                    if (_sessions.IsSessionExpired())
                    {
                        _sessions.CompleteSession();
                        _states.ResetAutomaticIdle();
                    }

                    await Task.Delay(100, ct).ConfigureAwait(false);
                    continue;
                }

                var approaching = _hasFaceHint?.Invoke() ?? false;
                if (!approaching)
                {
                    _approachStartedUtc = null;
                    if (_states.State != AccessUiState.AutomaticIdle)
                        _states.ResetAutomaticIdle();
                    await Task.Delay(150, ct).ConfigureAwait(false);
                    continue;
                }

                _approachStartedUtc ??= DateTime.UtcNow;
                var skipApproach = DateTime.UtcNow < _skipApproachUntilUtc;
                if (!skipApproach && DateTime.UtcNow - _approachStartedUtc.Value < ApproachHold)
                {
                    await Task.Delay(80, ct).ConfigureAwait(false);
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

                _camera.TryAcquire(CameraUsageMode.Recognition, CameraOwner);
                _states.TransitionTo(AccessUiState.FaceDetected);
                _states.TransitionTo(AccessUiState.Recognizing);

                var recognizingStarted = DateTime.UtcNow;
                await Task.Delay(SettleBeforeIdentify, ct).ConfigureAwait(false);

                AccessDecision? decision = null;
                for (var attempt = 0; attempt < IdentifyAttempts; attempt++)
                {
                    if (!_mode.RecognitionEnabled)
                        break;

                    var frame = _captureJpeg?.Invoke();
                    if (frame is not null && frame.Length >= 100)
                    {
                        var next = await _recognition.IdentifyAndDecideAsync(frame, ct).ConfigureAwait(false);
                        if (next is { MemberId: not null } &&
                            (decision is null || (next.Score ?? 0) >= (decision.Score ?? 0)))
                            decision = next;
                    }

                    if (decision is { Allowed: true } ||
                        decision is { MemberId: not null })
                        break;

                    await Task.Delay(IdentifyInterval, ct).ConfigureAwait(false);
                }

                while (decision is null || (decision.MemberId is null && !decision.Allowed))
                {
                    var remainingIdentify = MinRecognizingDisplay - (DateTime.UtcNow - recognizingStarted);
                    if (remainingIdentify <= TimeSpan.Zero || !_mode.RecognitionEnabled)
                        break;

                    await Task.Delay(IdentifyInterval, ct).ConfigureAwait(false);
                    var frame = _captureJpeg?.Invoke();
                    if (frame is not null && frame.Length >= 100)
                    {
                        var next = await _recognition.IdentifyAndDecideAsync(frame, ct).ConfigureAwait(false);
                        if (next is { MemberId: not null })
                        {
                            decision = next;
                            break;
                        }
                    }
                }

                var remaining = MinRecognizingDisplay - (DateTime.UtcNow - recognizingStarted);
                if (remaining > TimeSpan.Zero)
                    await Task.Delay(remaining, ct).ConfigureAwait(false);

                if (!_mode.RecognitionEnabled)
                {
                    _sessions.CompleteSession();
                    continue;
                }

                decision ??= _recognition.DecideUnknown();

                if (decision.ReasonCode == AccessDecisionService.ReasonMemberNotFound
                    || decision.MemberId is null && !decision.Allowed)
                {
                    _states.TransitionTo(
                        AccessUiState.Unknown,
                        statusMessage: "Não foi possível identificar seu rosto.");
                    await WaitResultOrUiAsync(TimeSpan.FromSeconds(22), ct).ConfigureAwait(false);
                    if (_states.State == AccessUiState.Unknown)
                    {
                        _sessions.CompleteSession();
                        _states.ResetAutomaticIdle();
                    }
                    continue;
                }

                if (!decision.Allowed)
                {
                    await _flow.ProcessDeniedOnlyAsync(decision, ct).ConfigureAwait(false);
                    _states.TransitionTo(
                        AccessUiState.Denied,
                        statusMessage: AccessDenialMessages.ForKiosk(decision),
                        memberDisplayName: decision.MemberName,
                        memberId: decision.MemberId);
                    await Task.Delay(_sessions.ResultDisplayDuration, ct).ConfigureAwait(false);
                    _sessions.CompleteSession();
                    _states.ResetAutomaticIdle();
                    continue;
                }

                var pendingMsg = decision.Kind == AccessDecisionKind.AllowTolerance
                    ? $"Olá, {decision.MemberName}!\n\n{decision.PublicMessage}"
                    : $"Olá, {decision.MemberName}!\n\nAguarde a passagem na catraca.";

                _states.TransitionTo(
                    AccessUiState.Recognizing,
                    statusMessage: pendingMsg,
                    memberDisplayName: decision.MemberName,
                    memberId: decision.MemberId);

                var pendingStarted = DateTime.UtcNow;
                var result = await _flow
                    .ProcessAuthorizedPassageAsync(decision, AccessFlowService.SourceFace, ct)
                    .ConfigureAwait(false);

                var pendingRemaining = MinPendingDisplay - (DateTime.UtcNow - pendingStarted);
                if (pendingRemaining > TimeSpan.Zero)
                    await Task.Delay(pendingRemaining, ct).ConfigureAwait(false);

                var confirmed = result.Passage == PassageOutcome.PassageDetected;
                if (confirmed)
                {
                    var successMsg = decision.Kind == AccessDecisionKind.AllowTolerance
                        ? $"Olá, {decision.MemberName}!\n\n{decision.PublicMessage}"
                        : $"Olá, {decision.MemberName}!\n\nEntrada registrada.\nTenha um ótimo treino!";
                    _states.TransitionTo(
                        AccessUiState.PassageConfirmed,
                        statusMessage: successMsg,
                        memberDisplayName: decision.MemberName,
                        memberId: decision.MemberId);
                    await Task.Delay(TimeSpan.FromSeconds(4), ct).ConfigureAwait(false);
                }
                else
                {
                    _states.TransitionTo(
                        AccessUiState.Denied,
                        statusMessage: result.UiMessage,
                        memberDisplayName: decision.MemberName,
                        memberId: decision.MemberId);
                    await Task.Delay(_sessions.ResultDisplayDuration, ct).ConfigureAwait(false);
                }

                _sessions.CompleteSession();
                _states.ResetAutomaticIdle();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "AutomaticAccessEngine loop error");
                _sessions.CompleteSession();
                try { _states.ResetAutomaticIdle(); } catch { /* ignore */ }
                await Task.Delay(500, CancellationToken.None).ConfigureAwait(false);
            }
        }
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
