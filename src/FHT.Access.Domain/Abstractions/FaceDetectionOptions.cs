namespace FHT.Access.Domain.Abstractions;

/// <summary>Haar / downscale tuning for identify and enroll.</summary>
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
    /// Gatilho de aproximação: rosto na zona da catraca (mais leve que identify).
    /// </summary>
    public static FaceDetectionOptions ApproachPresence { get; } = new(
        DetectMaxWidth: 800,
        MinFaceSize: 32,
        ScaleFactor: 1.07,
        MinNeighbors: 2,
        MinFaceAreaFraction: 0.022,
        CenterXMargin: 0.14,
        CenterYMargin: 0.12);

    /// <summary>
    /// Entrada no totem: um pouco mais permissivo que Default (distância de catraca),
    /// sem inventar crop wide — só Haar.
    /// </summary>
    public static FaceDetectionOptions EntryIdentify { get; } = new(
        DetectMaxWidth: 960,
        MinFaceSize: 40,
        ScaleFactor: 1.07,
        MinNeighbors: 3,
        MinFaceAreaFraction: 0.035,
        CenterXMargin: 0.16,
        CenterYMargin: 0.12);

    /// <summary>
    /// Cadastro no balcão: mais permissivo (rosto próximo, ângulo/iluminação variáveis).
    /// </summary>
    public static FaceDetectionOptions Enrollment { get; } = new(
        DetectMaxWidth: 960,
        MinFaceSize: 28,
        ScaleFactor: 1.07,
        MinNeighbors: 2,
        MinFaceAreaFraction: 0.012,
        CenterXMargin: 0.08,
        CenterYMargin: 0.08);

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
