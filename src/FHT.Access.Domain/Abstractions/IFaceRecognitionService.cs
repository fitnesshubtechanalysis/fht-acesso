namespace FHT.Access.Domain.Abstractions;

public sealed record FaceMatchResult(Guid MemberId, double Score);

public interface IFaceRecognitionService
{
    string ModelVersion { get; }

    Task EnrollAsync(Guid memberId, byte[] imageBgrOrJpeg, CancellationToken ct = default);
    Task<FaceMatchResult?> IdentifyAsync(byte[] imageBgrOrJpeg, CancellationToken ct = default);
    Task RemoveAsync(Guid memberId, CancellationToken ct = default);
}
