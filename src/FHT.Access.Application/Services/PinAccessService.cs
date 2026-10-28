using FHT.Access.Domain.Abstractions;
using FHT.Access.Domain.Enums;

namespace FHT.Access.Application.Services;

/// <summary>
/// Código de 6 dígitos no teclado do totem ou da placa. A decisão é a mesma da facial.
/// </summary>
public sealed class PinAccessService
{
    private readonly AccessFlowService _flow;
    private readonly AccessDecisionService _decisions;
    private readonly AccessEventService _events;
    private readonly IMemberRepository _members;
    private readonly AccessStateMachine _states;
    private readonly TurnstileService _turnstile;
    private readonly PinKeypadGuard _guard = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
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
        var digits = Digits(text);
        var enter = text.Length > 0 && digits.Length == 0;
        if (DateTime.UtcNow - _bufferUtc > TimeSpan.FromSeconds(8))
            _buffer = string.Empty;

        if (digits.Length >= 6)
        {
            _buffer = string.Empty;
            _ = SubmitAsync(digits[^6..]);
            return;
        }

        if (enter)
        {
            var full = _buffer;
            _buffer = string.Empty;
            _ = SubmitAsync(full);
            return;
        }

        if (digits.Length == 0)
            return;

        _bufferUtc = DateTime.UtcNow;
        _buffer += digits;
        if (_buffer.Length > 6)
            _buffer = _buffer[^6..];
        if (_buffer.Length == 6)
        {
            var full = _buffer;
            _buffer = string.Empty;
            _ = SubmitAsync(full);
        }
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

            var code = Digits(raw);
            if (code.Length != 6)
            {
                _guard.RegisterFailure(now);
                await ShowTemporaryAsync(
                    AccessUiState.Denied,
                    LockedOr("Digite os 6 números e confirme."),
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

    private static string Digits(string? raw)
        => new((raw ?? string.Empty).Where(char.IsDigit).ToArray());
}
