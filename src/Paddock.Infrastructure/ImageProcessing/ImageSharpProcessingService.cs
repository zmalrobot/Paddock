using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Paddock.Core.DTOs;
using Paddock.Core.Enums;
using Paddock.Core.Interfaces;

namespace Paddock.Infrastructure.ImageProcessing;

public class ImageSharpProcessingService : IImageProcessingService
{
    private static readonly HashSet<string> RasterExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".webp"
    };

    public async Task ApplyWatermarkAsync(
        string sourceImagePath,
        string destinationImagePath,
        WatermarkOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!options.Enabled)
        {
            if (!string.Equals(sourceImagePath, destinationImagePath, StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(sourceImagePath, destinationImagePath, overwrite: true);
            }
            return;
        }

        var ext = Path.GetExtension(sourceImagePath);
        if (!RasterExtensions.Contains(ext))
        {
            // I file RAW o non-raster non vengono modificati con watermark raster per prevenire alterazione
            if (!string.Equals(sourceImagePath, destinationImagePath, StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(sourceImagePath, destinationImagePath, overwrite: true);
            }
            return;
        }

        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var image = Image.Load<Rgba32>(sourceImagePath);

            if (!string.IsNullOrWhiteSpace(options.WatermarkImagePath) && File.Exists(options.WatermarkImagePath))
            {
                using var watermark = Image.Load<Rgba32>(options.WatermarkImagePath);

                // Calcolo dimensioni del watermark scalato in base alla dimensione dell'immagine principale
                var targetWatermarkWidth = Math.Max(32, (int)(image.Width * Math.Clamp(options.ScalePercent, 0.05f, 0.80f)));
                var scaleRatio = (double)targetWatermarkWidth / watermark.Width;
                var targetWatermarkHeight = Math.Max(32, (int)(watermark.Height * scaleRatio));

                watermark.Mutate(w => w.Resize(new ResizeOptions
                {
                    Size = new Size(targetWatermarkWidth, targetWatermarkHeight),
                    Mode = ResizeMode.Max
                }));

                var margin = Math.Clamp(options.MarginPixels, 8, 200);
                var opacity = Math.Clamp(options.Opacity, 0.05f, 1.0f);

                if (options.Position == WatermarkPosition.Tiled)
                {
                    var stepX = targetWatermarkWidth + margin * 2;
                    var stepY = targetWatermarkHeight + margin * 2;
                    for (int x = margin; x < image.Width; x += stepX)
                    {
                        for (int y = margin; y < image.Height; y += stepY)
                        {
                            var pt = new Point(x, y);
                            image.Mutate(ctx => ctx.DrawImage(watermark, pt, opacity));
                        }
                    }
                }
                else
                {
                    var location = CalculateWatermarkPoint(image.Width, image.Height, targetWatermarkWidth, targetWatermarkHeight, options.Position, margin);
                    image.Mutate(ctx => ctx.DrawImage(watermark, location, opacity));
                }
            }

            // Salvataggio mantenendo alta qualità JPEG
            var jpegEncoder = new JpegEncoder { Quality = 92 };
            image.Save(destinationImagePath, jpegEncoder);

        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<byte[]> GenerateThumbnailAsync(
        string imagePath,
        int maxWidth = 260,
        int maxHeight = 260,
        CancellationToken cancellationToken = default)
    {
        var ext = Path.GetExtension(imagePath);
        if (!RasterExtensions.Contains(ext))
        {
            // Per i file RAW, restituiamo un array vuoto o un placeholder
            return Array.Empty<byte>();
        }

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var image = Image.Load<Rgba32>(imagePath);

            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(maxWidth, maxHeight),
                Mode = ResizeMode.Max
            }));

            using var ms = new MemoryStream();
            image.SaveAsJpeg(ms, new JpegEncoder { Quality = 75 });
            return ms.ToArray();
        }, cancellationToken).ConfigureAwait(false);
    }

    private static Point CalculateWatermarkPoint(int imgW, int imgH, int wmW, int wmH, WatermarkPosition position, int margin)
    {
        return position switch
        {
            WatermarkPosition.TopLeft => new Point(margin, margin),
            WatermarkPosition.TopRight => new Point(Math.Max(0, imgW - wmW - margin), margin),
            WatermarkPosition.BottomLeft => new Point(margin, Math.Max(0, imgH - wmH - margin)),
            WatermarkPosition.BottomRight => new Point(Math.Max(0, imgW - wmW - margin), Math.Max(0, imgH - wmH - margin)),
            WatermarkPosition.Center => new Point(Math.Max(0, (imgW - wmW) / 2), Math.Max(0, (imgH - wmH) / 2)),
            _ => new Point(Math.Max(0, imgW - wmW - margin), Math.Max(0, imgH - wmH - margin))
        };
    }
}

