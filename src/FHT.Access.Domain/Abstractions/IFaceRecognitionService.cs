namespace FHT.Access.Domain.Abstractions;

public sealed record FaceMatchResult(Guid MemberId, double Score);

public interface IFaceRecognitionService
{
    string ModelVersion { get; }

    /// <summary>Por que o último identify não devolveu um nome. Null no stub.</summary>
    string? LastIdentifyNote => null;

    Task EnrollAsync(Guid memberId, byte[] imageBgrOrJpeg, CancellationToken ct = default);
    Task<FaceMatchResult?> IdentifyAsync(
        byte[] imageBgrOrJpeg,
        CancellationToken ct = default,
        FaceDetectionOptions? detection = null);
    Task RemoveAsync(Guid memberId, CancellationToken ct = default);
}
