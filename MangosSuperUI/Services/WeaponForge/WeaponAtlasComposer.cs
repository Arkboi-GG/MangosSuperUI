using SkiaSharp;

namespace MangosSuperUI.Services.WeaponForge;

/// <summary>
/// Composites the four zone fills (blade/guard/grip/pommel) into ONE weapon atlas PNG whose 2×2
/// quadrant layout matches <see cref="WeaponZoneAtlas.Cells"/> exactly. Each quadrant is filled
/// independently — a flat material colour, an uploaded 128×64-style image, or a locally generated
/// texture — and the composed atlas becomes the weapon's single BLP through the same
/// <c>WeaponAssetCompiler</c> path every other route uses. This is the "what fills the box" half of
/// the material-zone workflow; <see cref="WeaponZoneAtlas"/> is the "where does each face land" half.
/// </summary>
public static class WeaponAtlasComposer
{
    public const int AtlasWidth = 128;
    public const int AtlasHeight = 64;

    /// <summary>Compose the four fills into a PNG. Index order is the fixed zone order
    /// (blade, guard, grip, pommel) so it lines up with the projected UVs.</summary>
    public static byte[] ComposePng(IReadOnlyList<WeaponCellFill> fills)
    {
        var info = new SKImageInfo(AtlasWidth, AtlasHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(0x8a, 0x8f, 0x99)); // neutral steel base under everything

        int halfW = AtlasWidth / 2, halfH = AtlasHeight / 2;
        var rects = new[]
        {
            new SKRectI(0, 0, halfW, halfH),          // blade
            new SKRectI(halfW, 0, AtlasWidth, halfH), // guard
            new SKRectI(0, halfH, halfW, AtlasHeight),// grip
            new SKRectI(halfW, halfH, AtlasWidth, AtlasHeight), // pommel
        };

        for (int i = 0; i < rects.Length; i++)
        {
            var fill = i < fills.Count ? fills[i] : null;
            DrawCell(canvas, rects[i], fill);
        }

        canvas.Flush();
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static void DrawCell(SKCanvas canvas, SKRectI rect, WeaponCellFill? fill)
    {
        if (fill is null || fill.Kind == WeaponCellFillKind.Solid)
        {
            var color = fill is null ? new SKColor(0x9a, 0x9f, 0xa8) : ParseColor(fill.ColorHex, 0x9a9fa8);
            using var paint = new SKPaint { Color = color, IsAntialias = false };
            canvas.DrawRect(rect, paint);
            return;
        }

        // Image fill: scale the supplied PNG to fill the quadrant. Front/back share this quadrant by
        // design, so a single supplied texture clothes both faces of the zone.
        if (fill.ImagePng is { Length: > 0 })
        {
            using var bmp = SKBitmap.Decode(fill.ImagePng);
            if (bmp is not null)
            {
                using var paint = new SKPaint { IsAntialias = false, FilterQuality = SKFilterQuality.High };
                var src = SKRect.Create(0, 0, bmp.Width, bmp.Height);
                var dest = SKRect.Create(rect.Left, rect.Top, rect.Width, rect.Height);
                canvas.DrawBitmap(bmp, src, dest, paint);
                return;
            }
        }

        // Image failed to decode — leave the neutral base showing rather than a hole.
    }

    private static SKColor ParseColor(string? hex, int fallback)
    {
        if (!string.IsNullOrWhiteSpace(hex))
        {
            var s = hex.Trim().TrimStart('#');
            if (s.Length == 6 && uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out var v))
                return new SKColor((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        }
        return new SKColor((byte)(fallback >> 16), (byte)(fallback >> 8), (byte)fallback);
    }
}

public enum WeaponCellFillKind { Solid, Image }

/// <summary>One quadrant's fill: a flat colour, or an image (uploaded or locally generated, carried
/// as decoded PNG bytes).</summary>
public sealed class WeaponCellFill
{
    public WeaponCellFillKind Kind { get; init; } = WeaponCellFillKind.Solid;
    public string? ColorHex { get; init; }
    public byte[]? ImagePng { get; init; }
}
