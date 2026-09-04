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

    /// <summary>
    /// Outra lane não pode roubar a UI enquanto esta está em reconhecimento/resultado.
    /// Evita saída apagar o nome da entrada (e vice-versa).
    /// </summary>
    public bool CanLaneTakeUi(AccessDirection lane)
    {
        lock (_sync)
        {
            if (ActiveLane is null || ActiveLane == lane)
                return true;

            return _state is AccessUiState.AutomaticIdle;
        }
    }

    public void SetActiveLane(AccessDirection lane)
    {
        lock (_sync)
        {
            if (ActiveLane is { } owner
                && owner != lane
                && _state is not AccessUiState.AutomaticIdle)
            {
                return;
            }

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

    /// <summary>
    /// Só a lane ativa (ou idle) pode voltar para AutomaticIdle — evita a lane ociosa
    /// limpar a tela enquanto a outra ainda mostra resultado.
    /// </summary>
    public void ResetAutomaticIdle(AccessDirection? forLane = null)
    {
        lock (_sync)
        {
            if (forLane is { } lane
                && ActiveLane is { } owner
                && owner != lane
                && _state is not AccessUiState.AutomaticIdle)
            {
                return;
            }

            ActiveLane = null;
        }

        ActiveLaneChanged?.Invoke(this, EventArgs.Empty);
        TransitionTo(AccessUiState.AutomaticIdle, statusMessage: null, memberDisplayName: null, memberId: null);
    }
}
