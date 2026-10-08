using Paddock.Core.DTOs;
using Paddock.Core.Models;

namespace Paddock.Core.Interfaces;

public enum DatabaseEngine
{
    Excel,
    Sqlite
}

public interface IDatabaseRepository
{
    string DatabaseFilePath { get; set; }
    string ResolvedDatabaseFilePath { get; }
    DatabaseEngine EngineType { get; }
    string ResolvePath(string? path);
    event EventHandler<LockContentionEventArgs>? LockContentionDetected;

    Task EnsureDatabaseInitializedAsync(CancellationToken cancellationToken = default);
    Task SwitchDatabaseAsync(string newFilePath, CancellationToken cancellationToken = default);

    Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default);
    Task SetSettingAsync(string key, string value, string? description = null, CancellationToken cancellationToken = default);
    Task<string?> GetBasePathAsync(CancellationToken cancellationToken = default);
    Task<string> GetResolvedBasePathAsync(CancellationToken cancellationToken = default);
    Task SetBasePathAsync(string newBasePath, CancellationToken cancellationToken = default);
    Task UpdateAllEventRootsAsync(string newBasePath, CancellationToken cancellationToken = default);

    Task<List<Evento>> GetEventiAsync(CancellationToken cancellationToken = default);
    Task<Evento?> GetEventoByIdAsync(Guid eventoId, CancellationToken cancellationToken = default);
    Task UpsertEventoAsync(Evento evento, CancellationToken cancellationToken = default);
    Task DeleteEventoAsync(Guid eventoId, CancellationToken cancellationToken = default);

    Task<List<Disciplina>> GetDisciplineByEventoAsync(Guid eventoId, CancellationToken cancellationToken = default);
    Task UpsertDisciplinaAsync(Disciplina disciplina, CancellationToken cancellationToken = default);
    Task DeleteDisciplinaAsync(Guid disciplinaId, CancellationToken cancellationToken = default);

    Task<List<Atleta>> GetAtletiByEventoAsync(Guid eventoId, CancellationToken cancellationToken = default);
    Task UpsertAtletaAsync(Atleta atleta, CancellationToken cancellationToken = default);
    Task DeleteAtletaAsync(Guid atletaId, CancellationToken cancellationToken = default);

    Task<List<Foto>> GetFotoByEventoAsync(Guid eventoId, CancellationToken cancellationToken = default);
    Task<List<Foto>> GetFotoByAtletaAsync(Guid atletaId, CancellationToken cancellationToken = default);
    Task AddFotoBatchAsync(IEnumerable<Foto> fotoList, CancellationToken cancellationToken = default);
    Task UpdateFotoAsync(Foto foto, CancellationToken cancellationToken = default);
    Task DeleteFotoAsync(Guid fotoId, CancellationToken cancellationToken = default);

    // Catalogo Prezzi
    Task<List<PrezzoCatalogoItem>> GetCatalogoPrezziAsync(CancellationToken cancellationToken = default);
    Task SaveCatalogoPrezziAsync(IEnumerable<PrezzoCatalogoItem> items, CancellationToken cancellationToken = default);
    Task UpsertPrezzoCatalogoItemAsync(PrezzoCatalogoItem item, CancellationToken cancellationToken = default);
    Task DeletePrezzoCatalogoItemAsync(Guid itemId, CancellationToken cancellationToken = default);

    // Acquisti Foto
    Task<List<AcquistoFoto>> GetAcquistiByEventoAsync(Guid eventoId, CancellationToken cancellationToken = default);
    Task UpsertAcquistoFotoAsync(AcquistoFoto acquisto, CancellationToken cancellationToken = default);
    Task DeleteAcquistoFotoAsync(Guid acquistoId, CancellationToken cancellationToken = default);

    // Caricamento Consolidato Single-Pass
    Task<EventDataBundle> GetEventDataBundleAsync(Guid eventoId, CancellationToken cancellationToken = default);
}

