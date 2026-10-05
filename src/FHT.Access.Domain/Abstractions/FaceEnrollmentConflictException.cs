namespace FHT.Access.Domain.Abstractions;

/// <summary>
/// Rosto já cadastrado em outro aluno — evita A aparecer como B.
/// </summary>
public sealed class FaceEnrollmentConflictException : InvalidOperationException
{
    public Guid ConflictingMemberId { get; }
    public IReadOnlyList<Guid> ConflictingMemberIds { get; }
    public double Score { get; }

    public FaceEnrollmentConflictException(Guid conflictingMemberId, double score)
        : this([(conflictingMemberId, score)])
    {
    }

    public FaceEnrollmentConflictException(IReadOnlyList<(Guid MemberId, double Score)> conflicts)
        : base(conflicts.Count <= 1
            ? "Este rosto já está cadastrado em outro aluno. Remova a facial desse cadastro antes de continuar."
            : $"Este rosto parece com {conflicts.Count} cadastros. Remova a facial deles antes de continuar.")
    {
        if (conflicts.Count == 0)
            throw new ArgumentException("Informe ao menos um cadastro em conflito.", nameof(conflicts));

        ConflictingMemberIds = conflicts.Select(c => c.MemberId).Distinct().ToArray();
        ConflictingMemberId = ConflictingMemberIds[0];
        Score = conflicts[0].Score;
    }
}
