using FHT.Access.Domain;
using FHT.Access.Domain.Abstractions;
using FHT.Access.Domain.Enums;

namespace FHT.Access.Application.Services;

/// <summary>
/// Código de 4 ou 6 dígitos no teclado do totem ou da placa. A decisão é a mesma da facial.
/// </summary>
public sealed class PinAccessService
{
    private readonly AccessFlowService _flow;
    private readonly AccessDecisionService _decisions;
    private readonly AccessEventService _events;
    private readonly IMemberRepository _members;
    private readonly AccessStateMachine _states;
    private readonly TurnstileService _turnstile;
    private static readonly TimeSpan ShortCodePause = TimeSpan.FromMilliseconds(1500);
    private readonly PinKeypadGuard _guard = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _bufferGate = new();
    private CancellationTokenSource? _shortCodeDelay;
    private string _buffer = string.Empty;
    private DateTime _bufferUtc = DateTime.MinValue;
    private bool _started;

    public PinAccessService(
        AccessFlowService flow,
        AccessDecisionService decisions,
        AccessEventService events,
        IMemberRepository members,
        AccessStateMachine states,
        TurnstileService turnstile)
    {
        _flow = flow;
        _decisions = decisions;
        _events = events;
        _members = members;
        _states = states;
        _turnstile = turnstile;
    }

    public void Start()
    {
        if (_started)
            return;
        _started = true;
        _turnstile.KeypadCodeReceived += OnHardwareCode;
    }

    public void AcceptHardwareCode(string? raw)
    {
        var text = (raw ?? string.Empty).Trim();
        var digits = KeypadBuffer.Digits(text);
        var enter = text.Length > 0 && digits.Length == 0;
        KeypadBuffer.Step step;
        lock (_bufferGate)
        {
            var expired = DateTime.UtcNow - _bufferUtc > TimeSpan.FromSeconds(8);
            step = enter
                ? KeypadBuffer.Confirm(_buffer)
                : KeypadBuffer.Apply(_buffer, digits, expired);
            _buffer = step.Buffer;
            if (digits.Length > 0)
                _bufferUtc = DateTime.UtcNow;
            CancelShortCodeDelay();
            if (step.WaitForMoreDigits)
                ArmShortCodeDelay(_buffer);
        }

        if (step.SubmitNow is not null)
            _ = SubmitAsync(step.SubmitNow);
    }

    public async Task SubmitAsync(string? raw, CancellationToken cancellationToken = default)
    {
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return;

        try
        {
            if (!CanInterrupt())
                return;

            var now = DateTime.UtcNow;
            if (_guard.IsLocked(now))
            {
                await ShowTemporaryAsync(
                    AccessUiState.Denied,
                    "Teclado bloqueado. Aguarde um minuto.",
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            var code = KeypadBuffer.Digits(raw);
            if (!AccessCodeRules.IsAccepted(code))
            {
                _guard.RegisterFailure(now);
                await ShowTemporaryAsync(
                    AccessUiState.Denied,
                    LockedOr("Digite os 4 ou 6 números e confirme."),
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            var member = await _members.GetByAccessCodeAsync(code, cancellationToken).ConfigureAwait(false);
            if (member is null)
            {
                _guard.RegisterFailure(DateTime.UtcNow);
                await _events.RecordDeniedAsync(
                    null,
                    AccessDirection.Entry,
                    AccessFlowService.SourcePin,
                    _flow.DeviceId,
                    "access_code_unknown",
                    cancellationToken).ConfigureAwait(false);
                await ShowTemporaryAsync(
                    AccessUiState.Denied,
                    LockedOr("Código não encontrado."),
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            _guard.RegisterSuccess();
            var decision = _decisions.Decide(member);
            if (!decision.AllowAutomaticRelease)
            {
                await _flow.ProcessDeniedOnlyAsync(
                    decision,
                    AccessDirection.Entry,
                    cancellationToken,
                    AccessFlowService.SourcePin).ConfigureAwait(false);
                await ShowTemporaryAsync(
                    AccessUiState.Denied,
                    AccessDenialMessages.ForKiosk(decision),
                    cancellationToken,
                    decision.MemberName,
                    decision.MemberId).ConfigureAwait(false);
                return;
            }

            _states.SetActiveLane(AccessDirection.Entry);
            _states.TransitionTo(
                AccessUiState.Recognizing,
                "Conferindo código",
                decision.MemberName,
                decision.MemberId);

            var result = await _flow.ProcessAuthorizedPassageAsync(
                decision,
                AccessFlowService.SourcePin,
                AccessDirection.Entry,
                onTurnstileReleased: _ =>
                {
                    _states.SetActiveLane(AccessDirection.Entry);
                    _states.TransitionTo(
                        AccessUiState.WaitingPassage,
                        string.IsNullOrWhiteSpace(decision.PublicMessage)
                            ? decision.MemberName
                            : decision.PublicMessage,
                        decision.MemberName,
                        decision.MemberId);
                    return Task.CompletedTask;
                },
                cancellationToken).ConfigureAwait(false);

            var confirmed = result.Passage == PassageOutcome.PassageDetected;
            _states.TransitionTo(
                confirmed ? AccessUiState.PassageConfirmed : AccessUiState.Denied,
                confirmed ? "Entrada registrada" : result.UiMessage,
                decision.MemberName,
                decision.MemberId);
            await Task.Delay(TimeSpan.FromSeconds(confirmed ? 2.5 : 3), cancellationToken).ConfigureAwait(false);
            _states.ResetAutomaticIdle(AccessDirection.Entry);
        }
        finally
        {
            _gate.Release();
        }
    }

    private string LockedOr(string message)
    {
        if (!_guard.IsLocked(DateTime.UtcNow))
            return message;
        return "Teclado bloqueado. Aguarde um minuto.";
    }

    private bool CanInterrupt()
    {
        return _states.State is AccessUiState.AutomaticIdle or AccessUiState.Unknown or AccessUiState.Denied;
    }

    private async Task ShowTemporaryAsync(
        AccessUiState state,
        string message,
        CancellationToken cancellationToken,
        string? name = null,
        Guid? memberId = null)
    {
        _states.SetActiveLane(AccessDirection.Entry);
        _states.TransitionTo(state, message, name, memberId);
        await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
        if (_states.State == state)
            _states.ResetAutomaticIdle(AccessDirection.Entry);
    }

    private void OnHardwareCode(object? sender, string code) => AcceptHardwareCode(code);

    private void ArmShortCodeDelay(string code)
    {
        var cts = new CancellationTokenSource();
        _shortCodeDelay = cts;
        _ = SubmitShortCodeAfterPauseAsync(code, cts.Token);
    }

    private void CancelShortCodeDelay()
    {
        if (_shortCodeDelay is null)
            return;
        _shortCodeDelay.Cancel();
        _shortCodeDelay.Dispose();
        _shortCodeDelay = null;
    }

    private async Task SubmitShortCodeAfterPauseAsync(string code, CancellationToken token)
    {
        try
        {
            await Task.Delay(ShortCodePause, token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            return;
        }

        var submit = false;
        lock (_bufferGate)
        {
            if (_buffer != code)
                return;
            _buffer = string.Empty;
            CancelShortCodeDelay();
            submit = true;
        }

        if (submit)
            await SubmitAsync(code).ConfigureAwait(false);
    }
}
