using SkiaSharp;

namespace MangosSuperUI.Services.WeaponForge;

/// <summary>
/// Deterministic first stage for the in-page reference workbench. It removes a near-white paper
/// background, crops/pads the drawing, and creates explicitly-labelled starter views. The side
/// starters are construction guides, not claimed AI observations: users may paint over or replace
/// each one before reconstruction. This keeps the page useful with any provider and offline.
/// </summary>
public sealed class WeaponSketchViewService
{
    public WeaponSketchViews Prepare(byte[] source, int size = 512)
    {
        size = Math.Clamp(size, 256, 1024);
        using var decoded = SKBitmap.Decode(source) ?? throw new InvalidDataException("Image could not be decoded.");
        using var normalized = Normalize(decoded, size);
        float angle = DominantInkAngle(normalized);
        using var back = TransformAlongAxis(normalized, angle, 1f, mirrorAcrossAxis: true);
        using var left = TransformAlongAxis(normalized, angle, .20f, mirrorAcrossAxis: false);
        using var right = TransformAlongAxis(normalized, angle, .20f, mirrorAcrossAxis: true);
        using var threeQuarter = TransformAlongAxis(normalized, angle, .62f, mirrorAcrossAxis: false);
        return new WeaponSketchViews(
            Encode(normalized), Encode(back), Encode(left), Encode(right), Encode(threeQuarter), angle);
    }

    private static SKBitmap Normalize(SKBitmap source, int size)
    {
        int minX = source.Width, minY = source.Height, maxX = -1, maxY = -1;
        for (int y = 0; y < source.Height; y++)
        for (int x = 0; x < source.Width; x++)
        {
            var c = source.GetPixel(x, y);
            if (c.Alpha < 12) continue;
            int lum = (c.Red * 299 + c.Green * 587 + c.Blue * 114) / 1000;
            if (lum > 242) continue;
            minX = Math.Min(minX, x); minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
        }
        if (maxX < minX || maxY < minY)
            throw new InvalidDataException("No drawing was found against the paper background.");

        float pad = Math.Max(maxX - minX, maxY - minY) * .07f;
        var srcRect = new SKRect(
            Math.Max(0, minX - pad), Math.Max(0, minY - pad),
            Math.Min(source.Width, maxX + 1 + pad), Math.Min(source.Height, maxY + 1 + pad));
        float scale = Math.Min((size * .92f) / srcRect.Width, (size * .92f) / srcRect.Height);
        var dst = new SKRect(
            (size - srcRect.Width * scale) / 2f, (size - srcRect.Height * scale) / 2f,
            (size + srcRect.Width * scale) / 2f, (size + srcRect.Height * scale) / 2f);

        var output = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(output);
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High };
        canvas.DrawBitmap(source, srcRect, dst, paint);
        // Convert the white paper to alpha while preserving grey antialiasing as line opacity.
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            var c = output.GetPixel(x, y);
            int lum = (c.Red * 299 + c.Green * 587 + c.Blue * 114) / 1000;
            byte alpha = (byte)Math.Clamp((255 - lum) * 5, 0, 255);
            output.SetPixel(x, y, new SKColor(c.Red, c.Green, c.Blue, alpha));
        }
        return output;
    }

    private static float DominantInkAngle(SKBitmap image)
    {
        double sx = 0, sy = 0, weight = 0;
        for (int y = 0; y < image.Height; y += 2)
        for (int x = 0; x < image.Width; x += 2)
        {
            double w = image.GetPixel(x, y).Alpha / 255d;
            sx += x * w; sy += y * w; weight += w;
        }
        if (weight <= 0) return 0;
        double cx = sx / weight, cy = sy / weight;
        double cxx = 0, cyy = 0, cxy = 0;
        for (int y = 0; y < image.Height; y += 2)
        for (int x = 0; x < image.Width; x += 2)
        {
            double w = image.GetPixel(x, y).Alpha / 255d;
            double dx = x - cx, dy = y - cy;
            cxx += dx * dx * w; cyy += dy * dy * w; cxy += dx * dy * w;
        }
        return (float)(.5 * Math.Atan2(2 * cxy, cxx - cyy));
    }

    private static SKBitmap TransformAlongAxis(SKBitmap src, float angle, float perpendicularScale, bool mirrorAcrossAxis)
    {
        var output = new SKBitmap(src.Width, src.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(output);
        canvas.Clear(SKColors.Transparent);
        float cx = src.Width / 2f, cy = src.Height / 2f;
        canvas.Translate(cx, cy);
        canvas.RotateRadians(angle);
        canvas.Scale(1f, (mirrorAcrossAxis ? -1f : 1f) * perpendicularScale);
        canvas.RotateRadians(-angle);
        canvas.Translate(-cx, -cy);
        using var paint = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High };
        canvas.DrawBitmap(src, 0, 0, paint);
        return output;
    }

    private static byte[] Encode(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 95);
        return data.ToArray();
    }
}

public sealed record WeaponSketchViews(
    byte[] Front, byte[] Back, byte[] Left, byte[] Right, byte[] ThreeQuarter, float SourceAxisRadians);
