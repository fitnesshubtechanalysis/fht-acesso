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
    public AccessDirection? ActiveLane { get; private set; }

    public event EventHandler<AccessUiState>? StateChanged;
    public event EventHandler? ActiveLaneChanged;

    public void SetActiveLane(AccessDirection lane)
    {
        lock (_sync)
        {
            if (ActiveLane == lane)
                return;
            ActiveLane = lane;
        }

        ActiveLaneChanged?.Invoke(this, EventArgs.Empty);
    }

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
        lock (_sync)
        {
            ActiveLane = null;
        }

        ActiveLaneChanged?.Invoke(this, EventArgs.Empty);
        TransitionTo(AccessUiState.AutomaticIdle, statusMessage: null, memberDisplayName: null, memberId: null);
    }
}
