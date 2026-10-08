using Paddock.Core.DTOs;
using Paddock.Core.Interfaces;
using Paddock.Core.Models;
using Paddock.Infrastructure.Excel;
using Paddock.Infrastructure.Sqlite;

namespace Paddock.Infrastructure.Services;

public class DatabaseRepositoryRouter : IDatabaseRepository, IExcelRepository
{
    private readonly SqliteRepository _sqliteRepo;
    private readonly ExcelRepository _excelRepo;
    private IDatabaseRepository _activeRepo;

    public event EventHandler<LockContentionEventArgs>? LockContentionDetected;

    public DatabaseEngine EngineType => _activeRepo.EngineType;

    public string DatabaseFilePath
    {
        get => _activeRepo.DatabaseFilePath;
        set
        {
            var engine = DetectEngine(value);
            if (engine == DatabaseEngine.Excel)
            {
                _excelRepo.DatabaseFilePath = value;
                _activeRepo = _excelRepo;
            }
            else
            {
                _sqliteRepo.DatabaseFilePath = value;
                _activeRepo = _sqliteRepo;
            }
        }
    }

    public string ResolvedDatabaseFilePath => _activeRepo.ResolvedDatabaseFilePath;

    public static DatabaseEngine DetectEngine(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return DatabaseEngine.Sqlite;

        var ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext))
            return DatabaseEngine.Sqlite;

        return ext.ToLowerInvariant() switch
        {
            ".xlsx" or ".xlsm" or ".xls" => DatabaseEngine.Excel,
            _ => DatabaseEngine.Sqlite
        };
    }

    public DatabaseRepositoryRouter(string? initialDatabasePath = null)
    {
        _sqliteRepo = new SqliteRepository(initialDatabasePath);
        _excelRepo = new ExcelRepository(initialDatabasePath);

        _sqliteRepo.LockContentionDetected += OnSubRepoLockContention;
        _excelRepo.LockContentionDetected += OnSubRepoLockContention;

        var engine = DetectEngine(initialDatabasePath);
        _activeRepo = engine == DatabaseEngine.Excel ? _excelRepo : _sqliteRepo;
    }

    private void OnSubRepoLockContention(object? sender, LockContentionEventArgs e)
    {
        LockContentionDetected?.Invoke(this, e);
    }

    public string ResolvePath(string? path) => _activeRepo.ResolvePath(path);

    public Task<string> GetResolvedBasePathAsync(CancellationToken cancellationToken = default) =>
        _activeRepo.GetResolvedBasePathAsync(cancellationToken);

    public async Task SwitchDatabaseAsync(string newFilePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newFilePath))
            throw new ArgumentException("Il percorso del nuovo database non può essere vuoto.", nameof(newFilePath));

        var engine = DetectEngine(newFilePath);
        if (engine == DatabaseEngine.Excel)
        {
            _activeRepo = _excelRepo;
            await _excelRepo.SwitchDatabaseAsync(newFilePath, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            _activeRepo = _sqliteRepo;
            await _sqliteRepo.SwitchDatabaseAsync(newFilePath, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task EnsureDatabaseInitializedAsync(CancellationToken cancellationToken = default) =>
        _activeRepo.EnsureDatabaseInitializedAsync(cancellationToken);

    #region Delegated CRUD Operations

    public Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default) =>
        _activeRepo.GetSettingAsync(key, cancellationToken);

    public Task SetSettingAsync(string key, string value, string? description = null, CancellationToken cancellationToken = default) =>
        _activeRepo.SetSettingAsync(key, value, description, cancellationToken);

    public Task<string?> GetBasePathAsync(CancellationToken cancellationToken = default) =>
        _activeRepo.GetBasePathAsync(cancellationToken);

    public Task SetBasePathAsync(string newBasePath, CancellationToken cancellationToken = default) =>
        _activeRepo.SetBasePathAsync(newBasePath, cancellationToken);

    public Task UpdateAllEventRootsAsync(string newBasePath, CancellationToken cancellationToken = default) =>
        _activeRepo.UpdateAllEventRootsAsync(newBasePath, cancellationToken);

    public Task<List<Evento>> GetEventiAsync(CancellationToken cancellationToken = default) =>
        _activeRepo.GetEventiAsync(cancellationToken);

    public Task<Evento?> GetEventoByIdAsync(Guid eventoId, CancellationToken cancellationToken = default) =>
        _activeRepo.GetEventoByIdAsync(eventoId, cancellationToken);

    public Task UpsertEventoAsync(Evento evento, CancellationToken cancellationToken = default) =>
        _activeRepo.UpsertEventoAsync(evento, cancellationToken);

    public Task DeleteEventoAsync(Guid eventoId, CancellationToken cancellationToken = default) =>
        _activeRepo.DeleteEventoAsync(eventoId, cancellationToken);

    public Task<List<Disciplina>> GetDisciplineByEventoAsync(Guid eventoId, CancellationToken cancellationToken = default) =>
        _activeRepo.GetDisciplineByEventoAsync(eventoId, cancellationToken);

    public Task UpsertDisciplinaAsync(Disciplina disciplina, CancellationToken cancellationToken = default) =>
        _activeRepo.UpsertDisciplinaAsync(disciplina, cancellationToken);

    public Task DeleteDisciplinaAsync(Guid disciplinaId, CancellationToken cancellationToken = default) =>
        _activeRepo.DeleteDisciplinaAsync(disciplinaId, cancellationToken);

    public Task<List<Atleta>> GetAtletiByEventoAsync(Guid eventoId, CancellationToken cancellationToken = default) =>
        _activeRepo.GetAtletiByEventoAsync(eventoId, cancellationToken);

    public Task UpsertAtletaAsync(Atleta atleta, CancellationToken cancellationToken = default) =>
        _activeRepo.UpsertAtletaAsync(atleta, cancellationToken);

    public Task DeleteAtletaAsync(Guid atletaId, CancellationToken cancellationToken = default) =>
        _activeRepo.DeleteAtletaAsync(atletaId, cancellationToken);

    public Task<List<Foto>> GetFotoByEventoAsync(Guid eventoId, CancellationToken cancellationToken = default) =>
        _activeRepo.GetFotoByEventoAsync(eventoId, cancellationToken);

    public Task<List<Foto>> GetFotoByAtletaAsync(Guid atletaId, CancellationToken cancellationToken = default) =>
        _activeRepo.GetFotoByAtletaAsync(atletaId, cancellationToken);

    public Task AddFotoBatchAsync(IEnumerable<Foto> fotoList, CancellationToken cancellationToken = default) =>
        _activeRepo.AddFotoBatchAsync(fotoList, cancellationToken);

    public Task UpdateFotoAsync(Foto foto, CancellationToken cancellationToken = default) =>
        _activeRepo.UpdateFotoAsync(foto, cancellationToken);

    public Task DeleteFotoAsync(Guid fotoId, CancellationToken cancellationToken = default) =>
        _activeRepo.DeleteFotoAsync(fotoId, cancellationToken);

    public Task<List<PrezzoCatalogoItem>> GetCatalogoPrezziAsync(CancellationToken cancellationToken = default) =>
        _activeRepo.GetCatalogoPrezziAsync(cancellationToken);

    public Task SaveCatalogoPrezziAsync(IEnumerable<PrezzoCatalogoItem> items, CancellationToken cancellationToken = default) =>
        _activeRepo.SaveCatalogoPrezziAsync(items, cancellationToken);

    public Task UpsertPrezzoCatalogoItemAsync(PrezzoCatalogoItem item, CancellationToken cancellationToken = default) =>
        _activeRepo.UpsertPrezzoCatalogoItemAsync(item, cancellationToken);

    public Task DeletePrezzoCatalogoItemAsync(Guid itemId, CancellationToken cancellationToken = default) =>
        _activeRepo.DeletePrezzoCatalogoItemAsync(itemId, cancellationToken);

    public Task<List<AcquistoFoto>> GetAcquistiByEventoAsync(Guid eventoId, CancellationToken cancellationToken = default) =>
        _activeRepo.GetAcquistiByEventoAsync(eventoId, cancellationToken);

    public Task UpsertAcquistoFotoAsync(AcquistoFoto acquisto, CancellationToken cancellationToken = default) =>
        _activeRepo.UpsertAcquistoFotoAsync(acquisto, cancellationToken);

    public Task DeleteAcquistoFotoAsync(Guid acquistoId, CancellationToken cancellationToken = default) =>
        _activeRepo.DeleteAcquistoFotoAsync(acquistoId, cancellationToken);

    public Task<EventDataBundle> GetEventDataBundleAsync(Guid eventoId, CancellationToken cancellationToken = default) =>
        _activeRepo.GetEventDataBundleAsync(eventoId, cancellationToken);

    #endregion

    #region Utilità Migrazione Excel -> SQLite

    /// <summary>
    /// Copia fedelmente tutti i dati presenti nel database Excel sorgente in un nuovo database SQLite
    /// e commuta l'applicazione su quest'ultimo.
    /// </summary>
    public async Task MigrateExcelToSqliteAsync(string sourceExcelPath, string destinationSqlitePath, CancellationToken cancellationToken = default)
    {
        var tempExcel = new ExcelRepository(sourceExcelPath);
        var tempSqlite = new SqliteRepository(destinationSqlitePath);

        // Assicura il rilascio di eventuali connessioni residue e pulizia del file di destinazione per una migrazione vergine
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var resolvedDest = tempSqlite.ResolvedDatabaseFilePath;
        if (File.Exists(resolvedDest))
        {
            try
            {
                File.Delete(resolvedDest);
                if (File.Exists(resolvedDest + "-wal")) File.Delete(resolvedDest + "-wal");
                if (File.Exists(resolvedDest + "-shm")) File.Delete(resolvedDest + "-shm");
            }
            catch
            {
                // Se bloccato o non eliminabile, procederà sovrascrivendo tramite upsert
            }
        }

        await tempExcel.EnsureDatabaseInitializedAsync(cancellationToken).ConfigureAwait(false);
        await tempSqlite.EnsureDatabaseInitializedAsync(cancellationToken).ConfigureAwait(false);

        // 1. Impostazioni
        var basePath = await tempExcel.GetBasePathAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(basePath))
        {
            await tempSqlite.SetBasePathAsync(basePath, cancellationToken).ConfigureAwait(false);
        }

        // 2. Catalogo Prezzi
        var catalogo = await tempExcel.GetCatalogoPrezziAsync(cancellationToken).ConfigureAwait(false);
        if (catalogo.Count > 0)
        {
            await tempSqlite.SaveCatalogoPrezziAsync(catalogo, cancellationToken).ConfigureAwait(false);
        }

        // 3. Eventi, Discipline, Atleti, Foto, Acquisti
        var eventi = await tempExcel.GetEventiAsync(cancellationToken).ConfigureAwait(false);
        foreach (var ev in eventi)
        {
            await tempSqlite.UpsertEventoAsync(ev, cancellationToken).ConfigureAwait(false);

            var discipline = await tempExcel.GetDisciplineByEventoAsync(ev.Id, cancellationToken).ConfigureAwait(false);
            foreach (var d in discipline)
            {
                await tempSqlite.UpsertDisciplinaAsync(d, cancellationToken).ConfigureAwait(false);
            }

            var atleti = await tempExcel.GetAtletiByEventoAsync(ev.Id, cancellationToken).ConfigureAwait(false);
            foreach (var a in atleti)
            {
                await tempSqlite.UpsertAtletaAsync(a, cancellationToken).ConfigureAwait(false);
            }

            var foto = await tempExcel.GetFotoByEventoAsync(ev.Id, cancellationToken).ConfigureAwait(false);
            if (foto.Count > 0)
            {
                await tempSqlite.AddFotoBatchAsync(foto, cancellationToken).ConfigureAwait(false);
            }

            var acquisti = await tempExcel.GetAcquistiByEventoAsync(ev.Id, cancellationToken).ConfigureAwait(false);
            foreach (var acq in acquisti)
            {
                await tempSqlite.UpsertAcquistoFotoAsync(acq, cancellationToken).ConfigureAwait(false);
            }
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        // Commuta il router sul nuovo SQLite
        await SwitchDatabaseAsync(destinationSqlitePath, cancellationToken).ConfigureAwait(false);
    }

    #endregion
}

