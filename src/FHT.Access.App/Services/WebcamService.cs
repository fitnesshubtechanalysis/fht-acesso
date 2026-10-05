using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCvSharp;

namespace FHT.Access.App.Services;

public sealed class WebcamFrameEventArgs : EventArgs
{
    public required byte[] JpegBytes { get; init; }
    public required BitmapSource Bitmap { get; init; }
}

public enum WebcamConnectionState
{
    Disconnected,
    Connected,
    Reconnecting,
    Unavailable
}

/// <summary>
/// OpenCvSharp VideoCapture loop — preview at camera resolution, JPEG for face pipeline downscaled.
/// </summary>
public sealed class WebcamService : IDisposable
{
    private const int DefaultMaxProcessWidth = 1280;
    private static readonly int[] JpegParams = { (int)ImwriteFlags.JpegQuality, 88 };

    private readonly object _sync = new();
    private VideoCapture? _capture;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private byte[]? _lastJpeg;
    private bool _disposed;
    private Mat? _prevGray;
    private DateTime _motionUntilUtc;
    private int _warmupFrames;
    private int _cameraIndex;
    private int[] _openOrder = [0];
    private int _openOrderPos;
    private bool _stickToIndex;
    private int _emptyReads;
    private string? _deviceId;
    private int _width = 1920;
    private int _height = 1080;
    private int _previewFps = 30;
    private int _processFps = 8;
    private long _frameCounter;
    private WebcamConnectionState _state = WebcamConnectionState.Disconnected;

    public double MotionRatioThreshold { get; set; } = 0.025;
    public int MotionPixelThreshold { get; set; } = 28;
    public int MaxProcessWidth { get; set; } = DefaultMaxProcessWidth;
    public TimeSpan MotionHold { get; set; } = TimeSpan.FromSeconds(1.6);

    /// <summary>Frações 0–1: só o centro da imagem conta como movimento (ignora fundo/corredor).</summary>
    public double MotionRoiWidthFraction { get; set; } = 0.55;
    public double MotionRoiHeightFraction { get; set; } = 0.62;
    /// <summary>Centro vertical do ROI (0=topo, 1=base). ~0.45 favorece pessoa parada na catraca.</summary>
    public double MotionRoiCenterY { get; set; } = 0.45;

    public event EventHandler<WebcamFrameEventArgs>? FrameReady;
    public event EventHandler<WebcamConnectionState>? StateChanged;

    public bool IsRunning
    {
        get
        {
            lock (_sync) return _loop is { IsCompleted: false };
        }
    }

    public WebcamConnectionState State
    {
        get { lock (_sync) return _state; }
    }

    public int CameraIndex { get; private set; }

    public string? LastOpenError { get; private set; }

    public long FramesCaptured { get; private set; }

    public void Configure(int width, int height, int previewFps, int processFps)
    {
        _width = width > 0 ? width : 1920;
        _height = height > 0 ? height : 1080;
        _previewFps = previewFps > 0 ? previewFps : 30;
        _processFps = processFps is > 0 and <= 30 ? processFps : 8;
    }

    public void Start(int cameraIndex, string? deviceId = null, IReadOnlyList<int>? openOrder = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Stop();

        _openOrder = CopyOpenOrder(openOrder, cameraIndex);
        _openOrderPos = 0;
        _stickToIndex = false;
        _emptyReads = 0;
        _cameraIndex = _openOrder[0];
        _deviceId = deviceId;
        SetState(WebcamConnectionState.Reconnecting);

        lock (_sync)
        {
            _warmupFrames = 0;
            _motionUntilUtc = DateTime.MinValue;
            _prevGray?.Dispose();
            _prevGray = null;
            _frameCounter = 0;
            _cts = new CancellationTokenSource();
            _loop = Task.Run(() => CaptureLoop(_cts.Token));
        }
    }

    public void Stop()
    {
        CancellationTokenSource? cts;
        Task? loop;

        lock (_sync)
        {
            cts = _cts;
            loop = _loop;
            _cts = null;
            _loop = null;
            _capture?.Dispose();
            _capture = null;
        }

        try { cts?.Cancel(); } catch { /* ignore */ }

        try { loop?.Wait(TimeSpan.FromSeconds(2)); } catch { /* ignore */ }

        cts?.Dispose();
        _prevGray?.Dispose();
        _prevGray = null;
        SetState(WebcamConnectionState.Disconnected);
    }

    public bool HasMotion()
    {
        lock (_sync)
            return DateTime.UtcNow < _motionUntilUtc;
    }

    public byte[]? GetJpegFrame()
    {
        lock (_sync)
            return _lastJpeg is null ? null : (byte[])_lastJpeg.Clone();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Stop();
    }

    private void CaptureLoop(CancellationToken ct)
    {
        var previewDelayMs = Math.Max(1, 1000 / _previewFps);
        var processEveryN = Math.Max(1, _previewFps / Math.Max(1, _processFps));

        while (!ct.IsCancellationRequested)
        {
            try
            {
                EnsureCapture();
            }
            catch
            {
                SetState(WebcamConnectionState.Unavailable);
                Thread.Sleep(2000);
                continue;
            }

            VideoCapture? capture;
            lock (_sync) capture = _capture;
            if (capture is null || !capture.IsOpened())
            {
                SetState(WebcamConnectionState.Reconnecting);
                Thread.Sleep(1500);
                continue;
            }

            using var frame = new Mat();
            if (!capture.Read(frame) || frame.Empty())
            {
                _emptyReads++;
                if (_emptyReads < 12)
                {
                    Thread.Sleep(40);
                    continue;
                }

                _emptyReads = 0;
                SetState(WebcamConnectionState.Reconnecting);
                lock (_sync)
                {
                    _capture?.Dispose();
                    _capture = null;
                }
                Thread.Sleep(500);
                continue;
            }

            _emptyReads = 0;

            SetState(WebcamConnectionState.Connected);
            _frameCounter++;
            FramesCaptured++;
            var shouldProcess = _frameCounter % processEveryN == 0;

            UpdateMotion(frame);

            if (shouldProcess)
            {
                using var processFrame = DownscaleForProcessing(frame, MaxProcessWidth);
                if (!Cv2.ImEncode(".jpg", processFrame, out var jpeg, JpegParams)
                    || jpeg is null
                    || jpeg.Length == 0)
                    continue;

                BitmapSource? bitmap;
                try { bitmap = ToFrozenBitmap(frame); }
                catch { continue; }

                lock (_sync) _lastJpeg = jpeg;

                try
                {
                    FrameReady?.Invoke(this, new WebcamFrameEventArgs
                    {
                        JpegBytes = jpeg,
                        Bitmap = bitmap
                    });
                }
                catch { /* subscriber errors */ }
            }

            Thread.Sleep(previewDelayMs);
        }
    }

    private void EnsureCapture()
    {
        lock (_sync)
        {
            if (_capture is not null && _capture.IsOpened())
                return;

            _capture?.Dispose();
            _capture = null;

            var index = _stickToIndex
                ? _cameraIndex
                : _openOrder[_openOrderPos % _openOrder.Length];

            var opened = OpenDeliveringCapture(index);
            if (opened is null)
            {
                if (!_stickToIndex)
                    _openOrderPos++;
                LastOpenError = $"Não foi possível abrir a câmera índice {index}.";
                throw new InvalidOperationException(LastOpenError);
            }

            _capture = opened;
            _cameraIndex = index;
            CameraIndex = index;
            _stickToIndex = true;
            LastOpenError = null;
        }
    }

    /// <summary>
    /// Abre um índice e espera um quadro de verdade.
    /// No Windows o MSMF costuma devolver tela azul; o DSHOW entra primeiro.
    /// </summary>
    private VideoCapture? OpenDeliveringCapture(int index)
    {
        foreach (var width in new[] { _width, 1280 })
        {
            var height = width == _width ? _height : 720;
            var capture = OpenUsableCapture(index, width, height);
            if (capture is not null)
                return capture;
        }

        LastOpenError = $"Câmera {index} não entrega imagem utilizável.";
        return null;
    }

    private VideoCapture? OpenUsableCapture(int index, int width, int height)
    {
        foreach (var api in CaptureApis())
        {
            var capture = TryOpenCapture(index, api);
            if (capture is null)
                continue;

            ApplyCaptureProperties(capture, width, height, width == _width ? _previewFps : Math.Min(_previewFps, 30));
            if (ReadWarmFrame(capture))
                return capture;

            capture.Dispose();
            Thread.Sleep(250);
        }

        return null;
    }

    private static VideoCaptureAPIs[] CaptureApis()
    {
        if (OperatingSystem.IsWindows())
            return [VideoCaptureAPIs.DSHOW, VideoCaptureAPIs.MSMF];

        return [VideoCaptureAPIs.ANY];
    }

    private static bool ReadWarmFrame(VideoCapture capture)
    {
        using var frame = new Mat();
        for (var attempt = 0; attempt < 12; attempt++)
        {
            if (capture.Read(frame) && IsUsableColorFrame(frame))
                return true;
            Thread.Sleep(80);
        }

        return false;
    }

    /// <summary>
    /// Tela azul contínua do driver não conta como câmera aberta.
    /// </summary>
    private static bool IsUsableColorFrame(Mat bgr)
    {
        if (bgr.Empty() || bgr.Width < 16 || bgr.Channels() < 3)
            return false;

        using var small = new Mat();
        Cv2.Resize(bgr, small, new OpenCvSharp.Size(32, 24), interpolation: InterpolationFlags.Area);
        var mean = Cv2.Mean(small);
        var b = mean.Val0;
        var g = mean.Val1;
        var r = mean.Val2;
        return !(b > 90 && r < 50 && g < 70 && b > r + 40 && b > g + 25);
    }

    private static int[] CopyOpenOrder(IReadOnlyList<int>? openOrder, int cameraIndex)
    {
        if (openOrder is { Count: > 0 })
        {
            var copy = new List<int>(openOrder.Count);
            foreach (var index in openOrder)
            {
                if (index >= 0 && !copy.Contains(index))
                    copy.Add(index);
            }

            if (copy.Count > 0)
                return copy.ToArray();
        }

        return [cameraIndex >= 0 ? cameraIndex : 0];
    }

    private static void ApplyCaptureProperties(VideoCapture capture, int width, int height, int fps)
    {
        if (width > 0)
            capture.Set(VideoCaptureProperties.FrameWidth, width);
        if (height > 0)
            capture.Set(VideoCaptureProperties.FrameHeight, height);
        if (fps > 0)
            capture.Set(VideoCaptureProperties.Fps, fps);
        try { capture.Set(VideoCaptureProperties.ConvertRgb, 1); } catch { /* driver-dependent */ }
        try { capture.Set(VideoCaptureProperties.AutoExposure, 0.75); } catch { /* driver-dependent */ }
        // 0 = sem zoom digital na maioria das UVC. Zoom alto corta o rosto e o totem não reconhece.
        try { capture.Set(VideoCaptureProperties.Zoom, 0); } catch { /* driver-dependent */ }
    }

    private static VideoCapture? TryOpenCapture(int index, VideoCaptureAPIs api)
    {
        var capture = new VideoCapture(index, api);
        if (capture.IsOpened())
            return capture;

        capture.Dispose();
        return null;
    }

    private static Mat DownscaleForProcessing(Mat bgr, int maxProcessWidth)
    {
        var maxWidth = maxProcessWidth > 0 ? maxProcessWidth : DefaultMaxProcessWidth;
        if (bgr.Width <= maxWidth)
            return bgr.Clone();

        var scale = maxWidth / (double)bgr.Width;
        var h = Math.Max(1, (int)Math.Round(bgr.Height * scale));
        var resized = new Mat();
        Cv2.Resize(bgr, resized, new OpenCvSharp.Size(maxWidth, h));
        return resized;
    }

    private void UpdateMotion(Mat bgr)
    {
        using var grayFull = new Mat();
        Cv2.CvtColor(bgr, grayFull, ColorConversionCodes.BGR2GRAY);
        using var gray = MotionRoi(grayFull);
        Cv2.GaussianBlur(gray, gray, new OpenCvSharp.Size(21, 21), 0);

        lock (_sync)
        {
            _warmupFrames++;
            if (_warmupFrames < 8 || _prevGray is null || _prevGray.Empty()
                || _prevGray.Width != gray.Width || _prevGray.Height != gray.Height)
            {
                _prevGray?.Dispose();
                _prevGray = gray.Clone();
                return;
            }

            using var diff = new Mat();
            using var thresh = new Mat();
            Cv2.Absdiff(_prevGray, gray, diff);
            Cv2.Threshold(diff, thresh, MotionPixelThreshold, 255, ThresholdTypes.Binary);

            var changed = Cv2.CountNonZero(thresh);
            var total = thresh.Rows * thresh.Cols;
            if (total > 0 && changed / (double)total >= MotionRatioThreshold)
                _motionUntilUtc = DateTime.UtcNow.Add(MotionHold);

            _prevGray.Dispose();
            _prevGray = gray.Clone();
        }
    }

    /// <summary>Recorte central — pessoas passando no fundo da lente não disparam reconhecimento.</summary>
    private Mat MotionRoi(Mat gray)
    {
        var wf = Math.Clamp(MotionRoiWidthFraction, 0.25, 1.0);
        var hf = Math.Clamp(MotionRoiHeightFraction, 0.25, 1.0);
        var cy = Math.Clamp(MotionRoiCenterY, 0.2, 0.8);
        var w = Math.Max(16, (int)(gray.Width * wf));
        var h = Math.Max(16, (int)(gray.Height * hf));
        var x = Math.Clamp((gray.Width - w) / 2, 0, Math.Max(0, gray.Width - w));
        var y = Math.Clamp((int)(gray.Height * cy) - h / 2, 0, Math.Max(0, gray.Height - h));
        return new Mat(gray, new Rect(x, y, w, h)).Clone();
    }

    private void SetState(WebcamConnectionState state)
    {
        lock (_sync)
        {
            if (_state == state)
                return;
            _state = state;
        }

        try { StateChanged?.Invoke(this, state); } catch { /* ignore */ }
    }

    private static BitmapSource ToFrozenBitmap(Mat bgr)
    {
        if (bgr.Channels() != 3)
            throw new InvalidOperationException("Expected BGR frame.");

        var width = bgr.Width;
        var height = bgr.Height;
        var stride = width * 3;
        var pixels = new byte[stride * height];
        System.Runtime.InteropServices.Marshal.Copy(bgr.Data, pixels, 0, pixels.Length);

        var bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgr24,
            null,
            pixels,
            stride);
        bitmap.Freeze();
        return bitmap;
    }
}
