using System.Buffers.Binary;
using System.Runtime.InteropServices;
using FHT.Access.Domain.Abstractions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace FHT.Access.Face;

/// <summary>
/// Local face engine: Haar recorta o rosto e o ArcFace gera o vetor de 512 números.
/// Cadastros do SFace e do histograma não são lidos.
/// </summary>
public sealed class LocalHistogramFaceService : IFaceRecognitionService, IDisposable
{
    public const string HistModelVersion = "hist-v1";
    public const string SpatialModelVersion = "hist-v2";
    public const string SfaceModelVersion = "sface-v1";
    public const string ArcFaceModelVersion = "arcface-v1";

    private const int HistBins = 256;
    private const int Grid = 8;
    private const int CellBins = 16;
    private const int SpatialLen = Grid * Grid * CellBins;
    private const int ArcFaceLen = 512;
    // Cosseno do ArcFace. 0,45 ainda aceitava o mesmo aluno de boné.
    private const float ArcFaceDefaultThreshold = 0.72f;
    private const float ArcFaceMaxThreshold = 0.85f;
    /// <summary>Folga mínima contra o 2º nome. Sem isso o totem cumprimenta outra pessoa.</summary>
    private const float MinScoreMargin = 0.14f;
    private const int DetectMaxWidth = 640;

    private static readonly byte[] ArcFaceMagic = "AF01"u8.ToArray();
    private static readonly byte[] SfaceMagic = "SF01"u8.ToArray();
    private static readonly byte[] SpatialMagic = "H2\0\0"u8.ToArray();

    private readonly Dictionary<Guid, StoredFace> _templates = new();
    private readonly object _sync = new();
    private readonly object _cvLock = new();
    private readonly double _threshold;
    private readonly Func<Guid, byte[], CancellationToken, Task>? _persistAsync;
    private readonly string? _modelDirectory;
    private static readonly bool OpenCvAvailable = DetectOpenCv();

    private CascadeClassifier? _frontal;
    private CascadeClassifier? _profile;
    private CascadeClassifier? _eye;
    private CascadeClassifier? _eyeGlasses;
    private InferenceSession? _arcFace;
    private string? _arcFaceInput;
    private bool _useArcFace;
    private bool _engineReady;
    private bool _disposed;

    public LocalHistogramFaceService(
        double threshold = 0.92,
        Func<Guid, byte[], CancellationToken, Task>? persistAsync = null,
        string? modelDirectory = null)
    {
        if (threshold is <= 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(threshold), "Threshold must be in (0, 1].");

        _threshold = threshold;
        _persistAsync = persistAsync;
        _modelDirectory = modelDirectory;
        EnsureEngine();
    }

    public string ModelVersion => ArcFaceModelVersion;

    public string? LastIdentifyNote { get; private set; }

    public static bool CanHydrate(string? modelVersion)
        => string.Equals(modelVersion, ArcFaceModelVersion, StringComparison.Ordinal);

    /// <summary>
    /// Rápido: há rosto na zona da catraca para abrir o totem?
    /// Só Haar estrito — sem cache e sem detecção solta, para a câmera vazia
    /// não continuar valendo como o último aluno.
    /// </summary>
    public bool HasNearbyFace(byte[]? jpeg, FaceDetectionOptions? detection = null)
    {
        if (jpeg is null || jpeg.Length < 100)
            return false;

        EnsureEngine();
        var detect = detection ?? FaceDetectionOptions.ApproachPresence;
        try
        {
            if (!OpenCvAvailable || _frontal is null || _frontal.Empty())
            {
                // Sem Haar: não bloqueia o totem — deixa o movimento decidir.
                return true;
            }

            using var src = Cv2.ImDecode(jpeg, ImreadModes.Color);
            if (src.Empty())
                return false;

            using var work = Downscale(src, detect.DetectMaxWidth);
            using var enhanced = EnhanceLighting(work);
            return DetectForMatch(enhanced, detect, enroll: false) is not null;
        }
        catch
        {
            return false;
        }
    }

    public async Task EnrollAsync(Guid memberId, byte[] imageBgrOrJpeg, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(imageBgrOrJpeg);
        ct.ThrowIfCancellationRequested();
        EnsureEngine();
        if (!_useArcFace)
            throw new InvalidOperationException("Modelo ArcFace não encontrado. A captura não foi gravada.");

        var enrollDetect = FaceDetectionOptions.Enrollment;

        // Um único build com regras de cadastro (Haar permissivo + fallback central).
        var stored = BuildStoredFace(imageBgrOrJpeg, enroll: true, enrollDetect);
        if (stored.Embeddings.Count == 0)
            throw new InvalidOperationException("Nenhum rosto detectado. Olhe para a câmera e tente de novo.");

        var conflicts = FindEnrollmentConflicts(stored, memberId);
        if (conflicts.Count > 0)
            throw new FaceEnrollmentConflictException(conflicts);

        var blob = SerializeStored(stored);

        lock (_sync)
        {
            _templates[memberId] = stored;
        }

        if (_persistAsync is not null)
            await _persistAsync(memberId, blob, ct).ConfigureAwait(false);
    }

    public Task<FaceMatchResult?> IdentifyAsync(
        byte[] imageBgrOrJpeg,
        CancellationToken ct = default,
        FaceDetectionOptions? detection = null)
    {
        ArgumentNullException.ThrowIfNull(imageBgrOrJpeg);
        ct.ThrowIfCancellationRequested();
        EnsureEngine();

        if (!_useArcFace)
        {
            LastIdentifyNote = "Modelo ArcFace não encontrado";
            return Task.FromResult<FaceMatchResult?>(null);
        }

        var probe = BuildStoredFace(imageBgrOrJpeg, enroll: false, detection);
        if (probe.Embeddings.Count == 0)
        {
            LastIdentifyNote = "nenhum rosto no quadro";
            return Task.FromResult<FaceMatchResult?>(null);
        }

        var match = FindBestMatch(probe, excludeMemberId: null);
        return Task.FromResult(match);
    }

    /// <summary>
    /// Melhor match acima do cutoff + margem. <paramref name="excludeMemberId"/> ignora
    /// o próprio aluno (re-cadastro) ou null no identify normal.
    /// </summary>
    private FaceMatchResult? FindBestMatch(StoredFace probe, Guid? excludeMemberId)
    {
        Guid? bestId = null;
        var bestScore = 0.0;
        var secondBest = 0.0;
        var bestArcFace = false;

        lock (_sync)
        {
            foreach (var (memberId, template) in _templates)
            {
                if (excludeMemberId is { } ex && memberId == ex)
                    continue;

                var score = Score(probe, template, out var usedArcFace);
                if (score > bestScore)
                {
                    secondBest = bestScore;
                    bestScore = score;
                    bestId = memberId;
                    bestArcFace = usedArcFace;
                }
                else if (score > secondBest)
                {
                    secondBest = score;
                }
            }
        }

        var floor = ArcFaceDefaultThreshold;
        var configured = _threshold is >= ArcFaceDefaultThreshold and <= ArcFaceMaxThreshold ? _threshold : floor;
        var cutoff = Math.Max(configured, floor);

        if (bestId is null || bestScore < cutoff)
        {
            LastIdentifyNote = $"abaixo do corte score={bestScore:F3} segundo={secondBest:F3} corte={cutoff:F2}";
            return null;
        }

        if (!bestArcFace)
        {
            LastIdentifyNote = "ArcFace não confirmou o rosto";
            return null;
        }

        var margin = bestScore - secondBest;
        // Cadastro ainda acusa foto duplicada. Na catraca, margem curta não vira nome.
        if (excludeMemberId is null && margin < MinScoreMargin)
        {
            LastIdentifyNote =
                $"dúvida score={bestScore:F3} segundo={secondBest:F3} margem={margin:F3}";
            return null;
        }

        LastIdentifyNote = $"ok score={bestScore:F3}";
        return new FaceMatchResult(bestId.Value, bestScore);
    }

    /// <summary>
    /// Todo cadastro acima do corte, não só o primeiro nome. Dois enquadramentos
    /// não podem apontar pessoas diferentes sem a recepção ver a lista.
    /// </summary>
    private List<(Guid MemberId, double Score)> FindEnrollmentConflicts(StoredFace probe, Guid excludeMemberId)
    {
        var ranked = new List<(Guid MemberId, double Score, bool UsedArcFace)>();
        lock (_sync)
        {
            foreach (var (memberId, template) in _templates)
            {
                if (memberId == excludeMemberId)
                    continue;

                var score = Score(probe, template, out var usedArcFace);
                ranked.Add((memberId, score, usedArcFace));
            }
        }

        ranked.Sort((a, b) => b.Score.CompareTo(a.Score));
        var conflicts = new List<(Guid MemberId, double Score)>();
        foreach (var hit in ranked)
        {
            var floor = ArcFaceDefaultThreshold;
            var configured = _threshold is >= ArcFaceDefaultThreshold and <= ArcFaceMaxThreshold ? _threshold : floor;
            var cutoff = Math.Max(configured, floor);
            if (!hit.UsedArcFace)
                continue;
            if (hit.Score < cutoff)
                continue;
            conflicts.Add((hit.MemberId, hit.Score));
        }

        return conflicts;
    }

    public Task RemoveAsync(Guid memberId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_sync)
        {
            _templates.Remove(memberId);
        }

        return Task.CompletedTask;
    }

    public void LoadTemplate(Guid memberId, byte[] blob)
    {
        ArgumentNullException.ThrowIfNull(blob);
        var stored = DeserializeStored(blob);
        lock (_sync)
        {
            _templates[memberId] = stored;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        lock (_cvLock)
        {
            _frontal?.Dispose();
            _profile?.Dispose();
            _eye?.Dispose();
            _eyeGlasses?.Dispose();
            _arcFace?.Dispose();
            _frontal = null;
            _profile = null;
            _eye = null;
            _eyeGlasses = null;
            _arcFace = null;
            _arcFaceInput = null;
        }
    }

    public static byte[] SerializeHistogram(double[] hist)
    {
        ArgumentNullException.ThrowIfNull(hist);
        if (hist.Length != HistBins)
            throw new ArgumentException("Histogram must have 256 bins.", nameof(hist));

        var bytes = new byte[HistBins * sizeof(double)];
        for (var i = 0; i < HistBins; i++)
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(i * sizeof(double)), hist[i]);
        return bytes;
    }

    public static double[] DeserializeHistogram(byte[] blob)
    {
        ArgumentNullException.ThrowIfNull(blob);
        if (blob.Length != HistBins * sizeof(double))
            throw new ArgumentException("Invalid histogram blob length.", nameof(blob));

        var hist = new double[HistBins];
        for (var i = 0; i < HistBins; i++)
            hist[i] = BinaryPrimitives.ReadDoubleLittleEndian(blob.AsSpan(i * sizeof(double)));
        return hist;
    }

    private StoredFace BuildStoredFace(
        byte[] imageBgrOrJpeg,
        bool enroll,
        FaceDetectionOptions? detection = null)
    {
        if (OpenCvAvailable)
        {
            try
            {
                return BuildWithOpenCv(imageBgrOrJpeg, enroll, detection);
            }
            catch (InvalidOperationException) when (enroll)
            {
                throw;
            }
            catch
            {
                if (!enroll)
                    return new StoredFace();
                throw new InvalidOperationException("Não foi possível ler o rosto. Olhe para a câmera e tente de novo.");
            }
        }

        return new StoredFace();
    }

    private StoredFace BuildWithOpenCv(
        byte[] imageBgrOrJpeg,
        bool enroll,
        FaceDetectionOptions? detection)
    {
        var detect = detection ?? FaceDetectionOptions.Default;
        using var src = Cv2.ImDecode(imageBgrOrJpeg, ImreadModes.Color);
        if (src.Empty())
            return new StoredFace();

        var stored = new StoredFace();
        var variants = BuildVariants(src, enroll);
        var detected = false;
        try
        {
            foreach (var variant in variants)
            {
                using var work = Downscale(variant, detect.DetectMaxWidth);
                using var enhanced = EnhanceLighting(work);
                var face = DetectForMatch(enhanced, detect, enroll);
                if (face is not { } rect)
                    continue;

                detected = true;
                using var region = PaddedSquare(enhanced, rect);
                if (_useArcFace)
                {
                    foreach (var emb in EmbedCrop(region))
                        stored.Embeddings.Add(emb);
                }

                if (!enroll && stored.Embeddings.Count > 0)
                    break;
            }
        }
        finally
        {
            foreach (var v in variants)
            {
                if (!ReferenceEquals(v, src))
                    v.Dispose();
            }
        }

        if (enroll && !detected)
            throw new InvalidOperationException("Nenhum rosto detectado. Olhe para a câmera e tente de novo.");

        // Identify sem face: não preencher histograma do frame inteiro (falso positivo de longe).
        if (!enroll && !detected)
            return stored;

        return stored;
    }

    /// <summary>
    /// Só o quadro da câmera, já na orientação do preview.
    /// Girar 90/180 a cada captura e a cada reconhecimento deixava o totem lento
    /// e às vezes casava um recorte errado em vez do rosto cadastrado.
    /// </summary>
    private static List<Mat> BuildVariants(Mat src, bool enroll)
    {
        _ = enroll;
        return [src];
    }

    private List<float[]> EmbedCrop(Mat bgrCrop)
    {
        var found = new List<float[]>();
        lock (_cvLock)
        {
            if (_arcFace is null)
                return found;

            using var aligned = new Mat();
            if (bgrCrop.Width == 112 && bgrCrop.Height == 112)
                bgrCrop.CopyTo(aligned);
            else
                Cv2.Resize(bgrCrop, aligned, new Size(112, 112), 0, 0, InterpolationFlags.Area);
            found.Add(ToEmbedding(aligned));
        }

        return found;
    }

    /// <summary>
    /// Reconhecimento só aceita o Haar frontal com olhos no recorte.
    /// Boné, ombro e vulto no fundo não viram nome.
    /// </summary>
    private Rect? DetectForMatch(Mat bgr, FaceDetectionOptions detect, bool enroll)
    {
        var tuned = enroll
            ? detect
            : detect with { MinNeighbors = Math.Max(detect.MinNeighbors, 3) };
        var strict = DetectLargestFace(bgr, tuned, allowProfile: false);
        if (strict is { } strictFace && HasVisibleEyes(bgr, strictFace))
            return strictFace;

        if (!enroll)
            return null;

        var loose = DetectLargestFaceLoose(bgr, detect);
        if (loose is not { } face)
            return null;

        var frameArea = Math.Max(1, bgr.Width * bgr.Height);
        var areaFrac = face.Width * face.Height / (double)frameArea;
        var minFrac = enroll
            ? Math.Min(detect.MinFaceAreaFraction, 0.012)
            : detect.MinFaceAreaFraction;
        if (areaFrac < minFrac)
            return null;

        var cx = face.X + face.Width / 2.0;
        var cy = face.Y + face.Height / 2.0;
        if (cx < bgr.Width * 0.06 || cx > bgr.Width * 0.94)
            return null;
        if (cy < bgr.Height * 0.04 || cy > bgr.Height * 0.96)
            return null;

        return HasVisibleEyes(bgr, face) ? face : null;
    }

    /// <summary>
    /// Olho dentro do recorte. O Haar inclui a testa e o boné, então o olho
    /// não fica só no topo. Um botão de boné, sem pele embaixo, não conta.
    /// </summary>
    private bool HasVisibleEyes(Mat bgr, Rect face)
    {
        if (_eye is null && _eyeGlasses is null)
            return false;

        var band = ClampRect(bgr, new Rect(
            face.X,
            face.Y + (int)(face.Height * 0.08),
            face.Width,
            Math.Max(8, (int)(face.Height * 0.74))));
        if (band.Width < 24 || band.Height < 16)
            return false;

        using var roi = new Mat(bgr, band);
        using var gray = new Mat();
        Cv2.CvtColor(roi, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.EqualizeHist(gray, gray);

        var minEye = new Size(Math.Max(6, band.Width / 12), Math.Max(6, band.Height / 12));
        var maxEyeW = band.Width * 0.45;
        var count = 0;
        if (_eye is not null)
            count = Math.Max(count, CountReasonableEyes(_eye, gray, minEye, 2, maxEyeW));
        if (_eyeGlasses is not null)
            count = Math.Max(count, CountReasonableEyes(_eyeGlasses, gray, minEye, 2, maxEyeW));
        if (count >= 2)
            return true;
        if (count == 0)
            return false;

        var lower = ClampRect(bgr, new Rect(
            face.X,
            face.Y + face.Height / 2,
            face.Width,
            Math.Max(1, face.Height - face.Height / 2)));
        return SkinFraction(bgr, lower) >= 0.12;
    }

    private static double SkinFraction(Mat bgr, Rect roi)
    {
        if (roi.Width < 8 || roi.Height < 8)
            return 0;

        using var crop = new Mat(bgr, roi);
        using var ycrcb = new Mat();
        Cv2.CvtColor(crop, ycrcb, ColorConversionCodes.BGR2YCrCb);
        using var mask = new Mat();
        Cv2.InRange(ycrcb, new Scalar(0, 125, 70), new Scalar(255, 185, 145), mask);
        return Cv2.CountNonZero(mask) / (double)(roi.Width * roi.Height);
    }

    private static int CountReasonableEyes(
        CascadeClassifier cascade,
        Mat gray,
        Size minEye,
        int minNeighbors,
        double maxEyeWidth)
    {
        var hits = cascade.DetectMultiScale(
            gray,
            1.1,
            minNeighbors,
            HaarDetectionTypes.ScaleImage,
            minEye);
        var count = 0;
        foreach (var hit in hits)
        {
            if (hit.Width <= maxEyeWidth)
                count++;
        }

        return count;
    }

    private static Rect ClampRect(Mat src, Rect rect)
    {
        var x = Math.Clamp(rect.X, 0, Math.Max(0, src.Width - 1));
        var y = Math.Clamp(rect.Y, 0, Math.Max(0, src.Height - 1));
        var w = Math.Clamp(rect.Width, 0, src.Width - x);
        var h = Math.Clamp(rect.Height, 0, src.Height - y);
        return new Rect(x, y, w, h);
    }

    private Rect? DetectLargestFace(Mat bgr, FaceDetectionOptions detect, bool allowProfile = true)
    {
        using var gray = new Mat();
        Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.EqualizeHist(gray, gray);

        var minSize = new Size(Math.Max(12, detect.MinFaceSize), Math.Max(12, detect.MinFaceSize));
        Rect[] hits = [];
        if (_frontal is not null)
            hits = _frontal.DetectMultiScale(
                gray,
                detect.ScaleFactor,
                detect.MinNeighbors,
                HaarDetectionTypes.ScaleImage,
                minSize);
        if (hits.Length == 0 && allowProfile && _profile is not null)
            hits = _profile.DetectMultiScale(
                gray,
                detect.ScaleFactor,
                detect.MinNeighbors,
                HaarDetectionTypes.ScaleImage,
                minSize);
        if (hits.Length == 0)
            return null;

        var frameArea = Math.Max(1, bgr.Width * bgr.Height);
        var minArea = Math.Clamp(detect.MinFaceAreaFraction, 0, 0.5) * frameArea;
        var mx = Math.Clamp(detect.CenterXMargin, 0, 0.45);
        var my = Math.Clamp(detect.CenterYMargin, 0, 0.45);
        var x0 = bgr.Width * mx;
        var x1 = bgr.Width * (1.0 - mx);
        var y0 = bgr.Height * my;
        var y1 = bgr.Height * (1.0 - my);

        var candidates = hits
            .Where(r => r.Width * r.Height >= minArea)
            .Where(r =>
            {
                var cx = r.X + r.Width / 2.0;
                var cy = r.Y + r.Height / 2.0;
                return cx >= x0 && cx <= x1 && cy >= y0 && cy <= y1;
            })
            .OrderByDescending(r => r.Width * r.Height)
            .ToList();

        return candidates.Count > 0 ? candidates[0] : null;
    }

    /// <summary>
    /// Segunda passagem no cadastro: mesma Haar, sem filtro de centro/área mínima.
    /// </summary>
    private Rect? DetectLargestFaceLoose(Mat bgr, FaceDetectionOptions detect)
    {
        using var gray = new Mat();
        Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.EqualizeHist(gray, gray);

        var minSize = new Size(
            Math.Max(16, detect.MinFaceSize / 2),
            Math.Max(16, detect.MinFaceSize / 2));
        Rect[] hits = [];
        if (_frontal is not null)
            hits = _frontal.DetectMultiScale(
                gray,
                Math.Max(1.05, detect.ScaleFactor - 0.02),
                Math.Max(1, detect.MinNeighbors - 1),
                HaarDetectionTypes.ScaleImage,
                minSize);
        if (hits.Length == 0 && _profile is not null)
            hits = _profile.DetectMultiScale(
                gray,
                Math.Max(1.05, detect.ScaleFactor - 0.02),
                Math.Max(1, detect.MinNeighbors - 1),
                HaarDetectionTypes.ScaleImage,
                minSize);
        if (hits.Length == 0)
            return null;

        return hits.OrderByDescending(r => r.Width * r.Height).First();
    }

    private static Mat PaddedSquare(Mat bgr, Rect face)
    {
        var pad = (int)(Math.Max(face.Width, face.Height) * 0.22);
        var side = Math.Max(face.Width, face.Height) + pad * 2;
        var cx = face.X + face.Width / 2;
        var cy = face.Y + face.Height / 2;
        var x = Math.Clamp(cx - side / 2, 0, Math.Max(0, bgr.Width - 1));
        var y = Math.Clamp(cy - side / 2, 0, Math.Max(0, bgr.Height - 1));
        var w = Math.Min(side, bgr.Width - x);
        var h = Math.Min(side, bgr.Height - y);
        var box = new Rect(x, y, Math.Max(8, w), Math.Max(8, h));
        return new Mat(bgr, box).Clone();
    }

    private float[] ToEmbedding(Mat bgr112)
    {
        var input = new float[3 * 112 * 112];
        for (var y = 0; y < bgr112.Rows; y++)
        {
            for (var x = 0; x < bgr112.Cols; x++)
            {
                var px = bgr112.At<Vec3b>(y, x);
                var i = y * 112 + x;
                input[i] = (px.Item2 - 127.5f) / 128f;
                input[112 * 112 + i] = (px.Item1 - 127.5f) / 128f;
                input[2 * 112 * 112 + i] = (px.Item0 - 127.5f) / 128f;
            }
        }

        var tensor = new DenseTensor<float>(input, [1, 3, 112, 112]);
        var feeds = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(_arcFaceInput!, tensor)
        };
        using var results = _arcFace!.Run(feeds);
        var output = results[0].AsEnumerable<float>().Take(ArcFaceLen).ToArray();
        if (output.Length < ArcFaceLen)
            throw new InvalidOperationException("O ArcFace não devolveu o vetor de 512 números.");
        return output;
    }

    private static Mat Downscale(Mat bgr, int detectMaxWidth)
    {
        var maxWidth = detectMaxWidth > 0 ? detectMaxWidth : DetectMaxWidth;
        if (bgr.Width <= maxWidth)
            return bgr.Clone();

        var scale = maxWidth / (double)bgr.Width;
        var size = new Size(maxWidth, Math.Max(1, (int)Math.Round(bgr.Height * scale)));
        var dst = new Mat();
        Cv2.Resize(bgr, dst, size, 0, 0, InterpolationFlags.Area);
        return dst;
    }

    private static Mat EnhanceLighting(Mat bgr)
    {
        using var lab = new Mat();
        Cv2.CvtColor(bgr, lab, ColorConversionCodes.BGR2Lab);
        Cv2.Split(lab, out var planes);
        try
        {
            using var clahe = Cv2.CreateCLAHE(3.5, new Size(8, 8));
            clahe.Apply(planes[0], planes[0]);
            var mean = Cv2.Mean(planes[0]).Val0;
            const double target = 148.0;
            var delta = target - mean;
            if (Math.Abs(delta) > 6)
            {
                var lifted = new Mat();
                planes[0].ConvertTo(lifted, MatType.CV_8UC1, 1.0, delta * 0.5);
                planes[0].Dispose();
                planes[0] = lifted;
            }
            using var merged = new Mat();
            Cv2.Merge(planes, merged);
            var enhanced = new Mat();
            Cv2.CvtColor(merged, enhanced, ColorConversionCodes.Lab2BGR);
            return enhanced;
        }
        finally
        {
            foreach (var p in planes)
                p.Dispose();
        }
    }

    private static double[] BuildSpatial(Mat bgr)
    {
        using var gray = ToGrayEqualized(bgr);
        using var small = new Mat();
        Cv2.Resize(gray, small, new Size(Grid * 12, Grid * 12), 0, 0, InterpolationFlags.Area);

        var hist = new double[SpatialLen];
        var cell = small.Width / Grid;
        var idx = 0;
        for (var gy = 0; gy < Grid; gy++)
        {
            for (var gx = 0; gx < Grid; gx++)
            {
                var rect = new Rect(gx * cell, gy * cell, cell, cell);
                using var patch = new Mat(small, rect);
                for (var y = 0; y < patch.Rows; y++)
                {
                    for (var x = 0; x < patch.Cols; x++)
                    {
                        var v = patch.At<byte>(y, x);
                        hist[idx + (v * CellBins / 256)]++;
                    }
                }

                idx += CellBins;
            }
        }

        Normalize(hist);
        return hist;
    }

    private static double[] BuildIntensityHist(Mat bgr)
    {
        using var gray = ToGrayEqualized(bgr);
        var histMat = new Mat();
        try
        {
            Cv2.CalcHist(
                images: new[] { gray },
                channels: new[] { 0 },
                mask: null!,
                hist: histMat,
                dims: 1,
                histSize: new[] { HistBins },
                ranges: new[] { new Rangef(0, 256) });

            var hist = new double[HistBins];
            for (var i = 0; i < HistBins; i++)
                hist[i] = histMat.At<float>(i);
            Normalize(hist);
            return hist;
        }
        finally
        {
            histMat.Dispose();
        }
    }

    private static Mat ToGrayEqualized(Mat bgr)
    {
        var gray = new Mat();
        if (bgr.Channels() == 1)
            bgr.CopyTo(gray);
        else
            Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);

        Cv2.EqualizeHist(gray, gray);
        return gray;
    }

    /// <summary>Center square (kiosk person in frame), slightly biased to the upper half.</summary>
    private static Mat FaceishCropBgr(Mat bgr)
    {
        var rect = FaceishRect(bgr);
        return new Mat(bgr, rect).Clone();
    }

    private static Rect FaceishRect(Mat src)
    {
        var side = (int)(Math.Min(src.Width, src.Height) * 0.72);
        side = Math.Max(32, side);
        var x = Math.Max(0, (src.Width - side) / 2);
        var y = Math.Max(0, (src.Height - side) / 3);
        if (x + side > src.Width)
            x = src.Width - side;
        if (y + side > src.Height)
            y = src.Height - side;
        return new Rect(x, y, side, side);
    }

    private static Mat FaceishCrop(Mat gray) => new Mat(gray, FaceishRect(gray)).Clone();

    private static double Score(StoredFace probe, StoredFace template, out bool usedArcFace)
    {
        usedArcFace = false;
        var best = 0.0;

        if (probe.Embeddings.Count > 0 && template.Embeddings.Count > 0)
        {
            usedArcFace = true;
            return Cosine(probe.Embeddings[0], template.Embeddings[0]);
        }

        foreach (var a in probe.Spatial)
        {
            foreach (var b in template.Spatial)
                best = Math.Max(best, Cosine(a, b));
        }

        if (best > 0)
            return best;

        foreach (var a in probe.Hists256)
        {
            foreach (var b in template.Hists256)
                best = Math.Max(best, Cosine(a, b));
        }

        return best;
    }

    private static double Cosine(float[] a, float[] b)
    {
        var n = Math.Min(a.Length, b.Length);
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < n; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * (double)a[i];
            nb += b[i] * (double)b[i];
        }

        if (na <= 0 || nb <= 0)
            return 0;
        return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }

    private static double Cosine(double[] a, double[] b)
    {
        var n = Math.Min(a.Length, b.Length);
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < n; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }

        if (na <= 0 || nb <= 0)
            return 0;
        return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }

    private static byte[] SerializeStored(StoredFace stored)
    {
        if (stored.Embeddings.Count > 0)
        {
            var count = stored.Embeddings.Count;
            var bytes = new byte[4 + 4 + (count * ArcFaceLen * sizeof(float))];
            ArcFaceMagic.CopyTo(bytes, 0);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), count);
            var offset = 8;
            foreach (var emb in stored.Embeddings)
            {
                for (var i = 0; i < ArcFaceLen; i++)
                {
                    BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset), emb[i]);
                    offset += sizeof(float);
                }
            }

            return bytes;
        }

        if (stored.Spatial.Count > 0)
        {
            var count = stored.Spatial.Count;
            var bytes = new byte[4 + 4 + (count * SpatialLen * sizeof(double))];
            SpatialMagic.CopyTo(bytes, 0);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), count);
            var offset = 8;
            foreach (var hist in stored.Spatial)
            {
                for (var i = 0; i < SpatialLen; i++)
                {
                    BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(offset), hist[i]);
                    offset += sizeof(double);
                }
            }

            return bytes;
        }

        return SerializeHistogram(stored.Hists256[0]);
    }

    private static StoredFace DeserializeStored(byte[] blob)
    {
        if (blob.Length >= 8 && blob.AsSpan(0, 4).SequenceEqual(SfaceMagic))
            return new StoredFace();

        if (blob.Length >= 8 && blob.AsSpan(0, 4).SequenceEqual(ArcFaceMagic))
        {
            var count = BinaryPrimitives.ReadInt32LittleEndian(blob.AsSpan(4));
            var stored = new StoredFace();
            var offset = 8;
            for (var n = 0; n < count; n++)
            {
                var emb = new float[ArcFaceLen];
                for (var i = 0; i < ArcFaceLen; i++)
                {
                    emb[i] = BinaryPrimitives.ReadSingleLittleEndian(blob.AsSpan(offset));
                    offset += sizeof(float);
                }

                stored.Embeddings.Add(emb);
            }

            return stored;
        }

        if (blob.Length >= 8 && blob.AsSpan(0, 4).SequenceEqual(SpatialMagic))
        {
            var count = BinaryPrimitives.ReadInt32LittleEndian(blob.AsSpan(4));
            var stored = new StoredFace();
            var offset = 8;
            for (var n = 0; n < count; n++)
            {
                var hist = new double[SpatialLen];
                for (var i = 0; i < SpatialLen; i++)
                {
                    hist[i] = BinaryPrimitives.ReadDoubleLittleEndian(blob.AsSpan(offset));
                    offset += sizeof(double);
                }

                stored.Spatial.Add(hist);
            }

            return stored;
        }

        return new StoredFace { Hists256 = [DeserializeHistogram(blob)] };
    }

    private static double[] BuildHistogramFromBytes(byte[] data)
    {
        var hist = new double[HistBins];
        if (data.Length == 0)
            return hist;

        var start = 0;
        if (data.Length > 2 && data[0] == 0xFF && data[1] == 0xD8)
            start = Math.Min(data.Length, 64);

        for (var i = start; i < data.Length; i++)
            hist[data[i]]++;

        Normalize(hist);
        return hist;
    }

    private static void Normalize(double[] hist)
    {
        var sum = 0.0;
        for (var i = 0; i < hist.Length; i++)
            sum += hist[i];
        if (sum <= 0)
            return;
        for (var i = 0; i < hist.Length; i++)
            hist[i] /= sum;
    }

    private void EnsureEngine()
    {
        if (_engineReady)
            return;

        lock (_cvLock)
        {
            if (_engineReady)
                return;

            try
            {
                var frontal = FindModel("haarcascade_frontalface_alt2.xml");
                var profile = FindModel("haarcascade_profileface.xml");
                var eye = FindModel("haarcascade_eye.xml");
                var eyeGlasses = FindModel("haarcascade_eye_tree_eyeglasses.xml");
                var arcface = FindModel("arcfaceresnet100-8.onnx");
                if (OpenCvAvailable && frontal is not null)
                {
                    _frontal = new CascadeClassifier(frontal);
                    if (profile is not null)
                        _profile = new CascadeClassifier(profile);
                    if (eye is not null)
                        _eye = new CascadeClassifier(eye);
                    if (eyeGlasses is not null)
                        _eyeGlasses = new CascadeClassifier(eyeGlasses);
                }

                if (arcface is not null)
                {
                    var options = new SessionOptions
                    {
                        GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                        IntraOpNumThreads = 2
                    };
                    _arcFace = new InferenceSession(arcface, options);
                    _arcFaceInput = _arcFace.InputMetadata.Keys.First();
                    _useArcFace = true;
                }
            }
            catch
            {
                _frontal?.Dispose();
                _profile?.Dispose();
                _eye?.Dispose();
                _eyeGlasses?.Dispose();
                _arcFace?.Dispose();
                _frontal = null;
                _profile = null;
                _eye = null;
                _eyeGlasses = null;
                _arcFace = null;
                _arcFaceInput = null;
                _useArcFace = false;
            }

            _engineReady = true;
        }
    }

    private string? FindModel(string fileName)
    {
        var dirs = new List<string>();
        if (!string.IsNullOrWhiteSpace(_modelDirectory))
            dirs.Add(_modelDirectory);

        dirs.Add(Path.Combine(AppContext.BaseDirectory, "models"));
        var asmDir = Path.GetDirectoryName(typeof(LocalHistogramFaceService).Assembly.Location);
        if (!string.IsNullOrWhiteSpace(asmDir))
            dirs.Add(Path.Combine(asmDir, "models"));

        foreach (var dir in dirs)
        {
            var path = Path.Combine(dir, fileName);
            if (File.Exists(path) && new FileInfo(path).Length > 10_000)
                return path;
        }

        return null;
    }

    private static bool DetectOpenCv()
    {
        try
        {
            _ = typeof(Cv2).FullName;
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                   || RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                   || RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        }
        catch
        {
            return false;
        }
    }

    private sealed class StoredFace
    {
        public List<float[]> Embeddings { get; } = [];
        public List<double[]> Spatial { get; init; } = [];
        public List<double[]> Hists256 { get; init; } = [];
    }
}
