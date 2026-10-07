using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Paddock.Core.DTOs;
using Paddock.Core.Enums;
using Paddock.Core.Interfaces;
using Paddock.Core.Models;

namespace Paddock.Infrastructure.Ingestion;

public class ChannelIngestionPipelineService : IIngestionPipelineService
{
    private readonly IFileOrganizationService _fileOrgService;
    private readonly IImageProcessingService _imageService;
    private readonly IMetadataService _metadataService;
    private readonly IExcelRepository _excelRepo;
    private readonly IPhotoRenamerService _photoRenamerService;

    private readonly ConcurrentDictionary<Guid, IngestionProgressReport> _activeJobs = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _jobCts = new();

    public event EventHandler<IngestionProgressReport>? JobProgressUpdated;
    public event EventHandler<IngestionProgressReport>? JobCompleted;

    public IReadOnlyDictionary<Guid, IngestionProgressReport> ActiveJobs => _activeJobs;

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png",
        ".cr2", ".cr3", ".nef", ".arw", ".dng", ".raf", ".rw2", ".orf", ".pef"
    };

    private sealed record IngestionFileItem(string SourceFilePath, string RenamedFileName);

    public ChannelIngestionPipelineService(
        IFileOrganizationService fileOrgService,
        IImageProcessingService imageService,
        IMetadataService metadataService,
        IExcelRepository excelRepo,
        IPhotoRenamerService? photoRenamerService = null)
    {
        _fileOrgService = fileOrgService;
        _imageService = imageService;
        _metadataService = metadataService;
        _excelRepo = excelRepo;
        _photoRenamerService = photoRenamerService ?? new Metadata.PhotoRenamerService();
    }

    public Task<Guid> EnqueueJobAsync(IngestionJobRequest request, CancellationToken cancellationToken = default)
    {
        var jobId = request.JobId;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _jobCts[jobId] = cts;

        var sourceLabel = Path.GetFileName(request.SourceDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(sourceLabel)) sourceLabel = request.SourceDirectory;

        var report = new IngestionProgressReport
        {
            JobId = jobId,
            Status = IngestionStatus.Queued,
            SourceName = sourceLabel,
            TargetDescription = $"{request.AtletaTarget.DisplayPettoraleNome} — {request.DisciplinaTarget.NomeDisciplina}",
            StatusMessage = "Scansione file in corso..."
        };

        _activeJobs[jobId] = report;
        JobProgressUpdated?.Invoke(this, report);

        // Avvio background task non bloccante per l'esecuzione della pipeline
        _ = Task.Run(() => RunPipelineAsync(request, cts.Token), CancellationToken.None);

        return Task.FromResult(jobId);
    }

    private async Task RunPipelineAsync(IngestionJobRequest request, CancellationToken token)
    {
        var report = _activeJobs[request.JobId];
        report.Status = IngestionStatus.Running;
        report.StatusMessage = "Analisi cartella sorgente...";
        JobProgressUpdated?.Invoke(this, report);

        var stopwatch = Stopwatch.StartNew();
        var lastReportTime = stopwatch.ElapsedMilliseconds;
        var lastReportBytes = 0L;

        try
        {
            if (!Directory.Exists(request.SourceDirectory))
            {
                throw new DirectoryNotFoundException($"Cartella sorgente non trovata: {request.SourceDirectory}");
            }

            // 1. Scansione file supportati
            var searchOpt = request.RecursiveScan ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var allFiles = Directory.EnumerateFiles(request.SourceDirectory, "*.*", searchOpt)
                .Where(f => SupportedExtensions.Contains(Path.GetExtension(f)))
                .ToList();

            report.TotalFiles = allFiles.Count;
            report.TotalBytes = allFiles.Sum(f =>
            {
                try { return new FileInfo(f).Length; } catch { return 0L; }
            });

            if (report.TotalFiles == 0)
            {
                report.Status = IngestionStatus.Completed;
                report.StatusMessage = "Nessun file immagine trovato nella cartella sorgente.";
                JobCompleted?.Invoke(this, report);
                return;
            }

            // 2. Creazione Channel Producer-Consumer bounded
            var channel = Channel.CreateBounded<IngestionFileItem>(new BoundedChannelOptions(50)
            {
                SingleWriter = true,
                SingleReader = false,
                FullMode = BoundedChannelFullMode.Wait
            });

            // Avvio Producer
            var producerTask = Task.Run(async () =>
            {
                try
                {
                    // Raggruppa i file per coppia (stessa directory sorgente + stesso nome senza estensione)
                    var fileGroups = allFiles
                        .GroupBy(f => Path.Combine(Path.GetDirectoryName(f) ?? string.Empty, Path.GetFileNameWithoutExtension(f)), StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    foreach (var group in fileGroups)
                    {
                        if (token.IsCancellationRequested) break;

                        // Calcola la radice del nome una sola volta per la coppia RAW+JPEG
                        var rootName = _photoRenamerService.ComputeRenamedRoot(group);

                        foreach (var file in group)
                        {
                            if (token.IsCancellationRequested) break;

                            var finalName = _photoRenamerService.GetRenamedFileName(file, rootName);
                            await channel.Writer.WriteAsync(new IngestionFileItem(file, finalName), token).ConfigureAwait(false);
                        }
                    }
                }
                finally
                {
                    channel.Writer.Complete();
                }
            }, token);

            // 3. Pool di Consumer concorrenti (2 worker in parallelo per non saturare l'I/O del lettore SD)
            var workerCount = Math.Clamp(Environment.ProcessorCount / 2, 2, 4);
            var completedPhotos = new ConcurrentBag<Foto>();
            var processedLock = new object();

            var consumerTasks = Enumerable.Range(0, workerCount).Select(workerId => Task.Run(async () =>
            {
                while (await channel.Reader.WaitToReadAsync(token).ConfigureAwait(false))
                {
                    while (channel.Reader.TryRead(out var item))
                    {
                        token.ThrowIfCancellationRequested();

                        try
                        {
                            var ext = Path.GetExtension(item.SourceFilePath);
                            var isRaw = _fileOrgService.IsRawFormat(ext);

                            var rootFolder = !string.IsNullOrWhiteSpace(request.BasePath)
                                ? request.BasePath
                                : request.EventoTarget.CartellaDestinazioneRoot;

                            // Fase A: Copia organizzata con MD5, relativePath puro e nome file ridenominato
                            var (destPath, relPath, md5, sizeBytes) = await _fileOrgService.CopyFileOrganizedAsync(
                                item.SourceFilePath,
                                rootFolder,
                                request.EventoTarget.NomeEvento,
                                request.AtletaTarget,
                                request.DisciplinaTarget,
                                item.RenamedFileName,
                                token).ConfigureAwait(false);

                            // Fase B: Watermark (se abilitato e formato raster)
                            var watermarkApplied = false;
                            if (request.Watermark.Enabled && !isRaw)
                            {
                                await _imageService.ApplyWatermarkAsync(destPath, destPath, request.Watermark, token).ConfigureAwait(false);
                                watermarkApplied = true;
                            }

                            // Fase C: Iniezione Metadati Fotografo & Copyright
                            if (request.Metadata.InjectPhotographer && !string.IsNullOrWhiteSpace(request.Metadata.PhotographerName))
                            {
                                var metaWritten = await _metadataService.WritePhotographerMetadataAsync(
                                    destPath,
                                    request.Metadata.PhotographerName,
                                    request.Metadata.CopyrightNotice,
                                    token).ConfigureAwait(false);

                                if (!metaWritten && isRaw && !_metadataService.IsExifToolAvailable)
                                {
                                    lock (report.Warnings)
                                    {
                                        if (report.Warnings.Count == 0)
                                        {
                                            report.Warnings.Add("ExifTool non trovato: metadati autore omessi sui file RAW proprietari.");
                                        }
                                    }
                                }
                            }

                            // Fase D: Estrazione data scatto
                            var captureDate = await _metadataService.ExtractCaptureDateAsync(destPath, token).ConfigureAwait(false);

                            // Creazione modello Foto con percorso relativo conforme
                            var foto = new Foto
                            {
                                EventoId = request.EventoTarget.Id,
                                AtletaId = request.AtletaTarget.Id,
                                DisciplinaId = request.DisciplinaTarget.Id,
                                NomeFileOriginale = Path.GetFileName(item.SourceFilePath),
                                PathRelativo = relPath,
                                Formato = isRaw ? "RAW" : "JPEG",
                                DataScatto = captureDate,
                                Fotografo = request.Metadata.PhotographerName,
                                WatermarkApplicato = watermarkApplied,
                                DimensioneByte = sizeBytes,
                                HashMd5 = md5
                            };

                            completedPhotos.Add(foto);

                            // Aggiornamento telemetria
                            lock (processedLock)
                            {
                                report.ProcessedFiles++;
                                report.ProcessedBytes += sizeBytes;
                                report.CurrentProcessingFile = item.RenamedFileName;

                                var nowMs = stopwatch.ElapsedMilliseconds;
                                var deltaMs = nowMs - lastReportTime;
                                if (deltaMs >= 250) // Aggiorna metriche ogni 250ms
                                {
                                    var deltaBytes = report.ProcessedBytes - lastReportBytes;
                                    var currentSpeed = deltaBytes / (deltaMs / 1000.0);
                                    
                                    // Filtro passa-basso per smussare il calcolo della velocità
                                    report.SpeedBytesPerSecond = report.SpeedBytesPerSecond == 0 
                                        ? currentSpeed 
                                        : (report.SpeedBytesPerSecond * 0.7) + (currentSpeed * 0.3);

                                    var remainingBytes = Math.Max(0, report.TotalBytes - report.ProcessedBytes);
                                    if (report.SpeedBytesPerSecond > 0)
                                    {
                                        report.EstimatedTimeRemaining = TimeSpan.FromSeconds(remainingBytes / report.SpeedBytesPerSecond);
                                    }

                                    lastReportTime = nowMs;
                                    lastReportBytes = report.ProcessedBytes;

                                    JobProgressUpdated?.Invoke(this, report);
                                }
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            lock (report.Warnings)
                            {
                                report.Warnings.Add($"Errore sul file {Path.GetFileName(item.SourceFilePath)}: {ex.Message}");
                            }
                        }
                    }
                }
            }, token)).ToArray();

            await Task.WhenAll(consumerTasks.Concat(new[] { producerTask })).ConfigureAwait(false);

            // 4. Scrittura Batch Atomica su Excel
            report.StatusMessage = "Sincronizzazione archivio Excel...";
            JobProgressUpdated?.Invoke(this, report);

            if (!completedPhotos.IsEmpty)
            {
                await _excelRepo.AddFotoBatchAsync(completedPhotos, token).ConfigureAwait(false);
            }

            report.Status = IngestionStatus.Completed;
            report.StatusMessage = $"Importazione completata con successo! ({report.ProcessedFiles}/{report.TotalFiles} foto salvate)";
            report.EstimatedTimeRemaining = TimeSpan.Zero;
            JobCompleted?.Invoke(this, report);
        }
        catch (OperationCanceledException)
        {
            report.Status = IngestionStatus.Cancelled;
            report.StatusMessage = "Importazione annullata dall'utente.";
            JobCompleted?.Invoke(this, report);
        }
        catch (Exception ex)
        {
            report.Status = IngestionStatus.Failed;
            report.Exception = ex;
            report.StatusMessage = $"Errore durante l'importazione: {ex.Message}";
            JobCompleted?.Invoke(this, report);
        }
        finally
        {
            _jobCts.TryRemove(request.JobId, out var cts);
            cts?.Dispose();
        }
    }

    public void CancelJob(Guid jobId)
    {
        if (_jobCts.TryGetValue(jobId, out var cts))
        {
            cts.Cancel();
        }
    }

    public void CancelAllJobs()
    {
        foreach (var cts in _jobCts.Values)
        {
            try { cts.Cancel(); } catch { }
        }
    }

    public void Dispose()
    {
        CancelAllJobs();
        foreach (var cts in _jobCts.Values)
        {
            cts.Dispose();
        }
        _jobCts.Clear();
    }
}

