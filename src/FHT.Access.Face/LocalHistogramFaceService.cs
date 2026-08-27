using System.Buffers.Binary;
using System.Runtime.InteropServices;
using FHT.Access.Domain.Abstractions;
using OpenCvSharp;
using OpenCvSharp.Dnn;

namespace FHT.Access.Face;

/// <summary>
/// Local face engine: YuNet detect + SFace embeddings when models are present;
/// otherwise a spatial histogram with rotation/mirror probes.
/// Matches across 90° camera mounts and mirrored (selfie) frames.
/// </summary>
public sealed class LocalHistogramFaceService : IFaceRecognitionService, IDisposable
{
    public const string HistModelVersion = "hist-v1";
    public const string SpatialModelVersion = "hist-v2";
    public const string SfaceModelVersion = "sface-v1";

    public static readonly string[] CompatibleModelVersions =
    [
        HistModelVersion,
        SpatialModelVersion,
        SfaceModelVersion
    ];

    private const int HistBins = 256;
    private const int Grid = 8;
    private const int CellBins = 16;
    private const int SpatialLen = Grid * Grid * CellBins;
    private const int SfaceLen = 128;
    private const float SfaceDefaultThreshold = 0.35f;
    private const float SpatialDefaultThreshold = 0.45f;
    private const int DetectMaxWidth = 640;

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
    private Net? _sfaceNet;
    private bool _useSface;
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

    public string ModelVersion => _useSface ? SfaceModelVersion : SpatialModelVersion;

    public static bool CanHydrate(string? modelVersion)
        => !string.IsNullOrWhiteSpace(modelVersion)
           && CompatibleModelVersions.Contains(modelVersion, StringComparer.Ordinal);

    public async Task EnrollAsync(Guid memberId, byte[] imageBgrOrJpeg, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(imageBgrOrJpeg);
        ct.ThrowIfCancellationRequested();
        EnsureEngine();

        var stored = BuildStoredFace(imageBgrOrJpeg, enroll: true);
        if (stored.Sface.Count == 0 && stored.Spatial.Count == 0)
            throw new InvalidOperationException("Nenhum rosto detectado. Olhe para a câmera e tente de novo.");
        var blob = SerializeStored(stored);

        lock (_sync)
        {
            _templates[memberId] = stored;
        }

        if (_persistAsync is not null)
            await _persistAsync(memberId, blob, ct).ConfigureAwait(false);
    }

    public Task<FaceMatchResult?> IdentifyAsync(byte[] imageBgrOrJpeg, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(imageBgrOrJpeg);
        ct.ThrowIfCancellationRequested();
        EnsureEngine();

        var probe = BuildStoredFace(imageBgrOrJpeg, enroll: false);
        Guid? bestId = null;
        var bestScore = 0.0;
        var bestSface = false;

        lock (_sync)
        {
            foreach (var (memberId, template) in _templates)
            {
                var score = Score(probe, template, out var usedSface);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestId = memberId;
                    bestSface = usedSface;
                }
            }
        }

        var cutoff = _threshold > 0.7
            ? (bestSface ? SfaceDefaultThreshold : SpatialDefaultThreshold)
            : _threshold;
        if (bestId is null || bestScore < cutoff)
            return Task.FromResult<FaceMatchResult?>(null);

        return Task.FromResult<FaceMatchResult?>(new FaceMatchResult(bestId.Value, bestScore));
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
            _sfaceNet?.Dispose();
            _frontal = null;
            _profile = null;
            _sfaceNet = null;
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

    private StoredFace BuildStoredFace(byte[] imageBgrOrJpeg, bool enroll)
    {
        if (OpenCvAvailable)
        {
            try
            {
                return BuildWithOpenCv(imageBgrOrJpeg, enroll);
            }
            catch
            {
                // Fall through.
            }
        }

        return new StoredFace
        {
            Hists256 = [BuildHistogramFromBytes(imageBgrOrJpeg)]
        };
    }

    private StoredFace BuildWithOpenCv(byte[] imageBgrOrJpeg, bool enroll)
    {
        using var src = Cv2.ImDecode(imageBgrOrJpeg, ImreadModes.Color);
        if (src.Empty())
        {
            return new StoredFace { Hists256 = [BuildHistogramFromBytes(imageBgrOrJpeg)] };
        }

        var stored = new StoredFace();
        var variants = BuildVariants(src, enroll);
        var detected = false;
        try
        {
            foreach (var variant in variants)
            {
                using var work = Downscale(variant);
                using var enhanced = EnhanceLighting(work);
                var face = DetectLargestFace(enhanced);
                Mat region;
                if (face is { } rect)
                {
                    detected = true;
                    region = PaddedSquare(enhanced, rect);
                }
                else if (enroll)
                {
                    continue;
                }
                else
                {
                    region = FaceishCropBgr(enhanced);
                }

                try
                {
                    if (_useSface)
                    {
                        foreach (var emb in EmbedCrop(region, enroll))
                            stored.Sface.Add(emb);
                    }

                    stored.Spatial.Add(BuildSpatial(region));
                    stored.Hists256.Add(BuildIntensityHist(region));
                }
                finally
                {
                    region.Dispose();
                }

                if (!enroll && (stored.Sface.Count > 0 || detected))
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

        if (stored.Sface.Count == 0 && stored.Spatial.Count == 0 && stored.Hists256.Count == 0)
            stored.Hists256.Add(BuildHistogramFromBytes(imageBgrOrJpeg));

        return stored;
    }

    private static List<Mat> BuildVariants(Mat src, bool enroll)
    {
        var list = new List<Mat> { src };
        void AddRotate(RotateFlags flag)
        {
            var r = new Mat();
            Cv2.Rotate(src, r, flag);
            list.Add(r);
        }

        var mirrored = new Mat();
        Cv2.Flip(src, mirrored, FlipMode.Y);
        list.Add(mirrored);

        AddRotate(RotateFlags.Rotate90Clockwise);
        AddRotate(RotateFlags.Rotate90Counterclockwise);

        if (enroll)
            AddRotate(RotateFlags.Rotate180);

        return list;
    }

    private List<float[]> EmbedCrop(Mat bgrCrop, bool enroll)
    {
        var found = new List<float[]>();
        lock (_cvLock)
        {
            if (_sfaceNet is null)
                return found;

            using var aligned = new Mat();
            Cv2.Resize(bgrCrop, aligned, new Size(112, 112), 0, 0, InterpolationFlags.Area);
            found.Add(ToEmbedding(aligned));

            using var flipped = new Mat();
            Cv2.Flip(aligned, flipped, FlipMode.Y);
            found.Add(ToEmbedding(flipped));

            if (enroll)
            {
                using var slight = new Mat();
                var center = new Point2f(aligned.Width / 2f, aligned.Height / 2f);
                using var rot = Cv2.GetRotationMatrix2D(center, 12, 1.0);
                Cv2.WarpAffine(aligned, slight, rot, aligned.Size());
                found.Add(ToEmbedding(slight));
            }
        }

        return found;
    }

    private Rect? DetectLargestFace(Mat bgr)
    {
        using var gray = new Mat();
        Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.EqualizeHist(gray, gray);

        Rect[] hits = [];
        if (_frontal is not null)
            hits = _frontal.DetectMultiScale(gray, 1.08, 3, HaarDetectionTypes.ScaleImage, new Size(28, 28));
        if (hits.Length == 0 && _profile is not null)
            hits = _profile.DetectMultiScale(gray, 1.08, 3, HaarDetectionTypes.ScaleImage, new Size(28, 28));
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

    private float[] ToEmbedding(Mat alignedBgr)
    {
        using var blob = CvDnn.BlobFromImage(
            alignedBgr,
            scaleFactor: 1.0,
            size: new Size(112, 112),
            mean: new Scalar(0, 0, 0),
            swapRB: true,
            crop: false);
        _sfaceNet!.SetInput(blob);
        using var feature = _sfaceNet.Forward();
        var emb = new float[SfaceLen];
        var n = Math.Min(SfaceLen, feature.Total());
        Marshal.Copy(feature.Data, emb, 0, (int)n);
        return emb;
    }

    private static Mat Downscale(Mat bgr)
    {
        if (bgr.Width <= DetectMaxWidth)
            return bgr.Clone();

        var scale = DetectMaxWidth / (double)bgr.Width;
        var size = new Size(DetectMaxWidth, Math.Max(1, (int)Math.Round(bgr.Height * scale)));
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
            using var clahe = Cv2.CreateCLAHE(2.0, new Size(8, 8));
            clahe.Apply(planes[0], planes[0]);
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

    private static double Score(StoredFace probe, StoredFace template, out bool usedSface)
    {
        usedSface = false;
        var best = 0.0;

        if (probe.Sface.Count > 0 && template.Sface.Count > 0)
        {
            usedSface = true;
            foreach (var a in probe.Sface)
            {
                foreach (var b in template.Sface)
                    best = Math.Max(best, Cosine(a, b));
            }

            return best;
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
        if (stored.Sface.Count > 0)
        {
            var count = stored.Sface.Count;
            var bytes = new byte[4 + 4 + (count * SfaceLen * sizeof(float))];
            SfaceMagic.CopyTo(bytes, 0);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), count);
            var offset = 8;
            foreach (var emb in stored.Sface)
            {
                for (var i = 0; i < SfaceLen; i++)
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
        {
            var count = BinaryPrimitives.ReadInt32LittleEndian(blob.AsSpan(4));
            var stored = new StoredFace();
            var offset = 8;
            for (var n = 0; n < count; n++)
            {
                var emb = new float[SfaceLen];
                for (var i = 0; i < SfaceLen; i++)
                {
                    emb[i] = BinaryPrimitives.ReadSingleLittleEndian(blob.AsSpan(offset));
                    offset += sizeof(float);
                }

                stored.Sface.Add(emb);
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
                var sface = FindModel("face_recognition_sface_2021dec.onnx");
                if (OpenCvAvailable && frontal is not null)
                {
                    _frontal = new CascadeClassifier(frontal);
                    if (profile is not null)
                        _profile = new CascadeClassifier(profile);
                }

                if (OpenCvAvailable && sface is not null && _frontal is not null && !_frontal.Empty())
                {
                    _sfaceNet = CvDnn.ReadNetFromOnnx(sface);
                    _useSface = _sfaceNet is not null;
                }
            }
            catch
            {
                _frontal?.Dispose();
                _profile?.Dispose();
                _sfaceNet?.Dispose();
                _frontal = null;
                _profile = null;
                _sfaceNet = null;
                _useSface = false;
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
        public List<float[]> Sface { get; } = [];
        public List<double[]> Spatial { get; init; } = [];
        public List<double[]> Hists256 { get; init; } = [];
    }
}
