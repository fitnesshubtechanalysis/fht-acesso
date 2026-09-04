namespace FHT.Access.Domain.Abstractions;

/// <summary>
/// Rosto já cadastrado em outro aluno — evita A aparecer como B.
/// </summary>
public sealed class FaceEnrollmentConflictException : InvalidOperationException
{
    public Guid ConflictingMemberId { get; }
    public double Score { get; }

    public FaceEnrollmentConflictException(Guid conflictingMemberId, double score)
        : base(
            $"Este rosto já está cadastrado em outro aluno (score={score:F2}). " +
            "Remova a facial do outro cadastro ou escolha o aluno correto.")
    {
        ConflictingMemberId = conflictingMemberId;
        Score = score;
    }
}
