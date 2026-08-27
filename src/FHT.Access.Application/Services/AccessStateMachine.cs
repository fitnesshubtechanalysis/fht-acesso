using FHT.Access.Domain.Enums;

namespace FHT.Access.Application.Services;

public sealed class AccessStateMachine
{
    private AccessUiState _state = AccessUiState.AutomaticIdle;
    private readonly object _sync = new();

    public AccessUiState State
    {
        get { lock (_sync) return _state; }
    }

    public string? StatusMessage { get; private set; }
    public string? MemberDisplayName { get; private set; }
    public Guid? MemberId { get; private set; }

    public event EventHandler<AccessUiState>? StateChanged;

    public void TransitionTo(
        AccessUiState next,
        string? statusMessage = null,
        string? memberDisplayName = null,
        Guid? memberId = null)
    {
        lock (_sync)
        {
            _state = next;
            StatusMessage = statusMessage;
            MemberDisplayName = memberDisplayName;
            MemberId = memberId;
        }

        StateChanged?.Invoke(this, next);
    }

    public void ResetAutomaticIdle()
    {
        TransitionTo(AccessUiState.AutomaticIdle, statusMessage: null, memberDisplayName: null, memberId: null);
    }
}
