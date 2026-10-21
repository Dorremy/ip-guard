using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace IpGuard;

public static class Gfx
{
    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var path = new GraphicsPath();
        if (d <= 0.5f)
        {
            path.AddRectangle(r);
            return path;
        }

        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void DrawFaded(Graphics g, Bitmap image, Rectangle dest, float alpha)
    {
        if (alpha >= 0.995f)
        {
            g.DrawImageUnscaled(image, dest.Location);
            return;
        }

        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(new ColorMatrix { Matrix33 = alpha });
        g.DrawImage(image, dest, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
    }

    public static Color Mix(Color a, Color b, float t)
    {
        var k = Math.Clamp(t, 0f, 1f);
        return Color.FromArgb((int)(a.R + (b.R - a.R) * k), (int)(a.G + (b.G - a.G) * k), (int)(a.B + (b.B - a.B) * k));
    }

    public static float Smooth(float x)
    {
        var t = Math.Clamp(x, 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
