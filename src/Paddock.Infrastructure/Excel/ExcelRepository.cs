using System.Globalization;
using System.Text.Json;
using ClosedXML.Excel;
using Polly;
using Polly.Retry;
using Paddock.Core.Interfaces;
using Paddock.Core.Models;
using Paddock.Core.DTOs;

namespace Paddock.Infrastructure.Excel;

public class ExcelRepository : IExcelRepository
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly AsyncRetryPolicy _retryPolicy;

    public string DatabaseFilePath { get; set; }
    public event EventHandler<LockContentionEventArgs>? LockContentionDetected;

    public ExcelRepository(string? databaseFilePath = null)
    {
        if (string.IsNullOrWhiteSpace(databaseFilePath))
        {
            var defaultFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Paddock");
            Directory.CreateDirectory(defaultFolder);
            DatabaseFilePath = Path.Combine(defaultFolder, "Paddock_Database.xlsx");
        }
        else
        {
            DatabaseFilePath = databaseFilePath;
        }

        // Policy Polly per riprovare su IOException (file bloccato da Excel o Google Drive sync)
        _retryPolicy = Policy
            .Handle<IOException>()
            .Or<UnauthorizedAccessException>()
            .WaitAndRetryAsync(
                retryCount: 5,
                sleepDurationProvider: attempt => TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt)),
                onRetry: (exception, timeSpan, retryCount, context) =>
                {
                    LockContentionDetected?.Invoke(this, new LockContentionEventArgs
                    {
                        FilePath = DatabaseFilePath,
                        Message = $"File Excel temporaneamente occupato ({exception.Message}). Nuovo tentativo {retryCount}/5...",
                        AttemptCount = retryCount,
                        IsRetrying = true
                    });
                });
    }

    private async Task<T> ExecuteWithLockAndRetryAsync<T>(Func<Task<T>> action)
    {
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            return await _retryPolicy.ExecuteAsync(action).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task ExecuteWithLockAndRetryAsync(Func<Task> action)
    {
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            await _retryPolicy.ExecuteAsync(action).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task EnsureDatabaseInitializedAsync(CancellationToken cancellationToken = default)
    {
        await ExecuteWithLockAndRetryAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dir = Path.GetDirectoryName(DatabaseFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (!File.Exists(DatabaseFilePath))
            {
                using var workbook = new XLWorkbook();
                CreateSheetEventi(workbook);
                CreateSheetDiscipline(workbook);
                CreateSheetAtleti(workbook);
                CreateSheetFoto(workbook);
                CreateSheetImpostazioni(workbook);
                CreateSheetListinoPrezzi(workbook);
                CreateSheetAcquisti(workbook);

                await Task.Run(() => workbook.SaveAs(DatabaseFilePath), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // Verifica che tutti i fogli necessari esistano
                using var workbook = new XLWorkbook(DatabaseFilePath);
                var modified = false;

                if (!workbook.Worksheets.Contains("Eventi")) { CreateSheetEventi(workbook); modified = true; }
                if (!workbook.Worksheets.Contains("Discipline")) { CreateSheetDiscipline(workbook); modified = true; }
                if (!workbook.Worksheets.Contains("Atleti")) { CreateSheetAtleti(workbook); modified = true; }
                if (!workbook.Worksheets.Contains("Foto")) { CreateSheetFoto(workbook); modified = true; }
                if (!workbook.Worksheets.Contains("Impostazioni")) { CreateSheetImpostazioni(workbook); modified = true; }
                if (!workbook.Worksheets.Contains("ListinoPrezzi")) { CreateSheetListinoPrezzi(workbook); modified = true; }
                if (!workbook.Worksheets.Contains("Acquisti")) { CreateSheetAcquisti(workbook); modified = true; }

                if (modified)
                {
                    await Task.Run(() => workbook.Save(), cancellationToken).ConfigureAwait(false);
                }
            }
        }).ConfigureAwait(false);
    }

    private static void CreateSheetImpostazioni(XLWorkbook wb)
    {
        var ws = wb.Worksheets.Add("Impostazioni");
        ws.Cell(1, 1).Value = "Chiave";
        ws.Cell(1, 2).Value = "Valore";
        ws.Cell(1, 3).Value = "Descrizione";
        FormatHeader(ws, 3);

        // Valori di default
        var defaultPictures = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "Paddock");

        ws.Cell(2, 1).Value = "BasePath";
        ws.Cell(2, 2).Value = defaultPictures;
        ws.Cell(2, 3).Value = "Percorso radice per l'archivio delle foto";

        ws.Cell(3, 1).Value = "VersioneSchema";
        ws.Cell(3, 2).Value = "1.0";
        ws.Cell(3, 3).Value = "Versione dello schema database Excel";
    }

    private static void CreateSheetEventi(XLWorkbook wb)
    {
        var ws = wb.Worksheets.Add("Eventi");
        ws.Cell(1, 1).Value = "Id";
        ws.Cell(1, 2).Value = "NomeEvento";
        ws.Cell(1, 3).Value = "DataInizio";
        ws.Cell(1, 4).Value = "DataFine";
        ws.Cell(1, 5).Value = "Luogo";
        ws.Cell(1, 6).Value = "CartellaDestinazioneRoot";
        ws.Cell(1, 7).Value = "Note";
        FormatHeader(ws, 7);
    }

    private static void CreateSheetDiscipline(XLWorkbook wb)
    {
        var ws = wb.Worksheets.Add("Discipline");
        ws.Cell(1, 1).Value = "Id";
        ws.Cell(1, 2).Value = "EventoId";
        ws.Cell(1, 3).Value = "NomeDisciplina";
        ws.Cell(1, 4).Value = "Descrizione";
        FormatHeader(ws, 4);
    }

    private static void CreateSheetAtleti(XLWorkbook wb)
    {
        var ws = wb.Worksheets.Add("Atleti");
        ws.Cell(1, 1).Value = "Id";
        ws.Cell(1, 2).Value = "EventoId";
        ws.Cell(1, 3).Value = "NumeroPettorale";
        ws.Cell(1, 4).Value = "Nome";
        ws.Cell(1, 5).Value = "Cognome";
        ws.Cell(1, 6).Value = "Categoria";
        ws.Cell(1, 7).Value = "Note";
        FormatHeader(ws, 7);
    }

    private static void CreateSheetFoto(XLWorkbook wb)
    {
        var ws = wb.Worksheets.Add("Foto");
        ws.Cell(1, 1).Value = "Id";
        ws.Cell(1, 2).Value = "EventoId";
        ws.Cell(1, 3).Value = "AtletaId";
        ws.Cell(1, 4).Value = "DisciplinaId";
        ws.Cell(1, 5).Value = "NomeFileOriginale";
        ws.Cell(1, 6).Value = "PathRelativo";
        ws.Cell(1, 7).Value = "Formato";
        ws.Cell(1, 8).Value = "DataScatto";
        ws.Cell(1, 9).Value = "Fotografo";
        ws.Cell(1, 10).Value = "WatermarkApplicato";
        ws.Cell(1, 11).Value = "DimensioneByte";
        ws.Cell(1, 12).Value = "HashMd5";
        ws.Cell(1, 13).Value = "IsPremiazione";
        FormatHeader(ws, 13);
    }

    private static void CreateSheetListinoPrezzi(XLWorkbook wb)
    {
        var ws = wb.Worksheets.Add("ListinoPrezzi");
        ws.Cell(1, 1).Value = "Id";
        ws.Cell(1, 2).Value = "Categoria";
        ws.Cell(1, 3).Value = "Nome";
        ws.Cell(1, 4).Value = "Prezzo";
        ws.Cell(1, 5).Value = "QuantitaFoto";
        ws.Cell(1, 6).Value = "Descrizione";
        FormatHeader(ws, 6);

        var defaultItems = GetDefaultCatalogoItems();
        var row = 2;
        foreach (var item in defaultItems)
        {
            ws.Cell(row, 1).Value = item.Id.ToString();
            ws.Cell(row, 2).Value = item.Categoria.ToString();
            ws.Cell(row, 3).Value = item.Nome;
            ws.Cell(row, 4).Value = (double)item.Prezzo;
            ws.Cell(row, 5).Value = item.QuantitaFotoIncluse;
            ws.Cell(row, 6).Value = item.Descrizione ?? string.Empty;
            row++;
        }
    }

    private static void CreateSheetAcquisti(XLWorkbook wb)
    {
        var ws = wb.Worksheets.Add("Acquisti");
        ws.Cell(1, 1).Value = "Id";
        ws.Cell(1, 2).Value = "EventoId";
        ws.Cell(1, 3).Value = "DataAcquisto";
        ws.Cell(1, 4).Value = "AtletaId";
        ws.Cell(1, 5).Value = "NomeAtleta";
        ws.Cell(1, 6).Value = "NumeroPettorale";
        ws.Cell(1, 7).Value = "DisciplinaId";
        ws.Cell(1, 8).Value = "NomeDisciplina";
        ws.Cell(1, 9).Value = "TotaleQuantita";
        ws.Cell(1, 10).Value = "TotaleCalcolato";
        ws.Cell(1, 11).Value = "TotalePagato";
        ws.Cell(1, 12).Value = "EmailCliente";
        ws.Cell(1, 13).Value = "TelefonoCliente";
        ws.Cell(1, 14).Value = "InteraCartella";
        ws.Cell(1, 15).Value = "FileFotoSelezionate";
        ws.Cell(1, 16).Value = "CartellaPathRiferimento";
        ws.Cell(1, 17).Value = "VociJson";
        ws.Cell(1, 18).Value = "VociSommario";
        ws.Cell(1, 19).Value = "Note";
        ws.Cell(1, 20).Value = "Stato";
        FormatHeader(ws, 20);
    }

    public static List<PrezzoCatalogoItem> GetDefaultCatalogoItems() => new()
    {
        // 1. PACCHETTI GARA (*PER SINGOLA GIORNATA)
        new PrezzoCatalogoItem
        {
            Nome = "1 Percorso",
            Categoria = CategoriaPrezzo.PacchettoGara,
            Prezzo = 15.00m,
            QuantitaFotoIncluse = 1,
            Descrizione = "Foto per singolo percorso (per singola giornata)"
        },
        new PrezzoCatalogoItem
        {
            Nome = "2 Percorsi",
            Categoria = CategoriaPrezzo.PacchettoGara,
            Prezzo = 25.00m,
            QuantitaFotoIncluse = 2,
            Descrizione = "Foto per 2 percorsi (per singola giornata)"
        },
        new PrezzoCatalogoItem
        {
            Nome = "Giornata intera (Tutti i percorsi)",
            Categoria = CategoriaPrezzo.PacchettoGara,
            Prezzo = 35.00m,
            QuantitaFotoIncluse = 999,
            Descrizione = "Tutti i percorsi della giornata per singolo atleta"
        },

        // 2. FOTO SINGOLE (*PER SINGOLA GIORNATA)
        new PrezzoCatalogoItem
        {
            Nome = "1 Foto",
            Categoria = CategoriaPrezzo.FotoSingola,
            Prezzo = 5.00m,
            QuantitaFotoIncluse = 1,
            Descrizione = "Singola foto digitale ad alta risoluzione (per singola giornata)"
        },
        new PrezzoCatalogoItem
        {
            Nome = "3 Foto",
            Categoria = CategoriaPrezzo.PacchettoFoto,
            Prezzo = 12.00m,
            QuantitaFotoIncluse = 3,
            Descrizione = "Pacchetto 3 foto digitali (4.00€/foto, per singola giornata)"
        },
        new PrezzoCatalogoItem
        {
            Nome = "5 Foto",
            Categoria = CategoriaPrezzo.PacchettoFoto,
            Prezzo = 18.00m,
            QuantitaFotoIncluse = 5,
            Descrizione = "Pacchetto 5 foto digitali (3.60€/foto, per singola giornata)"
        },
        new PrezzoCatalogoItem
        {
            Nome = "10 Foto",
            Categoria = CategoriaPrezzo.PacchettoFoto,
            Prezzo = 30.00m,
            QuantitaFotoIncluse = 10,
            Descrizione = "Pacchetto 10 foto digitali (3.00€/foto, per singola giornata)"
        },

        // 3. SERVIZI EXTRA EDITING
        new PrezzoCatalogoItem
        {
            Nome = "Editing base (Luci e colore)",
            Categoria = CategoriaPrezzo.EditingBase,
            Prezzo = 3.00m,
            QuantitaFotoIncluse = 1,
            Descrizione = "Ottimizzazione luci, colore e contrasto professionale (+3€/foto)"
        },
        new PrezzoCatalogoItem
        {
            Nome = "Editing avanzato (rimozione oggetti)",
            Categoria = CategoriaPrezzo.EditingAvanzato,
            Prezzo = 6.00m,
            QuantitaFotoIncluse = 1,
            Descrizione = "Ritocco avanzato e rimozione elementi indesiderati (+6€/foto)"
        }
    };

    private static void FormatHeader(IXLWorksheet ws, int columnsCount)
    {
        var headerRange = ws.Range(1, 1, 1, columnsCount);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Font.FontColor = XLColor.White;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromArgb(26, 28, 32); // Darkroom slate
        ws.Columns(1, columnsCount).AdjustToContents();
    }

    #region Impostazioni & Database Switch

    public async Task SwitchDatabaseAsync(string newFilePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newFilePath))
            throw new ArgumentException("Il percorso del database non può essere vuoto", nameof(newFilePath));

        DatabaseFilePath = newFilePath;
        await EnsureDatabaseInitializedAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default)
    {
        await EnsureDatabaseInitializedAsync(cancellationToken).ConfigureAwait(false);

        return await ExecuteWithLockAndRetryAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await Task.Run(() =>
            {
                using var wb = new XLWorkbook(DatabaseFilePath);
                if (!wb.Worksheets.Contains("Impostazioni")) return null;
                var ws = wb.Worksheet("Impostazioni");
                var rows = ws.RangeUsed()?.RowsUsed().Skip(1);
                if (rows == null) return null;

                foreach (var row in rows)
                {
                    if (string.Equals(row.Cell(1).GetString().Trim(), key, StringComparison.OrdinalIgnoreCase))
                    {
                        return row.Cell(2).GetString();
                    }
                }
                return null;
            }, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public async Task SetSettingAsync(string key, string value, string? description = null, CancellationToken cancellationToken = default)
    {
        await EnsureDatabaseInitializedAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteWithLockAndRetryAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Run(() =>
            {
                using var wb = new XLWorkbook(DatabaseFilePath);
                var ws = wb.Worksheets.Contains("Impostazioni") ? wb.Worksheet("Impostazioni") : wb.Worksheets.Add("Impostazioni");
                var rows = ws.RangeUsed()?.RowsUsed().Skip(1).ToList();
                var existingRow = rows?.FirstOrDefault(r => string.Equals(r.Cell(1).GetString().Trim(), key, StringComparison.OrdinalIgnoreCase));

                if (existingRow != null)
                {
                    existingRow.Cell(2).Value = value;
                    if (!string.IsNullOrWhiteSpace(description))
                    {
                        existingRow.Cell(3).Value = description;
                    }
                }
                else
                {
                    var nextRow = (ws.LastRowUsed()?.RowNumber() ?? 1) + 1;
                    ws.Cell(nextRow, 1).Value = key;
                    ws.Cell(nextRow, 2).Value = value;
                    ws.Cell(nextRow, 3).Value = description ?? string.Empty;
                }

                wb.Save();
            }, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public async Task<string?> GetBasePathAsync(CancellationToken cancellationToken = default)
    {
        return await GetSettingAsync("BasePath", cancellationToken).ConfigureAwait(false);
    }

    public async Task SetBasePathAsync(string newBasePath, CancellationToken cancellationToken = default)
    {
        await SetSettingAsync("BasePath", newBasePath, "Percorso radice per l'archivio delle foto", cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAllEventRootsAsync(string newBasePath, CancellationToken cancellationToken = default)
    {
        await EnsureDatabaseInitializedAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteWithLockAndRetryAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Run(() =>
            {
                using var wb = new XLWorkbook(DatabaseFilePath);

                // 1. Aggiorna valore nel foglio Impostazioni
                var wsSettings = wb.Worksheets.Contains("Impostazioni") ? wb.Worksheet("Impostazioni") : wb.Worksheets.Add("Impostazioni");
                var rowsSettings = wsSettings.RangeUsed()?.RowsUsed().Skip(1);
                var basePathRow = rowsSettings?.FirstOrDefault(r => string.Equals(r.Cell(1).GetString().Trim(), "BasePath", StringComparison.OrdinalIgnoreCase));
                if (basePathRow != null)
                {
                    basePathRow.Cell(2).Value = newBasePath;
                }
                else
                {
                    var nextRow = (wsSettings.LastRowUsed()?.RowNumber() ?? 1) + 1;
                    wsSettings.Cell(nextRow, 1).Value = "BasePath";
                    wsSettings.Cell(nextRow, 2).Value = newBasePath;
                    wsSettings.Cell(nextRow, 3).Value = "Percorso radice per l'archivio delle foto";
                }

                // 2. Aggiorna CartellaDestinazioneRoot su tutti gli eventi esistenti nel foglio Eventi
                if (wb.Worksheets.Contains("Eventi"))
                {
                    var wsEventi = wb.Worksheet("Eventi");
                    var eventRows = wsEventi.RangeUsed()?.RowsUsed().Skip(1);
                    if (eventRows != null)
                    {
                        foreach (var row in eventRows)
                        {
                            row.Cell(6).Value = newBasePath;
                        }
                    }
                }

                wb.Save();
            }, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    #endregion

    #region Eventi CRUD

    public async Task<List<Evento>> GetEventiAsync(CancellationToken cancellationToken = default)
    {
        await EnsureDatabaseInitializedAsync(cancellationToken).ConfigureAwait(false);

        return await ExecuteWithLockAndRetryAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var list = new List<Evento>();

            await Task.Run(() =>
            {
                using var wb = new XLWorkbook(DatabaseFilePath);
                var ws = wb.Worksheet("Eventi");
                var rows = ws.RangeUsed()?.RowsUsed().Skip(1);
                if (rows == null) return;

                foreach (var r in rows)
                {
                    var idStr = r.Cell(1).GetString();
                    if (!Guid.TryParse(idStr, out var id)) continue;

                    list.Add(new Evento
                    {
                        Id = id,
                        NomeEvento = r.Cell(2).GetString(),
                        DataInizio = ParseDateTime(r.Cell(3).GetString()),
                        DataFine = ParseDateTime(r.Cell(4).GetString()),
                        Luogo = r.Cell(5).GetString(),
                        CartellaDestinazioneRoot = r.Cell(6).GetString(),
                        Note = r.Cell(7).GetString()
                    });
                }

                // Calcolo metriche aggregate da Atleti, Discipline e Foto
                if (wb.Worksheets.Contains("Atleti"))
                {
                    var atletiWs = wb.Worksheet("Atleti");
                    var atletiRows = atletiWs.RangeUsed()?.RowsUsed().Skip(1);
                    if (atletiRows != null)
                    {
                        var atletiByEvent = atletiRows
                            .Select(ar => ar.Cell(2).GetString())
                            .Where(e => Guid.TryParse(e, out _))
                            .GroupBy(e => Guid.Parse(e))
                            .ToDictionary(g => g.Key, g => g.Count());

                        foreach (var ev in list)
                        {
                            if (atletiByEvent.TryGetValue(ev.Id, out var count))
                                ev.TotaleAtleti = count;
                        }
                    }
                }

                if (wb.Worksheets.Contains("Discipline"))
                {
                    var discWs = wb.Worksheet("Discipline");
                    var discRows = discWs.RangeUsed()?.RowsUsed().Skip(1);
                    if (discRows != null)
                    {
                        var discByEvent = discRows
                            .Select(dr => dr.Cell(2).GetString())
                            .Where(e => Guid.TryParse(e, out _))
                            .GroupBy(e => Guid.Parse(e))
                            .ToDictionary(g => g.Key, g => g.Count());

                        foreach (var ev in list)
                        {
                            if (discByEvent.TryGetValue(ev.Id, out var count))
                                ev.TotaleDiscipline = count;
                        }
                    }
                }

                if (wb.Worksheets.Contains("Foto"))
                {
                    var fotoWs = wb.Worksheet("Foto");
                    var fotoRows = fotoWs.RangeUsed()?.RowsUsed().Skip(1);
                    if (fotoRows != null)
                    {
                        var photoAgg = fotoRows
                            .Select(fr => new
                            {
                                EventoIdStr = fr.Cell(2).GetString(),
                                SizeBytes = long.TryParse(fr.Cell(11).GetString(), out var s) ? s : 0
                            })
                            .Where(x => Guid.TryParse(x.EventoIdStr, out _))
                            .GroupBy(x => Guid.Parse(x.EventoIdStr))
                            .ToDictionary(g => g.Key, g => new { Count = g.Count(), TotalBytes = g.Sum(x => x.SizeBytes) });

                        foreach (var ev in list)
                        {
                            if (photoAgg.TryGetValue(ev.Id, out var agg))
                            {
                                ev.TotaleFoto = agg.Count;
                                ev.TotaleByteOccupati = agg.TotalBytes;
                            }
                        }
                    }
                }
            }, cancellationToken).ConfigureAwait(false);

            return list;
        }).ConfigureAwait(false);
    }

    public async Task<Evento?> GetEventoByIdAsync(Guid eventoId, CancellationToken cancellationToken = default)
    {
        var all = await GetEventiAsync(cancellationToken).ConfigureAwait(false);
        return all.FirstOrDefault(e => e.Id == eventoId);
    }

    public async Task UpsertEventoAsync(Evento evento, CancellationToken cancellationToken = default)
    {
        await EnsureDatabaseInitializedAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteWithLockAndRetryAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Run(() =>
            {
                using var wb = new XLWorkbook(DatabaseFilePath);
                var ws = wb.Worksheet("Eventi");
                var rows = ws.RangeUsed()?.RowsUsed().Skip(1);
                var existingRow = rows?.FirstOrDefault(r => r.Cell(1).GetString() == evento.Id.ToString());
                var targetRow = existingRow != null 
                    ? ws.Row(existingRow.RowNumber()) 
                    : ws.Row((ws.LastRowUsed()?.RowNumber() ?? 1) + 1);

                targetRow.Cell(1).Value = evento.Id.ToString();
                targetRow.Cell(2).Value = evento.NomeEvento;
                targetRow.Cell(3).Value = evento.DataInizio.ToString("yyyy-MM-dd");
                targetRow.Cell(4).Value = evento.DataFine.ToString("yyyy-MM-dd");
                targetRow.Cell(5).Value = evento.Luogo;
                targetRow.Cell(6).Value = evento.CartellaDestinazioneRoot;
                targetRow.Cell(7).Value = evento.Note ?? string.Empty;

                wb.Save();
            }, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public async Task DeleteEventoAsync(Guid eventoId, CancellationToken cancellationToken = default)
    {
        await EnsureDatabaseInitializedAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteWithLockAndRetryAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Run(() =>
            {
                using var wb = new XLWorkbook(DatabaseFilePath);
                var evIdStr = eventoId.ToString();

                // 1. Elimina da Eventi
                var evWs = wb.Worksheet("Eventi");
                var evRow = evWs.RangeUsed()?.RowsUsed().Skip(1).FirstOrDefault(r => r.Cell(1).GetString() == evIdStr);
                evRow?.Delete();

                // 2. Elimina discipline collegate
                if (wb.Worksheets.Contains("Discipline"))
                {
                    var discWs = wb.Worksheet("Discipline");
                    var discRows = discWs.RangeUsed()?.RowsUsed().Skip(1)
                        .Where(r => r.Cell(2).GetString() == evIdStr).ToList();
                    discRows?.ForEach(r => r.Delete());
                }

                // 3. Elimina atleti collegati
                if (wb.Worksheets.Contains("Atleti"))
                {
                    var atletiWs = wb.Worksheet("Atleti");
                    var atletiRows = atletiWs.RangeUsed()?.RowsUsed().Skip(1)
                        .Where(r => r.Cell(2).GetString() == evIdStr).ToList();
                    atletiRows?.ForEach(r => r.Delete());
                }

                // 4. Elimina foto collegate
                if (wb.Worksheets.Contains("Foto"))
                {
                    var fotoWs = wb.Worksheet("Foto");
                    var fotoRows = fotoWs.RangeUsed()?.RowsUsed().Skip(1)
                        .Where(r => r.Cell(2).GetString() == evIdStr).ToList();
                    fotoRows?.ForEach(r => r.Delete());
                }

                wb.Save();
            }, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    #endregion

    #region Discipline CRUD

    public async Task<List<Disciplina>> GetDisciplineByEventoAsync(Guid eventoId, CancellationToken cancellationToken = default)
    {
        await EnsureDatabaseInitializedAsync(cancellationToken).ConfigureAwait(false);

        return await ExecuteWithLockAndRetryAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var list = new List<Disciplina>();
            var evIdStr = eventoId.ToString();

            await Task.Run(() =>
            {
                using var wb = new XLWorkbook(DatabaseFilePath);
                var ws = wb.Worksheet("Discipline");
                var rows = ws.RangeUsed()?.RowsUsed().Skip(1);
                if (rows == null) return;

                foreach (var r in rows)
                {
                    if (r.Cell(2).GetString() != evIdStr) continue;
                    if (!Guid.TryParse(r.Cell(1).GetString(), out var id)) continue;

                    list.Add(new Disciplina
                    {
                        Id = id,
                        EventoId = eventoId,
                        NomeDisciplina = r.Cell(3).GetString(),
                        Descrizione = r.Cell(4).GetString()
                    });
                }
            }, cancellationToken).ConfigureAwait(false);

            return list;
        }).ConfigureAwait(false);
    }

    public async Task UpsertDisciplinaAsync(Disciplina disciplina, CancellationToken cancellationToken = default)
    {
        await EnsureDatabaseInitializedAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteWithLockAndRetryAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Run(() =>
            {
                using var wb = new XLWorkbook(DatabaseFilePath);
                var ws = wb.Worksheet("Discipline");
                var rows = ws.RangeUsed()?.RowsUsed().Skip(1);
                var existingRow = rows?.FirstOrDefault(r => r.Cell(1).GetString() == disciplina.Id.ToString());
                var targetRow = existingRow != null
                    ? ws.Row(existingRow.RowNumber())
                    : ws.Row((ws.LastRowUsed()?.RowNumber() ?? 1) + 1);

                targetRow.Cell(1).Value = disciplina.Id.ToString();
                targetRow.Cell(2).Value = disciplina.EventoId.ToString();
                targetRow.Cell(3).Value = disciplina.NomeDisciplina;
                targetRow.Cell(4).Value = disciplina.Descrizione ?? string.Empty;

                wb.Save();
            }, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public async Task DeleteDisciplinaAsync(Guid disciplinaId, CancellationToken cancellationToken = default)
    {
        await EnsureDatabaseInitializedAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteWithLockAndRetryAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Run(() =>
            {
                using var wb = new XLWorkbook(DatabaseFilePath);
                var ws = wb.Worksheet("Discipline");
                var row = ws.RangeUsed()?.RowsUsed().Skip(1)
                    .FirstOrDefault(r => r.Cell(1).GetString() == disciplinaId.ToString());
                row?.Delete();
                wb.Save();
            }, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    #endregion

    #region Atleti CRUD

    public async Task<List<Atleta>> GetAtletiByEventoAsync(Guid eventoId, CancellationToken cancellationToken = default)
    {
        await EnsureDatabaseInitializedAsync(cancellationToken).ConfigureAwait(false);

        return await ExecuteWithLockAndRetryAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var list = new List<Atleta>();
            var evIdStr = eventoId.ToString();

            await Task.Run(() =>
            {
                using var wb = new XLWorkbook(DatabaseFilePath);
                var ws = wb.Worksheet("Atleti");
                var rows = ws.RangeUsed()?.RowsUsed().Skip(1);
                if (rows == null) return;

                foreach (var r in rows)
                {
                    if (r.Cell(2).GetString() != evIdStr) continue;
                    if (!Guid.TryParse(r.Cell(1).GetString(), out var id)) continue;

                    list.Add(new Atleta
                    {
                        Id = id,
                        EventoId = eventoId,
                        NumeroPettorale = r.Cell(3).GetString(),
                        Nome = r.Cell(4).GetString(),
                        Cognome = r.Cell(5).GetString(),
                        Categoria = r.Cell(6).GetString(),
                        Note = r.Cell(7).GetString()
                    });
                }
            }, cancellationToken).ConfigureAwait(false);

            return list;
        }).ConfigureAwait(false);
    }

    public async Task UpsertAtletaAsync(Atleta atleta, CancellationToken cancellationToken = default)
    {
        await EnsureDatabaseInitializedAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteWithLockAndRetryAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Run(() =>
            {
                using var wb = new XLWorkbook(DatabaseFilePath);
                var ws = wb.Worksheet("Atleti");
                var rows = ws.RangeUsed()?.RowsUsed().Skip(1);
                var existingRow = rows?.FirstOrDefault(r => r.Cell(1).GetString() == atleta.Id.ToString());
                var targetRow = existingRow != null
                    ? ws.Row(existingRow.RowNumber())
                    : ws.Row((ws.LastRowUsed()?.RowNumber() ?? 1) + 1);

                targetRow.Cell(1).Value = atleta.Id.ToString();
                targetRow.Cell(2).Value = atleta.EventoId.ToString();
                targetRow.Cell(3).Value = atleta.NumeroPettorale;
                targetRow.Cell(4).Value = atleta.Nome;
                targetRow.Cell(5).Value = atleta.Cognome;
                targetRow.Cell(6).Value = atleta.Categoria ?? string.Empty;
                targetRow.Cell(7).Value = atleta.Note ?? string.Empty;

                wb.Save();
            }, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public async Task DeleteAtletaAsync(Guid atletaId, CancellationToken cancellationToken = default)
    {
        await EnsureDatabaseInitializedAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteWithLockAndRetryAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Run(() =>
            {
                using var wb = new XLWorkbook(DatabaseFilePath);
                var ws = wb.Worksheet("Atleti");
                var row = ws.RangeUsed()?.RowsUsed().Skip(1)
                    .FirstOrDefault(r => r.Cell(1).GetString() == atletaId.ToString());
                row?.Delete();
                wb.Save();
            }, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    #endregion

    #region Foto Batch & Operations

    public async Task<List<Foto>> GetFotoByEventoAsync(Guid eventoId, CancellationToken cancellationToken = default)
    {
        await EnsureDatabaseInitializedAsync(cancellationToken).ConfigureAwait(false);

        return await ExecuteWithLockAndRetryAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var list = new List<Foto>();
            var evIdStr = eventoId.ToString();

            await Task.Run(() =>
            {
                using var wb = new XLWorkbook(DatabaseFilePath);
                var ws = wb.Worksheet("Foto");
                var rows = ws.RangeUsed()?.RowsUsed().Skip(1);
                if (rows == null) return;

                foreach (var r in rows)
                {
                    if (r.Cell(2).GetString() != evIdStr) continue;
                    if (!Guid.TryParse(r.Cell(1).GetString(), out var id)) continue;

                    var aid = Guid.TryParse(r.Cell(3).GetString(), out var parsedAid) ? parsedAid : Guid.Empty;
                    var relPath = r.Cell(6).GetString();
                    var isPrem = bool.TryParse(r.Cell(13).GetString(), out var p)
                        ? p
                        : (relPath.Replace('/', '\\').Contains(@"\Premiazioni\") || relPath.StartsWith("Premiazioni", StringComparison.OrdinalIgnoreCase));

                    list.Add(new Foto
                    {
                        Id = id,
                        EventoId = eventoId,
                        AtletaId = aid,
                        DisciplinaId = Guid.TryParse(r.Cell(4).GetString(), out var did) ? did : Guid.Empty,
                        NomeFileOriginale = r.Cell(5).GetString(),
                        PathRelativo = relPath,
                        Formato = r.Cell(7).GetString(),
                        DataScatto = ParseNullableDateTime(r.Cell(8).GetString()),
                        Fotografo = r.Cell(9).GetString(),
                        WatermarkApplicato = bool.TryParse(r.Cell(10).GetString(), out var w) && w,
                        DimensioneByte = long.TryParse(r.Cell(11).GetString(), out var s) ? s : 0,
                        HashMd5 = r.Cell(12).GetString(),
                        IsPremiazione = isPrem
                    });
                }
            }, cancellationToken).ConfigureAwait(false);

            return list;
        }).ConfigureAwait(false);
    }

    public async Task<List<Foto>> GetFotoByAtletaAsync(Guid atletaId, CancellationToken cancellationToken = default)
    {
        await EnsureDatabaseInitializedAsync(cancellationToken).ConfigureAwait(false);

        return await ExecuteWithLockAndRetryAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var list = new List<Foto>();
            var aIdStr = atletaId.ToString();

            await Task.Run(() =>
            {
                using var wb = new XLWorkbook(DatabaseFilePath);
                var ws = wb.Worksheet("Foto");
                var rows = ws.RangeUsed()?.RowsUsed().Skip(1);
                if (rows == null) return;

                foreach (var r in rows)
                {
                    if (r.Cell(3).GetString() != aIdStr) continue;
                    if (!Guid.TryParse(r.Cell(1).GetString(), out var id)) continue;

                    list.Add(new Foto
                    {
                        Id = id,
                        EventoId = Guid.TryParse(r.Cell(2).GetString(), out var eid) ? eid : Guid.Empty,
                        AtletaId = atletaId,
                        DisciplinaId = Guid.TryParse(r.Cell(4).GetString(), out var did) ? did : Guid.Empty,
                        NomeFileOriginale = r.Cell(5).GetString(),
                        PathRelativo = r.Cell(6).GetString(),
                        Formato = r.Cell(7).GetString(),
                        DataScatto = ParseNullableDateTime(r.Cell(8).GetString()),
                        Fotografo = r.Cell(9).GetString(),
                        WatermarkApplicato = bool.TryParse(r.Cell(10).GetString(), out var w) && w,
                        DimensioneByte = long.TryParse(r.Cell(11).GetString(), out var s) ? s : 0,
                        HashMd5 = r.Cell(12).GetString()
                    });
                }
            }, cancellationToken).ConfigureAwait(false);

            return list;
        }).ConfigureAwait(false);
    }

    public async Task AddFotoBatchAsync(IEnumerable<Foto> fotoList, CancellationToken cancellationToken = default)
    {
        var items = fotoList.ToList();
        if (items.Count == 0) return;

        await EnsureDatabaseInitializedAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteWithLockAndRetryAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Run(() =>
            {
                using var wb = new XLWorkbook(DatabaseFilePath);
                var ws = wb.Worksheet("Foto");
                var currentRow = (ws.LastRowUsed()?.RowNumber() ?? 1) + 1;

                foreach (var f in items)
                {
                    ws.Cell(currentRow, 1).Value = f.Id.ToString();
                    ws.Cell(currentRow, 2).Value = f.EventoId.ToString();
                    ws.Cell(currentRow, 3).Value = f.AtletaId.ToString();
                    ws.Cell(currentRow, 4).Value = f.DisciplinaId.ToString();
                    ws.Cell(currentRow, 5).Value = f.NomeFileOriginale;
                    ws.Cell(currentRow, 6).Value = f.PathRelativo;
                    ws.Cell(currentRow, 7).Value = f.Formato;
                    ws.Cell(currentRow, 8).Value = f.DataScatto?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty;
                    ws.Cell(currentRow, 9).Value = f.Fotografo ?? string.Empty;
                    ws.Cell(currentRow, 10).Value = f.WatermarkApplicato;
                    ws.Cell(currentRow, 11).Value = f.DimensioneByte;
                    ws.Cell(currentRow, 12).Value = f.HashMd5 ?? string.Empty;
                    ws.Cell(currentRow, 13).Value = f.IsPremiazione;

                    currentRow++;
                }

                wb.Save();
            }, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public async Task UpdateFotoAsync(Foto foto, CancellationToken cancellationToken = default)
    {
        await EnsureDatabaseInitializedAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteWithLockAndRetryAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Run(() =>
            {
                using var wb = new XLWorkbook(DatabaseFilePath);
                var ws = wb.Worksheet("Foto");
                var row = ws.RangeUsed()?.RowsUsed().Skip(1)
                    .FirstOrDefault(r => r.Cell(1).GetString() == foto.Id.ToString());
                if (row != null)
                {
                    row.Cell(9).Value = foto.Fotografo ?? string.Empty;
                    row.Cell(10).Value = foto.WatermarkApplicato;
                    row.Cell(11).Value = foto.DimensioneByte;
                    row.Cell(12).Value = foto.HashMd5 ?? string.Empty;
                    wb.Save();
                }
            }, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public async Task DeleteFotoAsync(Guid fotoId, CancellationToken cancellationToken = default)
    {
        await EnsureDatabaseInitializedAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteWithLockAndRetryAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Run(() =>
            {
                using var wb = new XLWorkbook(DatabaseFilePath);
                var ws = wb.Worksheet("Foto");
                var row = ws.RangeUsed()?.RowsUsed().Skip(1)
                    .FirstOrDefault(r => r.Cell(1).GetString() == fotoId.ToString());
                row?.Delete();
                wb.Save();
            }, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    #endregion

    #region Catalogo Prezzi

    public async Task<List<PrezzoCatalogoItem>> GetCatalogoPrezziAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithLockAndRetryAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await Task.Run(() =>
            {
                using var wb = new XLWorkbook(DatabaseFilePath);
                if (!wb.Worksheets.Contains("ListinoPrezzi"))
                {
                    return GetDefaultCatalogoItems();
                }

                var ws = wb.Worksheet("ListinoPrezzi");
                var rows = ws.RangeUsed()?.RowsUsed().Skip(1);
                if (rows == null) return GetDefaultCatalogoItems();

                var list = new List<PrezzoCatalogoItem>();
                foreach (var row in rows)
                {
                    var idStr = row.Cell(1).GetString();
                    if (!Guid.TryParse(idStr, out var id)) continue;

                    var catStr = row.Cell(2).GetString();
                    if (!Enum.TryParse<CategoriaPrezzo>(catStr, true, out var cat))
                    {
                        cat = CategoriaPrezzo.FotoSingola;
                    }

                    var nome = row.Cell(3).GetString();
                    var prezzo = (decimal)row.Cell(4).GetDouble();
                    var qta = (int)row.Cell(5).GetDouble();
                    var desc = row.Cell(6).GetString();

                    list.Add(new PrezzoCatalogoItem
                    {
                        Id = id,
                        Categoria = cat,
                        Nome = nome,
                        Prezzo = prezzo,
                        QuantitaFotoIncluse = qta > 0 ? qta : 1,
                        Descrizione = desc
                    });
                }

                return list.Count > 0 ? list : GetDefaultCatalogoItems();
            }, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public async Task SaveCatalogoPrezziAsync(IEnumerable<PrezzoCatalogoItem> items, CancellationToken cancellationToken = default)
    {
        await ExecuteWithLockAndRetryAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Run(() =>
            {
                using var wb = new XLWorkbook(DatabaseFilePath);
                var ws = wb.Worksheets.Contains("ListinoPrezzi")
                    ? wb.Worksheet("ListinoPrezzi")
                    : wb.Worksheets.Add("ListinoPrezzi");

                var usedRows = ws.RangeUsed()?.RowsUsed().Skip(1).ToList();
                if (usedRows != null)
                {
                    foreach (var r in usedRows) r.Delete();
                }

                var row = 2;
                foreach (var item in items)
                {
                    ws.Cell(row, 1).Value = item.Id.ToString();
                    ws.Cell(row, 2).Value = item.Categoria.ToString();
                    ws.Cell(row, 3).Value = item.Nome;
                    ws.Cell(row, 4).Value = (double)item.Prezzo;
                    ws.Cell(row, 5).Value = item.QuantitaFotoIncluse;
                    ws.Cell(row, 6).Value = item.Descrizione ?? string.Empty;
                    row++;
                }

                FormatHeader(ws, 6);
                wb.Save();
            }, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public async Task UpsertPrezzoCatalogoItemAsync(PrezzoCatalogoItem item, CancellationToken cancellationToken = default)
    {
        await ExecuteWithLockAndRetryAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Run(() =>
            {
                using var wb = new XLWorkbook(DatabaseFilePath);
                var ws = wb.Worksheets.Contains("ListinoPrezzi")
                    ? wb.Worksheet("ListinoPrezzi")
                    : wb.Worksheets.Add("ListinoPrezzi");

                var existingRow = ws.RangeUsed()?.RowsUsed().Skip(1)
                    .FirstOrDefault(r => r.Cell(1).GetString() == item.Id.ToString());

                var targetRow = existingRow != null
                    ? ws.Row(existingRow.RowNumber())
                    : ws.Row((ws.LastRowUsed()?.RowNumber() ?? 1) + 1);

                targetRow.Cell(1).Value = item.Id.ToString();
                targetRow.Cell(2).Value = item.Categoria.ToString();
                targetRow.Cell(3).Value = item.Nome;
                targetRow.Cell(4).Value = (double)item.Prezzo;
                targetRow.Cell(5).Value = item.QuantitaFotoIncluse;
                targetRow.Cell(6).Value = item.Descrizione ?? string.Empty;

                wb.Save();
            }, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public async Task DeletePrezzoCatalogoItemAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        await ExecuteWithLockAndRetryAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Run(() =>
            {
                using var wb = new XLWorkbook(DatabaseFilePath);
                if (!wb.Worksheets.Contains("ListinoPrezzi")) return;

                var ws = wb.Worksheet("ListinoPrezzi");
                var row = ws.RangeUsed()?.RowsUsed().Skip(1)
                    .FirstOrDefault(r => r.Cell(1).GetString() == itemId.ToString());
                row?.Delete();
                wb.Save();
            }, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    #endregion

    #region Acquisti Foto

    public async Task<List<AcquistoFoto>> GetAcquistiByEventoAsync(Guid eventoId, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithLockAndRetryAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await Task.Run(() =>
            {
                using var wb = new XLWorkbook(DatabaseFilePath);
                if (!wb.Worksheets.Contains("Acquisti")) return new List<AcquistoFoto>();

                var ws = wb.Worksheet("Acquisti");
                var rows = ws.RangeUsed()?.RowsUsed().Skip(1);
                if (rows == null) return new List<AcquistoFoto>();

                var eventoIdStr = eventoId.ToString();
                var list = new List<AcquistoFoto>();

                foreach (var row in rows)
                {
                    if (row.Cell(2).GetString() != eventoIdStr) continue;

                    var idStr = row.Cell(1).GetString();
                    if (!Guid.TryParse(idStr, out var id)) continue;

                    var data = ParseDateTime(row.Cell(3).GetString());
                    var atletaIdStr = row.Cell(4).GetString();
                    Guid.TryParse(atletaIdStr, out var atletaId);
                    var nomeAtleta = row.Cell(5).GetString();
                    var pettorale = row.Cell(6).GetString();

                    var discIdStr = row.Cell(7).GetString();
                    Guid? discId = Guid.TryParse(discIdStr, out var parsedDiscId) ? parsedDiscId : null;
                    var nomeDisc = row.Cell(8).GetString();

                    var totQta = (int)row.Cell(9).GetDouble();
                    var totCalc = (decimal)row.Cell(10).GetDouble();
                    var totPagato = (decimal)row.Cell(11).GetDouble();

                    var email = row.Cell(12).GetString();
                    var tel = row.Cell(13).GetString();

                    var interaCartellaStr = row.Cell(14).GetString();
                    var interaCartella = bool.TryParse(interaCartellaStr, out var ic) ? ic : true;

                    var fileFotoStr = row.Cell(15).GetString();
                    var files = !string.IsNullOrWhiteSpace(fileFotoStr)
                        ? fileFotoStr.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
                        : new List<string>();

                    var cartellaRef = row.Cell(16).GetString();
                    var vociJson = row.Cell(17).GetString();
                    var voci = new List<VoceAcquisto>();
                    if (!string.IsNullOrWhiteSpace(vociJson))
                    {
                        try
                        {
                            voci = JsonSerializer.Deserialize<List<VoceAcquisto>>(vociJson) ?? new();
                        }
                        catch
                        {
                            voci = new();
                        }
                    }

                    var note = row.Cell(19).GetString();
                    var stato = row.Cell(20).GetString();
                    if (string.IsNullOrWhiteSpace(stato)) stato = "Completato";

                    list.Add(new AcquistoFoto
                    {
                        Id = id,
                        EventoId = eventoId,
                        DataAcquisto = data,
                        AtletaId = atletaId,
                        NomeAtleta = nomeAtleta,
                        NumeroPettorale = pettorale,
                        DisciplinaId = discId,
                        NomeDisciplina = nomeDisc,
                        TotaleQuantita = totQta,
                        TotaleCalcolato = totCalc,
                        TotalePagato = totPagato,
                        EmailCliente = email,
                        TelefonoCliente = tel,
                        InteraCartella = interaCartella,
                        FileFotoSelezionate = files,
                        CartellaPathRiferimento = cartellaRef,
                        Voci = voci,
                        Note = note,
                        Stato = stato
                    });
                }

                return list.OrderByDescending(a => a.DataAcquisto).ToList();
            }, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public async Task UpsertAcquistoFotoAsync(AcquistoFoto acquisto, CancellationToken cancellationToken = default)
    {
        await ExecuteWithLockAndRetryAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Run(() =>
            {
                using var wb = new XLWorkbook(DatabaseFilePath);
                var ws = wb.Worksheets.Contains("Acquisti")
                    ? wb.Worksheet("Acquisti")
                    : wb.Worksheets.Add("Acquisti");

                var existingRow = ws.RangeUsed()?.RowsUsed().Skip(1)
                    .FirstOrDefault(r => r.Cell(1).GetString() == acquisto.Id.ToString());

                var targetRow = existingRow != null
                    ? ws.Row(existingRow.RowNumber())
                    : ws.Row((ws.LastRowUsed()?.RowNumber() ?? 1) + 1);

                targetRow.Cell(1).Value = acquisto.Id.ToString();
                targetRow.Cell(2).Value = acquisto.EventoId.ToString();
                targetRow.Cell(3).Value = acquisto.DataAcquisto.ToString("yyyy-MM-dd HH:mm:ss");
                targetRow.Cell(4).Value = acquisto.AtletaId.ToString();
                targetRow.Cell(5).Value = acquisto.NomeAtleta;
                targetRow.Cell(6).Value = acquisto.NumeroPettorale;
                targetRow.Cell(7).Value = acquisto.DisciplinaId?.ToString() ?? string.Empty;
                targetRow.Cell(8).Value = acquisto.NomeDisciplina;
                targetRow.Cell(9).Value = acquisto.TotaleQuantita;
                targetRow.Cell(10).Value = (double)acquisto.TotaleCalcolato;
                targetRow.Cell(11).Value = (double)acquisto.TotalePagato;
                targetRow.Cell(12).Value = acquisto.EmailCliente;
                targetRow.Cell(13).Value = acquisto.TelefonoCliente;
                targetRow.Cell(14).Value = acquisto.InteraCartella ? "TRUE" : "FALSE";
                targetRow.Cell(15).Value = string.Join("; ", acquisto.FileFotoSelezionate);
                targetRow.Cell(16).Value = acquisto.CartellaPathRiferimento ?? string.Empty;
                targetRow.Cell(17).Value = JsonSerializer.Serialize(acquisto.Voci);
                targetRow.Cell(18).Value = acquisto.VociSommarioDisplay;
                targetRow.Cell(19).Value = acquisto.Note ?? string.Empty;
                targetRow.Cell(20).Value = acquisto.Stato;

                wb.Save();
            }, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public async Task DeleteAcquistoFotoAsync(Guid acquistoId, CancellationToken cancellationToken = default)
    {
        await ExecuteWithLockAndRetryAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Run(() =>
            {
                using var wb = new XLWorkbook(DatabaseFilePath);
                if (!wb.Worksheets.Contains("Acquisti")) return;

                var ws = wb.Worksheet("Acquisti");
                var row = ws.RangeUsed()?.RowsUsed().Skip(1)
                    .FirstOrDefault(r => r.Cell(1).GetString() == acquistoId.ToString());
                row?.Delete();
                wb.Save();
            }, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    #endregion

    #region Consolidamento Single-Pass

    public async Task<EventDataBundle> GetEventDataBundleAsync(Guid eventoId, CancellationToken cancellationToken = default)
    {
        await EnsureDatabaseInitializedAsync(cancellationToken).ConfigureAwait(false);

        return await ExecuteWithLockAndRetryAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await Task.Run(() =>
            {
                using var wb = new XLWorkbook(DatabaseFilePath);
                var bundle = new EventDataBundle();
                var evIdStr = eventoId.ToString();

                // 1. Atleti
                if (wb.Worksheets.Contains("Atleti"))
                {
                    var ws = wb.Worksheet("Atleti");
                    var rows = ws.RangeUsed()?.RowsUsed().Skip(1);
                    if (rows != null)
                    {
                        foreach (var r in rows)
                        {
                            if (r.Cell(2).GetString() != evIdStr) continue;
                            if (!Guid.TryParse(r.Cell(1).GetString(), out var id)) continue;

                            bundle.Atleti.Add(new Atleta
                            {
                                Id = id,
                                EventoId = eventoId,
                                NumeroPettorale = r.Cell(3).GetString(),
                                Nome = r.Cell(4).GetString(),
                                Cognome = r.Cell(5).GetString(),
                                Categoria = r.Cell(6).GetString(),
                                Note = r.Cell(7).GetString()
                            });
                        }
                    }
                }

                // 2. Discipline
                if (wb.Worksheets.Contains("Discipline"))
                {
                    var ws = wb.Worksheet("Discipline");
                    var rows = ws.RangeUsed()?.RowsUsed().Skip(1);
                    if (rows != null)
                    {
                        foreach (var r in rows)
                        {
                            if (r.Cell(2).GetString() != evIdStr) continue;
                            if (!Guid.TryParse(r.Cell(1).GetString(), out var id)) continue;

                            bundle.Discipline.Add(new Disciplina
                            {
                                Id = id,
                                EventoId = eventoId,
                                NomeDisciplina = r.Cell(3).GetString(),
                                Descrizione = r.Cell(4).GetString()
                            });
                        }
                    }
                }

                // 3. Foto
                if (wb.Worksheets.Contains("Foto"))
                {
                    var ws = wb.Worksheet("Foto");
                    var rows = ws.RangeUsed()?.RowsUsed().Skip(1);
                    if (rows != null)
                    {
                        foreach (var r in rows)
                        {
                            if (r.Cell(2).GetString() != evIdStr) continue;
                            if (!Guid.TryParse(r.Cell(1).GetString(), out var id)) continue;

                            var aid = Guid.TryParse(r.Cell(3).GetString(), out var parsedAid) ? parsedAid : Guid.Empty;
                            var relPath = r.Cell(6).GetString();
                            var isPrem = bool.TryParse(r.Cell(13).GetString(), out var p)
                                ? p
                                : (relPath.Replace('/', '\\').Contains(@"\Premiazioni\") || relPath.StartsWith("Premiazioni", StringComparison.OrdinalIgnoreCase));

                            bundle.Foto.Add(new Foto
                            {
                                Id = id,
                                EventoId = eventoId,
                                AtletaId = aid,
                                DisciplinaId = Guid.TryParse(r.Cell(4).GetString(), out var did) ? did : Guid.Empty,
                                NomeFileOriginale = r.Cell(5).GetString(),
                                PathRelativo = relPath,
                                Formato = r.Cell(7).GetString(),
                                DataScatto = ParseNullableDateTime(r.Cell(8).GetString()),
                                Fotografo = r.Cell(9).GetString(),
                                WatermarkApplicato = bool.TryParse(r.Cell(10).GetString(), out var w) && w,
                                DimensioneByte = long.TryParse(r.Cell(11).GetString(), out var s) ? s : 0,
                                HashMd5 = r.Cell(12).GetString(),
                                IsPremiazione = isPrem
                            });
                        }
                    }
                }

                // 4. Catalogo Prezzi
                if (wb.Worksheets.Contains("ListinoPrezzi"))
                {
                    var ws = wb.Worksheet("ListinoPrezzi");
                    var rows = ws.RangeUsed()?.RowsUsed().Skip(1);
                    if (rows != null)
                    {
                        foreach (var row in rows)
                        {
                            var idStr = row.Cell(1).GetString();
                            if (!Guid.TryParse(idStr, out var id)) continue;

                            var catStr = row.Cell(2).GetString();
                            if (!Enum.TryParse<CategoriaPrezzo>(catStr, true, out var cat))
                            {
                                cat = CategoriaPrezzo.FotoSingola;
                            }

                            var nome = row.Cell(3).GetString();
                            var prezzo = (decimal)row.Cell(4).GetDouble();
                            var qta = (int)row.Cell(5).GetDouble();
                            var desc = row.Cell(6).GetString();

                            bundle.CatalogoPrezzi.Add(new PrezzoCatalogoItem
                            {
                                Id = id,
                                Categoria = cat,
                                Nome = nome,
                                Prezzo = prezzo,
                                QuantitaFotoIncluse = qta > 0 ? qta : 1,
                                Descrizione = desc
                            });
                        }
                    }
                }
                if (bundle.CatalogoPrezzi.Count == 0)
                {
                    bundle.CatalogoPrezzi.AddRange(GetDefaultCatalogoItems());
                }

                // 5. Acquisti
                if (wb.Worksheets.Contains("Acquisti"))
                {
                    var ws = wb.Worksheet("Acquisti");
                    var rows = ws.RangeUsed()?.RowsUsed().Skip(1);
                    if (rows != null)
                    {
                        foreach (var row in rows)
                        {
                            if (row.Cell(2).GetString() != evIdStr) continue;

                            var idStr = row.Cell(1).GetString();
                            if (!Guid.TryParse(idStr, out var id)) continue;

                            var data = ParseDateTime(row.Cell(3).GetString());
                            var atletaIdStr = row.Cell(4).GetString();
                            Guid.TryParse(atletaIdStr, out var atletaId);
                            var nomeAtleta = row.Cell(5).GetString();
                            var pettorale = row.Cell(6).GetString();

                            var discIdStr = row.Cell(7).GetString();
                            Guid? discId = Guid.TryParse(discIdStr, out var parsedDiscId) ? parsedDiscId : null;
                            var nomeDisc = row.Cell(8).GetString();

                            var totQta = (int)row.Cell(9).GetDouble();
                            var totCalc = (decimal)row.Cell(10).GetDouble();
                            var totPagato = (decimal)row.Cell(11).GetDouble();

                            var email = row.Cell(12).GetString();
                            var tel = row.Cell(13).GetString();

                            var interaCartellaStr = row.Cell(14).GetString();
                            var interaCartella = bool.TryParse(interaCartellaStr, out var ic) ? ic : true;

                            var fileFotoStr = row.Cell(15).GetString();
                            var files = !string.IsNullOrWhiteSpace(fileFotoStr)
                                ? fileFotoStr.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
                                : new List<string>();

                            var cartellaRef = row.Cell(16).GetString();
                            var vociJson = row.Cell(17).GetString();
                            var voci = new List<VoceAcquisto>();
                            if (!string.IsNullOrWhiteSpace(vociJson))
                            {
                                try
                                {
                                    voci = JsonSerializer.Deserialize<List<VoceAcquisto>>(vociJson) ?? new();
                                }
                                catch
                                {
                                    voci = new();
                                }
                            }

                            bundle.Acquisti.Add(new AcquistoFoto
                            {
                                Id = id,
                                EventoId = eventoId,
                                DataAcquisto = data,
                                AtletaId = atletaId,
                                NomeAtleta = nomeAtleta,
                                NumeroPettorale = pettorale,
                                DisciplinaId = discId,
                                NomeDisciplina = nomeDisc,
                                TotaleQuantita = totQta,
                                TotaleCalcolato = totCalc,
                                TotalePagato = totPagato,
                                EmailCliente = email,
                                TelefonoCliente = tel,
                                InteraCartella = interaCartella,
                                FileFotoSelezionate = files,
                                CartellaPathRiferimento = cartellaRef,
                                Voci = voci,
                                Note = row.Cell(19).GetString(),
                                Stato = row.Cell(20).GetString()
                            });
                        }
                    }
                }

                return bundle;
            }, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    #endregion

    private static DateTime ParseDateTime(string str)
    {
        if (DateTime.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ||
            DateTime.TryParse(str, out dt))
        {
            return dt;
        }
        return DateTime.Today;
    }

    private static DateTime? ParseNullableDateTime(string str)
    {
        if (string.IsNullOrWhiteSpace(str)) return null;
        if (DateTime.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ||
            DateTime.TryParse(str, out dt))
        {
            return dt;
        }
        return null;
    }
}
