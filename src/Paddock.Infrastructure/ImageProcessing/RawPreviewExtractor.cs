using System.Diagnostics;

namespace Paddock.Infrastructure.ImageProcessing;

/// <summary>
/// Estrattore ad alte prestazioni in-process per anteprime e miniature JPEG incorporate
/// all'interno di file RAW fotografici (Canon CR2/CR3, Nikon NEF, Sony ARW, Adobe DNG, ecc.).
/// </summary>
public static class RawPreviewExtractor
{
    private static readonly HashSet<string> RawExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cr2", ".cr3", ".nef", ".arw", ".dng", ".raf", ".rw2", ".orf", ".pef", ".srw"
    };

    public static bool IsRawFormat(string extensionOrPath)
    {
        if (string.IsNullOrWhiteSpace(extensionOrPath)) return false;
        var ext = Path.GetExtension(extensionOrPath);
        if (string.IsNullOrEmpty(ext))
        {
            ext = extensionOrPath.StartsWith('.') ? extensionOrPath : "." + extensionOrPath;
        }
        return RawExtensions.Contains(ext);
    }

    /// <summary>
    /// Estrae l'anteprima JPEG (preferibilmente ad alta risoluzione) incorporata nel file RAW.
    /// </summary>
    public static async Task<byte[]> ExtractEmbeddedJpegAsync(
        string rawFilePath,
        bool preferLargest = true,
        string? exifToolPath = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawFilePath) || !File.Exists(rawFilePath))
        {
            return Array.Empty<byte>();
        }

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var fs = new FileStream(rawFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024);
                
                // 1. Parsing strutturato IFD per container TIFF (CR2, NEF, ARW, DNG, PEF, ORF)
                var candidates = ExtractJpegCandidatesFromTiff(fs);
                if (candidates.Count > 0)
                {
                    var chosen = preferLargest
                        ? candidates.OrderByDescending(c => c.Length).First()
                        : candidates.OrderBy(c => c.Length).First();

                    var buffer = new byte[chosen.Length];
                    fs.Seek(chosen.Offset, SeekOrigin.Begin);
                    int read = 0;
                    while (read < chosen.Length)
                    {
                        int r = fs.Read(buffer, read, chosen.Length - read);
                        if (r <= 0) break;
                        read += r;
                    }

                    if (read == chosen.Length && IsValidJpegHeader(buffer))
                    {
                        return buffer;
                    }
                }

                // 2. Fast Stream Scanner: scansione marker JPEG (per CR3, RAF o container anomali)
                var scanned = ScanForLargestEmbeddedJpeg(fs, cancellationToken);
                if (scanned != null && scanned.Length > 0)
                {
                    return scanned;
                }
            }
            catch
            {
                // Fallback successivo
            }

            // 3. Fallback ExifTool se disponibile
            if (!string.IsNullOrEmpty(exifToolPath) && File.Exists(exifToolPath))
            {
                try
                {
                    var bytes = ExtractWithExifTool(rawFilePath, exifToolPath, cancellationToken);
                    if (bytes != null && bytes.Length > 0)
                    {
                        return bytes;
                    }
                }
                catch
                {
                    // Ignora errori processo
                }
            }

            return Array.Empty<byte>();
        }, cancellationToken).ConfigureAwait(false);
    }

    private sealed record JpegCandidate(long Offset, int Length);

    private static List<JpegCandidate> ExtractJpegCandidatesFromTiff(Stream stream)
    {
        var results = new List<JpegCandidate>();
        if (stream.Length < 16) return results;

        stream.Seek(0, SeekOrigin.Begin);
        using var reader = new BinaryReader(stream, System.Text.Encoding.Default, leaveOpen: true);

        // Header TIFF
        ushort byteOrder = reader.ReadUInt16();
        bool isLittleEndian = byteOrder == 0x4949; // "II"
        bool isBigEndian = byteOrder == 0x4D4D;    // "MM"

        if (!isLittleEndian && !isBigEndian)
        {
            return results;
        }

        ushort magic = ReadUInt16(reader, isLittleEndian);
        if (magic != 42 && magic != 0x55) // 42 standard TIFF, 0x55 Olympus ORF
        {
            return results;
        }

        uint firstIfdOffset = ReadUInt32(reader, isLittleEndian);
        var visitedIfds = new HashSet<uint>();
        var ifdQueue = new Queue<uint>();

        if (firstIfdOffset > 0 && firstIfdOffset < stream.Length)
        {
            ifdQueue.Enqueue(firstIfdOffset);
        }

        // Esplora la catena di IFD
        while (ifdQueue.Count > 0 && visitedIfds.Count < 20)
        {
            var ifdOffset = ifdQueue.Dequeue();
            if (!visitedIfds.Add(ifdOffset) || ifdOffset >= stream.Length - 2)
            {
                continue;
            }

            stream.Seek(ifdOffset, SeekOrigin.Begin);
            ushort numEntries = ReadUInt16(reader, isLittleEndian);
            if (numEntries == 0 || numEntries > 1000)
            {
                continue;
            }

            long stripOffset = 0;
            int stripLength = 0;
            long jpegInterchangeOffset = 0;
            int jpegInterchangeLength = 0;

            for (int i = 0; i < numEntries; i++)
            {
                if (stream.Position + 12 > stream.Length) break;

                ushort tag = ReadUInt16(reader, isLittleEndian);
                ushort type = ReadUInt16(reader, isLittleEndian);
                uint count = ReadUInt32(reader, isLittleEndian);
                uint valueOrOffset = ReadUInt32(reader, isLittleEndian);

                switch (tag)
                {
                    case 0x0111: // StripOffsets / PreviewImageStart
                        stripOffset = ReadValueOrOffset(reader, stream, type, count, valueOrOffset, isLittleEndian);
                        break;
                    case 0x0117: // StripByteCounts / PreviewImageLength
                        stripLength = (int)ReadValueOrOffset(reader, stream, type, count, valueOrOffset, isLittleEndian);
                        break;
                    case 0x0201: // JPEGInterchangeFormat
                        jpegInterchangeOffset = valueOrOffset;
                        break;
                    case 0x0202: // JPEGInterchangeFormatLength
                        jpegInterchangeLength = (int)valueOrOffset;
                        break;
                    case 0x014A: // SubIFDs
                    case 0x8769: // ExifIFD
                        // Aggiunge sub-IFD alla coda di esplorazione
                        if (count == 1 && valueOrOffset > 0 && valueOrOffset < stream.Length)
                        {
                            ifdQueue.Enqueue(valueOrOffset);
                        }
                        else if (count > 1 && count < 10 && valueOrOffset > 0 && valueOrOffset < stream.Length)
                        {
                            var savePos = stream.Position;
                            stream.Seek(valueOrOffset, SeekOrigin.Begin);
                            for (int sc = 0; sc < count; sc++)
                            {
                                if (stream.Position + 4 <= stream.Length)
                                {
                                    var subOffset = ReadUInt32(reader, isLittleEndian);
                                    if (subOffset > 0 && subOffset < stream.Length)
                                    {
                                        ifdQueue.Enqueue(subOffset);
                                    }
                                }
                            }
                            stream.Seek(savePos, SeekOrigin.Begin);
                        }
                        break;
                }
            }

            // Next IFD Offset
            if (stream.Position + 4 <= stream.Length)
            {
                uint nextIfd = ReadUInt32(reader, isLittleEndian);
                if (nextIfd > 0 && nextIfd < stream.Length)
                {
                    ifdQueue.Enqueue(nextIfd);
                }
            }

            // Verifica StripOffsets
            CheckAndAddCandidate(stream, stripOffset, stripLength, results);

            // Verifica JPEGInterchangeFormat
            CheckAndAddCandidate(stream, jpegInterchangeOffset, jpegInterchangeLength, results);
        }

        return results;
    }

    private static long ReadValueOrOffset(BinaryReader reader, Stream stream, ushort type, uint count, uint valueOrOffset, bool isLittleEndian)
    {
        if (count == 1)
        {
            if (type == 3) // SHORT
            {
                return isLittleEndian ? (valueOrOffset & 0xFFFF) : (valueOrOffset >> 16);
            }
            if (type == 4) // LONG
            {
                return valueOrOffset;
            }
        }
        else if (count > 1 && valueOrOffset > 0 && valueOrOffset < stream.Length)
        {
            // Array di valori: per le anteprime prendiamo il primo elemento
            var savePos = stream.Position;
            try
            {
                stream.Seek(valueOrOffset, SeekOrigin.Begin);
                if (type == 4 && stream.Position + 4 <= stream.Length)
                {
                    return ReadUInt32(reader, isLittleEndian);
                }
                if (type == 3 && stream.Position + 2 <= stream.Length)
                {
                    return ReadUInt16(reader, isLittleEndian);
                }
            }
            finally
            {
                stream.Seek(savePos, SeekOrigin.Begin);
            }
        }

        return valueOrOffset;
    }

    private static void CheckAndAddCandidate(Stream stream, long offset, int length, List<JpegCandidate> results)
    {
        if (offset > 0 && length > 1024 && offset + length <= stream.Length)
        {
            var savePos = stream.Position;
            try
            {
                stream.Seek(offset, SeekOrigin.Begin);
                int b1 = stream.ReadByte();
                int b2 = stream.ReadByte();
                if (b1 == 0xFF && b2 == 0xD8) // SOI marker JPEG valido
                {
                    results.Add(new JpegCandidate(offset, length));
                }
            }
            finally
            {
                stream.Seek(savePos, SeekOrigin.Begin);
            }
        }
    }

    private static byte[]? ScanForLargestEmbeddedJpeg(Stream stream, CancellationToken ct)
    {
        const int chunkSize = 512 * 1024; // 512 KB
        var buffer = new byte[chunkSize];
        long maxScanBytes = Math.Min(stream.Length, 64 * 1024 * 1024); // Scansiona fino a 64 MB
        long streamPos = 0;

        long bestOffset = -1;
        int bestLength = 0;

        stream.Seek(0, SeekOrigin.Begin);

        while (streamPos < maxScanBytes)
        {
            ct.ThrowIfCancellationRequested();

            stream.Seek(streamPos, SeekOrigin.Begin);
            int read = stream.Read(buffer, 0, chunkSize);
            if (read < 4) break;

            for (int i = 0; i < read - 3; i++)
            {
                // Cerca 0xFF 0xD8 0xFF (SOI seguito da APP/DQT marker)
                if (buffer[i] == 0xFF && buffer[i + 1] == 0xD8 && buffer[i + 2] == 0xFF)
                {
                    long jpegStart = streamPos + i;
                    // Cerca il marker di fine immagine 0xFF 0xD9 (EOI)
                    var length = FindJpegLength(stream, jpegStart);
                    if (length > bestLength && length > 1024) // Minimo 1 KB per essere un'anteprima valida
                    {
                        bestOffset = jpegStart;
                        bestLength = length;
                    }
                }
            }

            streamPos += read - 3; // Sovrapposizione di 3 byte per non perdere marker a cavallo del chunk
        }

        if (bestOffset >= 0 && bestLength > 0)
        {
            var result = new byte[bestLength];
            stream.Seek(bestOffset, SeekOrigin.Begin);
            int totalRead = 0;
            while (totalRead < bestLength)
            {
                int r = stream.Read(result, totalRead, bestLength - totalRead);
                if (r <= 0) break;
                totalRead += r;
            }
            if (totalRead == bestLength && IsValidJpegHeader(result))
            {
                return result;
            }
        }

        return null;
    }

    private static int FindJpegLength(Stream stream, long startOffset)
    {
        var savePos = stream.Position;
        try
        {
            stream.Seek(startOffset, SeekOrigin.Begin);
            // Salta SOI
            stream.Seek(startOffset + 2, SeekOrigin.Begin);

            const int searchBlock = 64 * 1024;
            var block = new byte[searchBlock];
            long currentOffset = startOffset + 2;
            long maxSearch = Math.Min(stream.Length, startOffset + 16 * 1024 * 1024); // Max 16 MB per anteprima

            long lastEoi = -1;

            while (currentOffset < maxSearch)
            {
                int read = stream.Read(block, 0, (int)Math.Min(searchBlock, maxSearch - currentOffset));
                if (read < 2) break;

                for (int i = 0; i < read - 1; i++)
                {
                    if (block[i] == 0xFF && block[i + 1] == 0xD9) // EOI
                    {
                        lastEoi = currentOffset + i + 2;
                    }
                }

                currentOffset += read;
            }

            if (lastEoi > startOffset)
            {
                return (int)(lastEoi - startOffset);
            }
        }
        catch
        {
            // Errore seek
        }
        finally
        {
            stream.Seek(savePos, SeekOrigin.Begin);
        }

        return 0;
    }

    private static byte[]? ExtractWithExifTool(string filePath, string exifToolPath, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exifToolPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        // Prova estrazione anteprima PreviewImage, fallback su JpgFromRaw
        psi.ArgumentList.Add("-b");
        psi.ArgumentList.Add("-PreviewImage");
        psi.ArgumentList.Add(filePath);

        using var process = Process.Start(psi);
        if (process == null) return null;

        using var ms = new MemoryStream();
        using var reg = ct.Register(() => { try { process.Kill(); } catch { } });

        process.StandardOutput.BaseStream.CopyTo(ms);
        process.WaitForExit();

        var bytes = ms.ToArray();
        if (bytes.Length > 1024 && IsValidJpegHeader(bytes))
        {
            return bytes;
        }

        return null;
    }

    private static bool IsValidJpegHeader(byte[] buffer)
    {
        return buffer.Length >= 4 && buffer[0] == 0xFF && buffer[1] == 0xD8;
    }

    private static ushort ReadUInt16(BinaryReader reader, bool isLittleEndian)
    {
        var val = reader.ReadUInt16();
        if (BitConverter.IsLittleEndian != isLittleEndian)
        {
            return (ushort)((val >> 8) | (val << 8));
        }
        return val;
    }

    private static uint ReadUInt32(BinaryReader reader, bool isLittleEndian)
    {
        var val = reader.ReadUInt32();
        if (BitConverter.IsLittleEndian != isLittleEndian)
        {
            return ((val & 0x000000FF) << 24) |
                   ((val & 0x0000FF00) << 8) |
                   ((val & 0x00FF0000) >> 8) |
                   ((val & 0xFF000000) >> 24);
        }
        return val;
    }
}
