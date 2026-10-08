using OpenCvSharp;

namespace FHT.Access.App.Services;

/// <summary>
/// Recorte quadrado do círculo desenhado no cadastro facial.
/// As medidas acompanham o preview em AttendantShellView (364×205, círculo 150).
/// O lado de fora do círculo fica de fora do quadrado; o miolo não é pintado de preto,
/// para o YuNet ver o rosto inteiro.
/// </summary>
internal static class EnrollmentGuideCrop
{
    public const double ViewWidth = 364;
    public const double ViewHeight = 205;
    public const double CircleDiameter = 150;

    public static byte[] Apply(byte[] jpeg)
    {
        using var src = Cv2.ImDecode(jpeg, ImreadModes.Color);
        if (src.Empty() || src.Width < 32 || src.Height < 32)
            return jpeg;

        var scale = Math.Max(ViewWidth / src.Width, ViewHeight / src.Height);
        var offsetX = (src.Width - (ViewWidth / scale)) / 2.0;
        var offsetY = (src.Height - (ViewHeight / scale)) / 2.0;
        var radius = (CircleDiameter / 2.0) / scale;
        var cx = offsetX + (ViewWidth / 2.0) / scale;
        var cy = offsetY + (ViewHeight / 2.0) / scale;

        var side = Math.Max(32, (int)Math.Round(radius * 2));
        var x = (int)Math.Round(cx - radius);
        var y = (int)Math.Round(cy - radius);
        x = Math.Clamp(x, 0, Math.Max(0, src.Width - side));
        y = Math.Clamp(y, 0, Math.Max(0, src.Height - side));
        side = Math.Min(side, Math.Min(src.Width - x, src.Height - y));
        if (side < 32)
            return jpeg;

        using var crop = new Mat(src, new Rect(x, y, side, side));
        Cv2.ImEncode(".jpg", crop, out var bytes, [new ImageEncodingParam(ImwriteFlags.JpegQuality, 92)]);
        return bytes;
    }
}
