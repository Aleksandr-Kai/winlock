using System.Windows.Media;
using System.Windows.Media.Imaging;
using QRCoder;

namespace WinLock.Agent.UI;

/// <summary>
/// Renders a QR code straight to a WPF vector image (no GDI+/System.Drawing dependency),
/// so it stays crisp at any size on any monitor's DPI.
/// </summary>
public static class QrCodeRenderer
{
    public static DrawingImage Render(string text, Color moduleColor, Color backgroundColor)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
        var matrix = data.ModuleMatrix;
        var size = matrix.Count;

        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(
            new SolidColorBrush(backgroundColor), null, new RectangleGeometry(new(0, 0, size, size))));

        var modules = new GeometryGroup();
        for (var row = 0; row < size; row++)
        for (var col = 0; col < size; col++)
        {
            if (matrix[row][col])
                modules.Children.Add(new RectangleGeometry(new(col, row, 1, 1)));
        }

        group.Children.Add(new GeometryDrawing(new SolidColorBrush(moduleColor), null, modules));
        group.Freeze();

        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }
}
