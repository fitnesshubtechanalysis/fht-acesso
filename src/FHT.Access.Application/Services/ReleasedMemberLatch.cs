namespace FHT.Access.Application.Services;

/// <summary>
/// Uma mesma pessoa parada na câmera não pode pulsar a catraca sem parar.
/// A primeira liberação ainda permite mais uma, para quem libera outra pessoa
/// e entra em seguida. A terceira só acontece se o rosto sair e voltar,
/// ou se digitar o código. A próxima pessoa não espera a câmera ficar vazia.
/// </summary>
public sealed class ReleasedMemberLatch
{
    public static readonly TimeSpan OpenWhenReleasedNotSeen = TimeSpan.FromMilliseconds(800);

    private Guid? _otherId;
    private int _otherHits;
    private int _releasesWhilePresent;
    private DateTime? _notSeenSinceUtc;

    public Guid? MemberId { get; private set; }

    public bool IsArmed => MemberId is not null;

    /// <summary>Código digitado durante a passagem. A facial não abre a vez extra junto.</summary>
    public bool HasParkedCode { get; private set; }

    /// <summary>Ainda cabe uma entrada da mesma pessoa sem ela sair do quadro.</summary>
    public bool SamePersonMayEnterAgain => _releasesWhilePresent < 2 && !HasParkedCode;

    public void Arm(Guid memberId)
    {
        if (MemberId != memberId)
            _releasesWhilePresent = 0;
        MemberId = memberId;
        _releasesWhilePresent++;
        ResetProbe();
    }

    /// <summary>O código é a entrada seguinte. A câmera não libera essa pessoa de novo enquanto ela continuar no quadro.</summary>
    public void NoteCodeUsed(Guid memberId)
    {
        Arm(memberId);
        _releasesWhilePresent = Math.Max(_releasesWhilePresent, 2);
        HasParkedCode = false;
    }

    public void ParkCode() => HasParkedCode = true;

    public void ClearParkedCode() => HasParkedCode = false;

    public void Clear()
    {
        MemberId = null;
        _releasesWhilePresent = 0;
        HasParkedCode = false;
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
            return SamePersonMayEnterAgain ? LatchDecision.RecognizeSame : LatchDecision.Hold;
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
    RecognizeSame,
    RecognizeOther,
    OpenRecognition,
}
