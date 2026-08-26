using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Encoder = System.Drawing.Imaging.Encoder;

namespace WinLock.Agent.UI;

/// <summary>
/// Captures the whole virtual desktop (every monitor) to a JPEG file. Uses raw Win32 virtual
/// screen metrics rather than WPF's <c>SystemParameters</c>, so the captured region is exact
/// physical pixels regardless of per-monitor DPI scaling.
/// </summary>
internal static class ScreenCapture
{
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    public static void CaptureToJpegFile(string path, long jpegQuality = 60)
    {
        var x = GetSystemMetrics(SM_XVIRTUALSCREEN);
        var y = GetSystemMetrics(SM_YVIRTUALSCREEN);
        var width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        var height = GetSystemMetrics(SM_CYVIRTUALSCREEN);

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.CopyFromScreen(x, y, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
        }

        var jpegEncoder = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var encoderParams = new EncoderParameters(1);
        encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, jpegQuality);
        bitmap.Save(path, jpegEncoder, encoderParams);
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
