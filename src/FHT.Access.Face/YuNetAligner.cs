using FHT.Access.Domain.Abstractions;
using OpenCvSharp;

namespace FHT.Access.Face;

/// <summary>
/// YuNet (Apache 2.0, OpenCV Zoo) devolve olhos, nariz e cantos da boca.
/// O ArcFace só recebe o rosto colocado nesses cinco pontos.
/// </summary>
internal sealed class YuNetAligner : IDisposable
{
    public const int InputSize = 640;

    /// <summary>
    /// Olho esquerdo da imagem, olho direito, nariz, boca esquerda, boca direita.
    /// É o molde de 112×112 em que o ArcFace foi treinado.
    /// </summary>
    private static readonly Point2f[] Template =
    [
        new(38.2946f, 51.6963f),
        new(73.5318f, 51.5014f),
        new(56.0252f, 71.7366f),
        new(41.5493f, 92.3655f),
        new(70.7299f, 92.2041f),
    ];

    private readonly FaceDetectorYN _detector;
    private readonly object _cvLock;
    private bool _disposed;

    public YuNetAligner(string modelPath, object cvLock)
    {
        _cvLock = cvLock;
        _detector = FaceDetectorYN.Create(
            modelPath,
            "",
            new Size(InputSize, InputSize),
            scoreThreshold: 0.6f,
            nmsThreshold: 0.3f,
            topK: 500);
    }

    public bool TrySelect(Mat bgr, FaceDetectionOptions detect, out Point2f[] landmarks)
    {
        landmarks = [];
        if (_disposed || bgr.Empty())
            return false;

        var frameArea = Math.Max(1, bgr.Width * bgr.Height);
        var minArea = Math.Clamp(detect.MinFaceAreaFraction, 0, 0.5) * frameArea;
        var mx = Math.Clamp(detect.CenterXMargin, 0, 0.45);
        var my = Math.Clamp(detect.CenterYMargin, 0, 0.45);
        var minSide = Math.Max(12, detect.MinFaceSize);

        Point2f[]? best = null;
        var bestArea = 0f;
        foreach (var face in Detect(bgr))
        {
            var area = face.Box.Width * face.Box.Height;
            if (area < minArea || area <= bestArea)
                continue;
            if (Math.Max(face.Box.Width, face.Box.Height) < minSide)
                continue;

            var cx = face.Box.X + face.Box.Width / 2.0;
            var cy = face.Box.Y + face.Box.Height / 2.0;
            if (cx < bgr.Width * mx || cx > bgr.Width * (1.0 - mx))
                continue;
            if (cy < bgr.Height * my || cy > bgr.Height * (1.0 - my))
                continue;
            if (!LandmarksPlausible(face.Landmarks))
                continue;

            best = face.Landmarks;
            bestArea = area;
        }

        if (best is null)
            return false;

        landmarks = best;
        return true;
    }

    public static Mat? Align(Mat bgr, Point2f[] landmarks)
    {
        if (landmarks.Length < 5)
            return null;

        using var from = InputArray.Create(landmarks);
        using var to = InputArray.Create(Template);
        using var inliers = new Mat();
        using var matrix = Cv2.EstimateAffinePartial2D(
            from,
            to,
            inliers,
            RobustEstimationAlgorithms.LMEDS);
        if (matrix is not null && !matrix.Empty() && matrix.Rows >= 2 && matrix.Cols >= 3)
        {
            var aligned = new Mat();
            Cv2.WarpAffine(
                bgr,
                aligned,
                matrix,
                new Size(112, 112),
                InterpolationFlags.Linear,
                BorderTypes.Constant,
                Scalar.All(0));
            return aligned;
        }

        return AlignByEyes(bgr, landmarks[0], landmarks[1]);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _detector.Dispose();
    }

    private readonly record struct Hit(Rect2f Box, Point2f[] Landmarks);

    private List<Hit> Detect(Mat bgr)
    {
        var found = new List<Hit>();
        var scale = InputSize / (double)Math.Max(bgr.Width, bgr.Height);
        var resizedW = Math.Clamp((int)Math.Round(bgr.Width * scale), 1, InputSize);
        var resizedH = Math.Clamp((int)Math.Round(bgr.Height * scale), 1, InputSize);

        using var resized = new Mat();
        Cv2.Resize(bgr, resized, new Size(resizedW, resizedH), 0, 0, InterpolationFlags.Linear);
        using var canvas = new Mat(InputSize, InputSize, MatType.CV_8UC3, Scalar.All(0));
        var offsetX = (InputSize - resizedW) / 2;
        var offsetY = (InputSize - resizedH) / 2;
        using (var roi = new Mat(canvas, new Rect(offsetX, offsetY, resizedW, resizedH)))
            resized.CopyTo(roi);

        using var faces = new Mat();
        lock (_cvLock)
        {
            if (_disposed)
                return found;
            _detector.Detect(canvas, faces);
        }

        if (faces.Empty() || faces.Rows < 1 || faces.Cols < 15)
            return found;

        for (var i = 0; i < faces.Rows; i++)
        {
            var width = faces.At<float>(i, 2) / (float)scale;
            var height = faces.At<float>(i, 3) / (float)scale;
            if (width < 8 || height < 8)
                continue;

            var landmarks = new Point2f[5];
            for (var k = 0; k < 5; k++)
            {
                landmarks[k] = new Point2f(
                    (faces.At<float>(i, 4 + (k * 2)) - offsetX) / (float)scale,
                    (faces.At<float>(i, 5 + (k * 2)) - offsetY) / (float)scale);
            }

            var box = new Rect2f(
                (faces.At<float>(i, 0) - offsetX) / (float)scale,
                (faces.At<float>(i, 1) - offsetY) / (float)scale,
                width,
                height);
            found.Add(new Hit(box, landmarks));
        }

        return found;
    }

    private static bool LandmarksPlausible(Point2f[] landmarks)
    {
        var eye = Distance(landmarks[0], landmarks[1]);
        if (eye < 8)
            return false;

        var eyeLine = (landmarks[0].Y + landmarks[1].Y) / 2f;
        var nose = landmarks[2];
        if (nose.Y < eyeLine - (eye * 0.4f))
            return false;

        var mouth = (landmarks[3].Y + landmarks[4].Y) / 2f;
        return mouth >= nose.Y - (eye * 0.15f);
    }

    /// <summary>
    /// Os dois olhos do YuNet já estão na ordem do molde. Serve se o ajuste dos cinco pontos falhar.
    /// </summary>
    private static Mat? AlignByEyes(Mat bgr, Point2f leftEye, Point2f rightEye)
    {
        const float dstLx = 38.2946f;
        const float dstLy = 51.6963f;
        const float dstRx = 73.5318f;
        const float dstRy = 51.5014f;

        var srcDx = rightEye.X - leftEye.X;
        var srcDy = rightEye.Y - leftEye.Y;
        var srcLen = Math.Sqrt((srcDx * srcDx) + (srcDy * srcDy));
        if (srcLen < 4)
            return null;

        var dstDx = dstRx - dstLx;
        var dstDy = dstRy - dstLy;
        var scale = Math.Sqrt((dstDx * dstDx) + (dstDy * dstDy)) / srcLen;
        var angle = Math.Atan2(dstDy, dstDx) - Math.Atan2(srcDy, srcDx);
        var cos = Math.Cos(angle) * scale;
        var sin = Math.Sin(angle) * scale;
        var tx = dstLx - ((cos * leftEye.X) - (sin * leftEye.Y));
        var ty = dstLy - ((sin * leftEye.X) + (cos * leftEye.Y));

        using var matrix = new Mat(2, 3, MatType.CV_64FC1);
        matrix.Set(0, 0, cos);
        matrix.Set(0, 1, -sin);
        matrix.Set(0, 2, tx);
        matrix.Set(1, 0, sin);
        matrix.Set(1, 1, cos);
        matrix.Set(1, 2, ty);

        var aligned = new Mat();
        Cv2.WarpAffine(
            bgr,
            aligned,
            matrix,
            new Size(112, 112),
            InterpolationFlags.Linear,
            BorderTypes.Constant,
            Scalar.All(0));
        return aligned;
    }

    private static float Distance(Point2f a, Point2f b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return MathF.Sqrt((dx * dx) + (dy * dy));
    }
}
