using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
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

    private readonly IMetadataService? _metadataService;

    public ImageSharpProcessingService(IMetadataService? metadataService = null)
    {
        _metadataService = metadataService;
    }

    public async Task<byte[]> ExtractRawPreviewAsync(
        string rawFilePath,
        CancellationToken cancellationToken = default)
    {
        var exifToolPath = _metadataService?.ExifToolPath;
        var bytes = await RawPreviewExtractor.ExtractEmbeddedJpegAsync(rawFilePath, preferLargest: true, exifToolPath, cancellationToken).ConfigureAwait(false);
        if (bytes == null || bytes.Length == 0)
        {
            return Array.Empty<byte>();
        }

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var ms = new MemoryStream(bytes);
                var info = Image.Identify(ms);
                ms.Position = 0;
                int orientation = 1;
                if (info?.Metadata.ExifProfile != null &&
                    info.Metadata.ExifProfile.TryGetValue(ExifTag.Orientation, out var tagVal))
                {
                    orientation = (int)(ushort)tagVal.Value;
                }

                if (orientation > 1 && orientation <= 8)
                {
                    using var image = Image.Load<Rgba32>(ms);
                    image.Mutate(x => x.AutoOrient());
                    using var outMs = new MemoryStream();
                    image.SaveAsJpeg(outMs, new JpegEncoder { Quality = 95 });
                    return outMs.ToArray();
                }
            }
            catch
            {
                // In caso di errore parsing orientamento, restituisce i byte originali
            }

            return bytes;
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> AutoRotateImageAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            return false;
        }

        var ext = Path.GetExtension(imagePath);
        if (!RasterExtensions.Contains(ext))
        {
            // I file RAW proprietari non vengono alterati/ricodificati
            return false;
        }

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 1. Rileva l'orientamento EXIF con MetadataExtractor in-process
            int orientation = 1;
            try
            {
                var directories = ImageMetadataReader.ReadMetadata(imagePath);
                var exifDir = directories.OfType<ExifDirectoryBase>()
                    .FirstOrDefault(d => d.ContainsTag(ExifDirectoryBase.TagOrientation));
                if (exifDir != null && exifDir.TryGetInt32(ExifDirectoryBase.TagOrientation, out var parsedVal))
                {
                    orientation = parsedVal;
                }
            }
            catch
            {
                // Fallback successivo
            }

            // 2. Se non rilevato, ispeziona i metadati con Image.Identify
            if (orientation <= 1)
            {
                try
                {
                    var info = Image.Identify(imagePath);
                    if (info?.Metadata.ExifProfile != null &&
                        info.Metadata.ExifProfile.TryGetValue(ExifTag.Orientation, out var exifVal))
                    {
                        orientation = (int)(ushort)exifVal.Value;
                    }
                }
                catch
                {
                    // Fallback
                }
            }

            // Se l'immagine è già orientata normalmente (1 o sconosciuto), non serve rielaborare
            if (orientation <= 1 || orientation > 8)
            {
                return false;
            }

            // 3. Esegui la rotazione fisica e aggiorna l'orientamento a TopLeft (1)
            using var image = Image.Load<Rgba32>(imagePath);

            // Assicura che l'ExifProfile contenga il valore di orientamento rilevato
            if (image.Metadata.ExifProfile == null)
            {
                image.Metadata.ExifProfile = new ExifProfile();
            }
            image.Metadata.ExifProfile.SetValue(ExifTag.Orientation, (ushort)orientation);

            // AutoOrient ruota/ribalta fisicamente la matrice pixel e reimposta Orientation su TopLeft (1)
            image.Mutate(ctx => ctx.AutoOrient());

            // Salvataggio sul percorso di destinazione mantenendo alta qualità fotografica
            if (string.Equals(ext, ".jpg", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ext, ".jpeg", StringComparison.OrdinalIgnoreCase))
            {
                var jpegEncoder = new JpegEncoder { Quality = 95 };
                image.Save(imagePath, jpegEncoder);
            }
            else
            {
                image.Save(imagePath);
            }

            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

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
            ApplyWatermarkToImage(image, options);

            // Salvataggio atomico tramite file temporaneo per prevenire lock di sovrascrittura in-place
            var destDir = Path.GetDirectoryName(destinationImagePath);
            if (!string.IsNullOrEmpty(destDir))
            {
                System.IO.Directory.CreateDirectory(destDir);
            }

            var tempDest = Path.Combine(destDir ?? Path.GetTempPath(), $".tmp_wm_{Guid.NewGuid()}{ext}");
            try
            {
                if (string.Equals(ext, ".png", StringComparison.OrdinalIgnoreCase))
                {
                    image.Save(tempDest, new PngEncoder());
                }
                else
                {
                    var jpegEncoder = new JpegEncoder { Quality = 92 };
                    image.Save(tempDest, jpegEncoder);
                }

                File.Copy(tempDest, destinationImagePath, overwrite: true);
            }
            finally
            {
                try { if (File.Exists(tempDest)) File.Delete(tempDest); } catch { }
            }

        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<byte[]> GenerateThumbnailAsync(
        string imagePath,
        int maxWidth = 260,
        int maxHeight = 260,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            return Array.Empty<byte>();
        }

        var ext = Path.GetExtension(imagePath);

        // Se è un formato RAW (.CR2, .CR3, .NEF, ecc.), estrae e ridimensiona l'anteprima nativa incorporata
        if (RawPreviewExtractor.IsRawFormat(ext))
        {
            var rawBytes = await ExtractRawPreviewAsync(imagePath, cancellationToken).ConfigureAwait(false);
            if (rawBytes != null && rawBytes.Length > 0)
            {
                return await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var image = Image.Load<Rgba32>(rawBytes);

                    image.Mutate(x =>
                    {
                        x.AutoOrient();
                        x.Resize(new ResizeOptions
                        {
                            Size = new Size(maxWidth, maxHeight),
                            Mode = ResizeMode.Max
                        });
                    });

                    using var ms = new MemoryStream();
                    image.SaveAsJpeg(ms, new JpegEncoder { Quality = 85 });
                    return ms.ToArray();
                }, cancellationToken).ConfigureAwait(false);
            }

            return Array.Empty<byte>();
        }

        if (!RasterExtensions.Contains(ext))
        {
            return Array.Empty<byte>();
        }

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var image = Image.Load<Rgba32>(imagePath);

            image.Mutate(x =>
            {
                x.AutoOrient();
                x.Resize(new ResizeOptions
                {
                    Size = new Size(maxWidth, maxHeight),
                    Mode = ResizeMode.Max
                });
            });

            using var ms = new MemoryStream();
            image.SaveAsJpeg(ms, new JpegEncoder { Quality = 75 });
            return ms.ToArray();
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<byte[]> GenerateWatermarkPreviewJpegAsync(
        string? sampleImagePath,
        WatermarkOptions options,
        int previewWidth = 640,
        int previewHeight = 426,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            Image<Rgba32> image;

            if (!string.IsNullOrWhiteSpace(sampleImagePath) && File.Exists(sampleImagePath))
            {
                var ext = Path.GetExtension(sampleImagePath);
                if (RasterExtensions.Contains(ext))
                {
                    try
                    {
                        image = Image.Load<Rgba32>(sampleImagePath);
                    }
                    catch
                    {
                        image = CreateCanonEos600DTestPattern();
                    }
                }
                else
                {
                    image = CreateCanonEos600DTestPattern();
                }
            }
            else
            {
                image = CreateCanonEos600DTestPattern();
            }

            using (image)
            {
                ApplyWatermarkToImage(image, options);

                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(previewWidth, previewHeight),
                    Mode = ResizeMode.Max
                }));

                using var ms = new MemoryStream();
                image.SaveAsJpeg(ms, new JpegEncoder { Quality = 85 });
                return ms.ToArray();
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private static void ApplyWatermarkToImage(Image<Rgba32> image, WatermarkOptions options)
    {
        if (!options.Enabled || string.IsNullOrWhiteSpace(options.WatermarkImagePath) || !File.Exists(options.WatermarkImagePath))
        {
            return;
        }

        using var watermark = Image.Load<Rgba32>(options.WatermarkImagePath);

        // Calcolo proporzionale del watermark su base risoluzione immagine target (es. 5184px nativi)
        var targetWatermarkWidth = Math.Max(32, (int)(image.Width * Math.Clamp(options.ScalePercent, 0.05f, 0.80f)));
        var scaleRatio = (double)targetWatermarkWidth / watermark.Width;
        var targetWatermarkHeight = Math.Max(32, (int)(watermark.Height * scaleRatio));

        watermark.Mutate(w => w.Resize(new ResizeOptions
        {
            Size = new Size(targetWatermarkWidth, targetWatermarkHeight),
            Mode = ResizeMode.Max
        }));

        var margin = Math.Clamp(options.MarginPixels, 8, 300);
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

    /// <summary>
    /// Genera in memoria una tela fotorealistica con risoluzione standard nativa Canon EOS 600D (5184 x 3456 px, 18.0 MP APS-C, 3:2)
    /// </summary>
    private static Image<Rgba32> CreateCanonEos600DTestPattern()
    {
        const int width = 5184;
        const int height = 3456;
        var image = new Image<Rgba32>(width, height);

        image.ProcessPixelRows(accessor =>
        {
            var thirdW1 = width / 3;
            var thirdW2 = (width * 2) / 3;
            var thirdH1 = height / 3;
            var thirdH2 = (height * 2) / 3;

            for (int y = 0; y < height; y++)
            {
                var row = accessor.GetRowSpan(y);
                float ratio = (float)y / height;
                // Gradiente fotografico blu scuro - asfalto sportivo
                byte r = (byte)(26 + ratio * 28);
                byte g = (byte)(30 + ratio * 26);
                byte b = (byte)(38 + ratio * 18);

                bool isHorizontalThird = Math.Abs(y - thirdH1) <= 3 || Math.Abs(y - thirdH2) <= 3;

                for (int x = 0; x < width; x++)
                {
                    bool isVerticalThird = Math.Abs(x - thirdW1) <= 3 || Math.Abs(x - thirdW2) <= 3;
                    bool isBorder = x < 12 || x >= width - 12 || y < 12 || y >= height - 12;
                    bool isCenterCross = (Math.Abs(x - width / 2) <= 60 && Math.Abs(y - height / 2) <= 3) ||
                                         (Math.Abs(y - height / 2) <= 60 && Math.Abs(x - width / 2) <= 3);

                    if (isBorder || isCenterCross)
                    {
                        row[x] = new Rgba32(255, 140, 50); // Paddock Orange accent
                    }
                    else if (isHorizontalThird || isVerticalThird)
                    {
                        row[x] = new Rgba32(90, 105, 125, 180); // Griglia fotografica terzi
                    }
                    else
                    {
                        row[x] = new Rgba32(r, g, b);
                    }
                }
            }
        });

        return image;
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

