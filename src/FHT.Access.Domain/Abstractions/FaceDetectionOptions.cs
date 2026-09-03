namespace FHT.Access.Domain.Abstractions;

/// <summary>Haar / downscale tuning for identify (enroll always uses defaults).</summary>
public sealed record FaceDetectionOptions(
    int DetectMaxWidth = 640,
    int MinFaceSize = 48,
    double ScaleFactor = 1.08,
    int MinNeighbors = 3,
    /// <summary>Rosto mínimo como fração da área do frame (ignora pessoas longe).</summary>
    double MinFaceAreaFraction = 0.045,
    /// <summary>Centro do rosto deve ficar na faixa horizontal central (0–0.5).</summary>
    double CenterXMargin = 0.20,
    /// <summary>Centro do rosto deve ficar na faixa vertical central (0–0.5).</summary>
    double CenterYMargin = 0.14)
{
    public static FaceDetectionOptions Default { get; } = new();

    /// <summary>
    /// Saída: exige rosto bem próximo/central — ignora musculação/esteira ao fundo.
    /// </summary>
    public static FaceDetectionOptions ExitDistance { get; } = new(
        DetectMaxWidth: 960,
        MinFaceSize: 72,
        ScaleFactor: 1.06,
        MinNeighbors: 4,
        MinFaceAreaFraction: 0.08,
        CenterXMargin: 0.28,
        CenterYMargin: 0.18);
}
