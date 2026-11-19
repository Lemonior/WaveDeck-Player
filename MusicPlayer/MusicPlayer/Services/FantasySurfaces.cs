using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MusicPlayer.Services;

internal static class FantasySurfaces
{
    public static ImageBrush Texture(Color color, int variation = 3)
    {
        const int side = 96; var pixels = new byte[side * side * 4]; uint random = 0x2a794bc3;
        for (var y = 0; y < side; y++)
            for (var x = 0; x < side; x++)
            {
                random ^= random << 13; random ^= random >> 17; random ^= random << 5;
                var noise = (int)(random % (variation * 2 + 1)) - variation;
                // Seamless faint broad mottling underneath fine grain.
                noise += (int)(Math.Sin(x * Math.PI * 2 / side) * Math.Sin(y * Math.PI * 4 / side) * 2);
                var offset = (y * side + x) * 4;
                pixels[offset] = (byte)Math.Clamp(color.B + noise, 0, 255);
                pixels[offset + 1] = (byte)Math.Clamp(color.G + noise, 0, 255);
                pixels[offset + 2] = (byte)Math.Clamp(color.R + noise, 0, 255); pixels[offset + 3] = 255;
            }
        var bitmap = BitmapSource.Create(side, side, 96, 96, PixelFormats.Bgra32, null, pixels, side * 4); bitmap.Freeze();
        var brush = new ImageBrush(bitmap) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, side, side), Stretch = Stretch.None }; brush.Freeze(); return brush;
    }
    public static DrawingBrush Header()
    {
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
        {
            dc.DrawRectangle(Texture(Color.FromRgb(41, 37, 31), 4), null, new Rect(0, 0, 160, 64));
            var shade = new SolidColorBrush(Color.FromArgb(35, 10, 9, 7));
            // Shallow, irregular stone facets, rather than a polished chrome gradient.
            foreach (var points in new[] { new[] { new Point(0, 0), new Point(60, 0), new Point(47, 20), new Point(4, 29) }, new[] { new Point(160, 9), new Point(115, 0), new Point(96, 27), new Point(140, 43) }, new[] { new Point(20, 64), new Point(72, 41), new Point(97, 64) } })
            {
                var path = new StreamGeometry(); using (var geometry = path.Open()) { geometry.BeginFigure(points[0], true, true); geometry.PolyLineTo(points.Skip(1).ToArray(), true, false); } path.Freeze(); dc.DrawGeometry(shade, null, path);
            }
        }
        drawing.Freeze(); var brush = new DrawingBrush(drawing) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 160, 64), Stretch = Stretch.None }; brush.Freeze(); return brush;
    }
    public static DrawingBrush Console(Color color, bool retro, bool chrome = false)
    {
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
        {
            dc.DrawRectangle(Texture(color, retro ? 3 : 2), null, new Rect(0, 0, 96, 64));
            if (chrome)
            {
                var sheen = new LinearGradientBrush(Color.FromArgb(38, 255, 255, 255), Color.FromArgb(20, 0, 0, 0), new Point(0, 0), new Point(0, 1));
                dc.DrawRectangle(sheen, null, new Rect(0, 0, 96, 64));
            }
            var line = new Pen(new SolidColorBrush(Color.FromArgb(retro ? (byte)22 : (byte)7, 0, 0, 0)), 1);
            for (var y = 1; y < 64; y += retro ? 4 : 2) dc.DrawLine(line, new Point(0, y), new Point(96, y));
        }
        drawing.Freeze(); var brush = new DrawingBrush(drawing) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 96, 64), Stretch = Stretch.None }; brush.Freeze(); return brush;
    }
}
