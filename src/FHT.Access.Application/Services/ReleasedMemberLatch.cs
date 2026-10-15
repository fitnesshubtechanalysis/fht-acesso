namespace FHT.Access.Application.Services;

/// <summary>
/// Depois da entrada, a mesma pessoa não libera de novo enquanto continua no quadro.
/// A próxima não espera a câmera ficar vazia: dois reconhecimentos de outro aluno
/// abrem a vez, e um tempo sem ver quem acabou de entrar também abre a tela.
/// </summary>
public sealed class ReleasedMemberLatch
{
    public static readonly TimeSpan OpenWhenReleasedNotSeen = TimeSpan.FromMilliseconds(800);

    private Guid? _otherId;
    private int _otherHits;
    private DateTime? _notSeenSinceUtc;

    public Guid? MemberId { get; private set; }

    public bool IsArmed => MemberId is not null;

    public void Arm(Guid memberId)
    {
        MemberId = memberId;
        ResetProbe();
    }

    public void Clear()
    {
        MemberId = null;
        ResetProbe();
    }

    public void NoteStillPresent() => ResetProbe();

    public LatchDecision Observe(Guid? identifiedMemberId, DateTime utcNow)
    {
        if (MemberId is null)
            return LatchDecision.Idle;

        if (identifiedMemberId == MemberId)
        {
            ResetProbe();
            return LatchDecision.Hold;
        }

        if (identifiedMemberId is { } other)
        {
            if (_otherId == other)
                _otherHits++;
            else
            {
                _otherId = other;
                _otherHits = 1;
            }

            _notSeenSinceUtc = null;
            return _otherHits >= 2 ? LatchDecision.RecognizeOther : LatchDecision.Hold;
        }

        _notSeenSinceUtc ??= utcNow;
        return utcNow - _notSeenSinceUtc.Value >= OpenWhenReleasedNotSeen
            ? LatchDecision.OpenRecognition
            : LatchDecision.Hold;
    }

    private void ResetProbe()
    {
        _otherId = null;
        _otherHits = 0;
        _notSeenSinceUtc = null;
    }
}

public enum LatchDecision
{
    Idle,
    Hold,
    RecognizeOther,
    OpenRecognition,
}
