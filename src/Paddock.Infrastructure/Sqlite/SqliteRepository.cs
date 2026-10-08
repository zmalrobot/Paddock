using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Paddock.Core.DTOs;
using Paddock.Core.Interfaces;
using Paddock.Core.Models;
using Paddock.Infrastructure.Excel;

namespace Paddock.Infrastructure.Sqlite;

public class SqliteRepository : IDatabaseRepository
{
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public string DatabaseFilePath { get; set; }
    public DatabaseEngine EngineType => DatabaseEngine.Sqlite;

    public string ResolvedDatabaseFilePath
    {
        get
        {
            if (string.IsNullOrWhiteSpace(DatabaseFilePath))
                return string.Empty;

            if (Path.IsPathRooted(DatabaseFilePath))
                return Path.GetFullPath(DatabaseFilePath);

            return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, DatabaseFilePath));
        }
    }

    public string ResolvePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        if (Path.IsPathRooted(path))
            return Path.GetFullPath(path);

        var referenceDir = !string.IsNullOrWhiteSpace(ResolvedDatabaseFilePath)
            ? Path.GetDirectoryName(ResolvedDatabaseFilePath)
            : AppContext.BaseDirectory;

        if (string.IsNullOrWhiteSpace(referenceDir))
        {
            referenceDir = AppContext.BaseDirectory;
        }

        return Path.GetFullPath(Path.Combine(referenceDir, path));
    }

    public async Task<string> GetResolvedBasePathAsync(CancellationToken cancellationToken = default)
    {
        var raw = await GetBasePathAsync(cancellationToken).ConfigureAwait(false);
        return ResolvePath(raw);
    }

    public event EventHandler<LockContentionEventArgs>? LockContentionDetected
    {
        add { }
        remove { }
    }

    public SqliteRepository(string? databaseFilePath = null)
    {
        if (string.IsNullOrWhiteSpace(databaseFilePath))
        {
            var containerDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
            var defaultDbDir = Path.Combine(containerDir, "Database");
            var defaultDbFile = Path.Combine(defaultDbDir, "Paddock_Database.db");

            if (Directory.Exists(defaultDbDir) || File.Exists(defaultDbFile))
            {
                DatabaseFilePath = Path.Combine("..", "Database", "Paddock_Database.db");
            }
            else
            {
                var defaultFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "Paddock");
                Directory.CreateDirectory(defaultFolder);
                DatabaseFilePath = Path.Combine(defaultFolder, "Paddock_Database.db");
            }
        }
        else
        {
            DatabaseFilePath = databaseFilePath;
        }
    }

    private string GetConnectionString()
    {
        var b = new SqliteConnectionStringBuilder
        {
            DataSource = ResolvedDatabaseFilePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        };
        return b.ToString();
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var conn = new SqliteConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL; PRAGMA foreign_keys = OFF; PRAGMA busy_timeout = 5000;";
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        return conn;
    }

    public async Task SwitchDatabaseAsync(string newFilePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newFilePath))
            throw new ArgumentException("Il percorso del nuovo database non può essere vuoto.", nameof(newFilePath));

        DatabaseFilePath = newFilePath;
        await EnsureDatabaseInitializedAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task EnsureDatabaseInitializedAsync(CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var targetPath = ResolvedDatabaseFilePath;
            var dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await using var conn = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var tx = conn.BeginTransaction();

            var ddl = @"
                CREATE TABLE IF NOT EXISTS Impostazioni (
                    Chiave TEXT PRIMARY KEY,
                    Valore TEXT NOT NULL,
                    Descrizione TEXT,
                    DataModifica TEXT
                );

                CREATE TABLE IF NOT EXISTS Eventi (
                    Id TEXT PRIMARY KEY,
                    NomeEvento TEXT NOT NULL,
                    DataInizio TEXT NOT NULL,
                    DataFine TEXT NOT NULL,
                    Luogo TEXT NOT NULL,
                    CartellaDestinazioneRoot TEXT NOT NULL,
                    Note TEXT,
                    DataCreazione TEXT
                );

                CREATE TABLE IF NOT EXISTS Discipline (
                    Id TEXT PRIMARY KEY,
                    EventoId TEXT NOT NULL,
                    NomeDisciplina TEXT NOT NULL,
                    Descrizione TEXT
                );

                CREATE TABLE IF NOT EXISTS Atleti (
                    Id TEXT PRIMARY KEY,
                    EventoId TEXT NOT NULL,
                    NumeroPettorale TEXT,
                    Nome TEXT NOT NULL,
                    Cognome TEXT NOT NULL,
                    Categoria TEXT,
                    Note TEXT,
                    TimestampIngestione TEXT
                );

                CREATE TABLE IF NOT EXISTS Foto (
                    Id TEXT PRIMARY KEY,
                    EventoId TEXT NOT NULL,
                    AtletaId TEXT NOT NULL,
                    DisciplinaId TEXT NOT NULL,
                    NomeFileOriginale TEXT NOT NULL,
                    PathRelativo TEXT NOT NULL,
                    Formato TEXT NOT NULL,
                    DataScatto TEXT,
                    Fotografo TEXT,
                    WatermarkApplicato INTEGER NOT NULL DEFAULT 0,
                    DimensioneByte INTEGER NOT NULL DEFAULT 0,
                    HashMd5 TEXT,
                    IsPremiazione INTEGER NOT NULL DEFAULT 0
                );

                CREATE TABLE IF NOT EXISTS ListinoPrezzi (
                    Id TEXT PRIMARY KEY,
                    Categoria TEXT NOT NULL,
                    Nome TEXT NOT NULL,
                    Prezzo REAL NOT NULL,
                    QuantitaFoto INTEGER NOT NULL,
                    Descrizione TEXT
                );

                CREATE TABLE IF NOT EXISTS Acquisti (
                    Id TEXT PRIMARY KEY,
                    EventoId TEXT NOT NULL,
                    DataAcquisto TEXT NOT NULL,
                    AtletaId TEXT NOT NULL,
                    NomeAtleta TEXT,
                    NumeroPettorale TEXT,
                    DisciplinaId TEXT,
                    NomeDisciplina TEXT,
                    TotaleQuantita INTEGER NOT NULL,
                    TotaleCalcolato REAL NOT NULL,
                    TotalePagato REAL NOT NULL,
                    EmailCliente TEXT,
                    TelefonoCliente TEXT,
                    InteraCartella INTEGER NOT NULL,
                    FileFotoSelezionate TEXT,
                    CartellaPathRiferimento TEXT,
                    VociJson TEXT,
                    VociSommario TEXT,
                    Note TEXT,
                    Stato TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_discipline_evento ON Discipline(EventoId);
                CREATE INDEX IF NOT EXISTS idx_atleti_evento ON Atleti(EventoId);
                CREATE INDEX IF NOT EXISTS idx_atleti_pettorale ON Atleti(EventoId, NumeroPettorale);
                CREATE INDEX IF NOT EXISTS idx_foto_evento ON Foto(EventoId);
                CREATE INDEX IF NOT EXISTS idx_foto_atleta ON Foto(AtletaId);
                CREATE INDEX IF NOT EXISTS idx_foto_disciplina ON Foto(DisciplinaId);
                CREATE INDEX IF NOT EXISTS idx_foto_evento_atleta ON Foto(EventoId, AtletaId);
                CREATE INDEX IF NOT EXISTS idx_acquisti_evento ON Acquisti(EventoId);
            ";

            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = ddl;
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            // Inizializza impostazioni predefinite se non presenti
            var defaultSettingsCmd = @"
                INSERT OR IGNORE INTO Impostazioni (Chiave, Valore, Descrizione, DataModifica)
                VALUES ('BasePath', '..\Foto', 'Percorso radice per archivio foto (relativo o completo)', datetime('now')),
                       ('VersioneSchema', '1.0', 'Versione dello schema database SQLite', datetime('now'));
            ";
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = defaultSettingsCmd;
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            // Inizializza Listino Prezzi se vuoto
            await using (var checkCmd = conn.CreateCommand())
            {
                checkCmd.Transaction = tx;
                checkCmd.CommandText = "SELECT COUNT(*) FROM ListinoPrezzi;";
                var count = Convert.ToInt64(await checkCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
                if (count == 0)
                {
                    var defaultItems = ExcelRepository.GetDefaultCatalogoItems();
                    foreach (var item in defaultItems)
                    {
                        await using var insertItem = conn.CreateCommand();
                        insertItem.Transaction = tx;
                        insertItem.CommandText = @"
                            INSERT INTO ListinoPrezzi (Id, Categoria, Nome, Prezzo, QuantitaFoto, Descrizione)
                            VALUES (@id, @cat, @nome, @prezzo, @qta, @desc);
                        ";
                        insertItem.Parameters.AddWithValue("@id", item.Id.ToString());
                        insertItem.Parameters.AddWithValue("@cat", item.Categoria.ToString());
                        insertItem.Parameters.AddWithValue("@nome", item.Nome);
                        insertItem.Parameters.AddWithValue("@prezzo", (double)item.Prezzo);
                        insertItem.Parameters.AddWithValue("@qta", item.QuantitaFotoIncluse);
                        insertItem.Parameters.AddWithValue("@desc", item.Descrizione ?? string.Empty);
                        await insertItem.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);

            // Assicura l'esistenza della cartella Foto di default a fianco del database
            var defaultFotoDir = ResolvePath(@"..\Foto");
            if (!string.IsNullOrEmpty(defaultFotoDir) && !Directory.Exists(defaultFotoDir))
            {
                try { Directory.CreateDirectory(defaultFotoDir); } catch { /* ignore */ }
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    #region Impostazioni

    public async Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var conn = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Valore FROM Impostazioni WHERE Chiave = @k;";
        cmd.Parameters.AddWithValue("@k", key);

        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result?.ToString();
    }

    public async Task SetSettingAsync(string key, string value, string? description = null, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var conn = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO Impostazioni (Chiave, Valore, Descrizione, DataModifica)
                VALUES (@k, @v, @d, datetime('now'))
                ON CONFLICT(Chiave) DO UPDATE SET
                    Valore = excluded.Valore,
                    Descrizione = COALESCE(excluded.Descrizione, Impostazioni.Descrizione),
                    DataModifica = excluded.DataModifica;
            ";
            cmd.Parameters.AddWithValue("@k", key);
            cmd.Parameters.AddWithValue("@v", value);
            cmd.Parameters.AddWithValue("@d", (object?)description ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<string?> GetBasePathAsync(CancellationToken cancellationToken = default)
    {
        return await GetSettingAsync("BasePath", cancellationToken).ConfigureAwait(false);
    }

    public async Task SetBasePathAsync(string newBasePath, CancellationToken cancellationToken = default)
    {
        await SetSettingAsync("BasePath", newBasePath, "Percorso radice per archivio foto (relativo o completo)", cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAllEventRootsAsync(string newBasePath, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var conn = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var tx = conn.BeginTransaction();

            await using (var updateSettings = conn.CreateCommand())
            {
                updateSettings.Transaction = tx;
                updateSettings.CommandText = @"
                    INSERT INTO Impostazioni (Chiave, Valore, Descrizione, DataModifica)
                    VALUES ('BasePath', @v, 'Percorso radice per archivio foto (relativo o completo)', datetime('now'))
                    ON CONFLICT(Chiave) DO UPDATE SET Valore = excluded.Valore, DataModifica = excluded.DataModifica;
                ";
                updateSettings.Parameters.AddWithValue("@v", newBasePath);
                await updateSettings.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (var updateEventi = conn.CreateCommand())
            {
                updateEventi.Transaction = tx;
                updateEventi.CommandText = "UPDATE Eventi SET CartellaDestinazioneRoot = @root;";
                updateEventi.Parameters.AddWithValue("@root", newBasePath);
                await updateEventi.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    #endregion

    #region Eventi

    public async Task<List<Evento>> GetEventiAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<Evento>();
        await using var conn = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        
        // Query con statistiche aggregate in un unico colpo
        var sql = @"
            SELECT e.Id, e.NomeEvento, e.DataInizio, e.DataFine, e.Luogo, e.CartellaDestinazioneRoot, e.Note,
                   (SELECT COUNT(*) FROM Atleti a WHERE a.EventoId = e.Id) AS TotAtleti,
                   (SELECT COUNT(*) FROM Discipline d WHERE d.EventoId = e.Id) AS TotDiscipline,
                   (SELECT COUNT(*) FROM Foto f WHERE f.EventoId = e.Id) AS TotFoto,
                   COALESCE((SELECT SUM(f.DimensioneByte) FROM Foto f WHERE f.EventoId = e.Id), 0) AS TotBytes
            FROM Eventi e
            ORDER BY e.DataInizio DESC;
        ";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var evento = new Evento
            {
                Id = Guid.TryParse(reader.GetString(0), out var id) ? id : Guid.NewGuid(),
                NomeEvento = reader.GetString(1),
                DataInizio = DateTime.TryParse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.None, out var di) ? di : DateTime.Today,
                DataFine = DateTime.TryParse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.None, out var df) ? df : DateTime.Today,
                Luogo = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                CartellaDestinazioneRoot = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                Note = reader.IsDBNull(6) ? null : reader.GetString(6),
                TotaleAtleti = reader.GetInt32(7),
                TotaleDiscipline = reader.GetInt32(8),
                TotaleFoto = reader.GetInt32(9),
                TotaleByteOccupati = reader.GetInt64(10)
            };
            list.Add(evento);
        }

        return list;
    }

    public async Task<Evento?> GetEventoByIdAsync(Guid eventoId, CancellationToken cancellationToken = default)
    {
        await using var conn = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var sql = @"
            SELECT e.Id, e.NomeEvento, e.DataInizio, e.DataFine, e.Luogo, e.CartellaDestinazioneRoot, e.Note,
                   (SELECT COUNT(*) FROM Atleti a WHERE a.EventoId = e.Id) AS TotAtleti,
                   (SELECT COUNT(*) FROM Discipline d WHERE d.EventoId = e.Id) AS TotDiscipline,
                   (SELECT COUNT(*) FROM Foto f WHERE f.EventoId = e.Id) AS TotFoto,
                   COALESCE((SELECT SUM(f.DimensioneByte) FROM Foto f WHERE f.EventoId = e.Id), 0) AS TotBytes
            FROM Eventi e
            WHERE e.Id = @id;
        ";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@id", eventoId.ToString());

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return new Evento
            {
                Id = Guid.TryParse(reader.GetString(0), out var id) ? id : Guid.NewGuid(),
                NomeEvento = reader.GetString(1),
                DataInizio = DateTime.TryParse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.None, out var di) ? di : DateTime.Today,
                DataFine = DateTime.TryParse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.None, out var df) ? df : DateTime.Today,
                Luogo = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                CartellaDestinazioneRoot = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                Note = reader.IsDBNull(6) ? null : reader.GetString(6),
                TotaleAtleti = reader.GetInt32(7),
                TotaleDiscipline = reader.GetInt32(8),
                TotaleFoto = reader.GetInt32(9),
                TotaleByteOccupati = reader.GetInt64(10)
            };
        }

        return null;
    }

    public async Task UpsertEventoAsync(Evento evento, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var conn = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO Eventi (Id, NomeEvento, DataInizio, DataFine, Luogo, CartellaDestinazioneRoot, Note, DataCreazione)
                VALUES (@id, @nome, @inizio, @fine, @luogo, @root, @note, datetime('now'))
                ON CONFLICT(Id) DO UPDATE SET
                    NomeEvento = excluded.NomeEvento,
                    DataInizio = excluded.DataInizio,
                    DataFine = excluded.DataFine,
                    Luogo = excluded.Luogo,
                    CartellaDestinazioneRoot = excluded.CartellaDestinazioneRoot,
                    Note = excluded.Note;
            ";
            cmd.Parameters.AddWithValue("@id", evento.Id.ToString());
            cmd.Parameters.AddWithValue("@nome", evento.NomeEvento);
            cmd.Parameters.AddWithValue("@inizio", evento.DataInizio.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@fine", evento.DataFine.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@luogo", evento.Luogo);
            cmd.Parameters.AddWithValue("@root", evento.CartellaDestinazioneRoot);
            cmd.Parameters.AddWithValue("@note", (object?)evento.Note ?? DBNull.Value);

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task DeleteEventoAsync(Guid eventoId, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var conn = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var tx = conn.BeginTransaction();

            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    DELETE FROM Foto WHERE EventoId = @id;
                    DELETE FROM Atleti WHERE EventoId = @id;
                    DELETE FROM Discipline WHERE EventoId = @id;
                    DELETE FROM Acquisti WHERE EventoId = @id;
                    DELETE FROM Eventi WHERE Id = @id;
                ";
                cmd.Parameters.AddWithValue("@id", eventoId.ToString());
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    #endregion

    #region Discipline

    public async Task<List<Disciplina>> GetDisciplineByEventoAsync(Guid eventoId, CancellationToken cancellationToken = default)
    {
        var list = new List<Disciplina>();
        await using var conn = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, EventoId, NomeDisciplina, Descrizione FROM Discipline WHERE EventoId = @evId ORDER BY NomeDisciplina;";
        cmd.Parameters.AddWithValue("@evId", eventoId.ToString());

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new Disciplina
            {
                Id = Guid.TryParse(reader.GetString(0), out var did) ? did : Guid.NewGuid(),
                EventoId = Guid.TryParse(reader.GetString(1), out var evid) ? evid : Guid.Empty,
                NomeDisciplina = reader.GetString(2),
                Descrizione = reader.IsDBNull(3) ? null : reader.GetString(3)
            });
        }

        return list;
    }

    public async Task UpsertDisciplinaAsync(Disciplina disciplina, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var conn = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO Discipline (Id, EventoId, NomeDisciplina, Descrizione)
                VALUES (@id, @evId, @nome, @desc)
                ON CONFLICT(Id) DO UPDATE SET
                    NomeDisciplina = excluded.NomeDisciplina,
                    Descrizione = excluded.Descrizione;
            ";
            cmd.Parameters.AddWithValue("@id", disciplina.Id.ToString());
            cmd.Parameters.AddWithValue("@evId", disciplina.EventoId.ToString());
            cmd.Parameters.AddWithValue("@nome", disciplina.NomeDisciplina);
            cmd.Parameters.AddWithValue("@desc", (object?)disciplina.Descrizione ?? DBNull.Value);

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task DeleteDisciplinaAsync(Guid disciplinaId, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var conn = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Discipline WHERE Id = @id;";
            cmd.Parameters.AddWithValue("@id", disciplinaId.ToString());
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    #endregion

    #region Atleti

    public async Task<List<Atleta>> GetAtletiByEventoAsync(Guid eventoId, CancellationToken cancellationToken = default)
    {
        var list = new List<Atleta>();
        await using var conn = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, EventoId, NumeroPettorale, Nome, Cognome, Categoria, Note FROM Atleti WHERE EventoId = @evId ORDER BY Cognome, Nome;";
        cmd.Parameters.AddWithValue("@evId", eventoId.ToString());

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new Atleta
            {
                Id = Guid.TryParse(reader.GetString(0), out var aid) ? aid : Guid.NewGuid(),
                EventoId = Guid.TryParse(reader.GetString(1), out var evid) ? evid : Guid.Empty,
                NumeroPettorale = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                Nome = reader.GetString(3),
                Cognome = reader.GetString(4),
                Categoria = reader.IsDBNull(5) ? null : reader.GetString(5),
                Note = reader.IsDBNull(6) ? null : reader.GetString(6)
            });
        }

        return list;
    }

    public async Task UpsertAtletaAsync(Atleta atleta, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var conn = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO Atleti (Id, EventoId, NumeroPettorale, Nome, Cognome, Categoria, Note, TimestampIngestione)
                VALUES (@id, @evId, @pett, @nome, @cognome, @cat, @note, datetime('now'))
                ON CONFLICT(Id) DO UPDATE SET
                    NumeroPettorale = excluded.NumeroPettorale,
                    Nome = excluded.Nome,
                    Cognome = excluded.Cognome,
                    Categoria = excluded.Categoria,
                    Note = excluded.Note,
                    TimestampIngestione = excluded.TimestampIngestione;
            ";
            cmd.Parameters.AddWithValue("@id", atleta.Id.ToString());
            cmd.Parameters.AddWithValue("@evId", atleta.EventoId.ToString());
            cmd.Parameters.AddWithValue("@pett", (object?)atleta.NumeroPettorale ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@nome", atleta.Nome);
            cmd.Parameters.AddWithValue("@cognome", atleta.Cognome);
            cmd.Parameters.AddWithValue("@cat", (object?)atleta.Categoria ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@note", (object?)atleta.Note ?? DBNull.Value);

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task DeleteAtletaAsync(Guid atletaId, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var conn = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Atleti WHERE Id = @id;";
            cmd.Parameters.AddWithValue("@id", atletaId.ToString());
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    #endregion

    #region Foto

    public async Task<List<Foto>> GetFotoByEventoAsync(Guid eventoId, CancellationToken cancellationToken = default)
    {
        var list = new List<Foto>();
        await using var conn = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, EventoId, AtletaId, DisciplinaId, NomeFileOriginale, PathRelativo, Formato,
                   DataScatto, Fotografo, WatermarkApplicato, DimensioneByte, HashMd5, IsPremiazione
            FROM Foto
            WHERE EventoId = @evId;
        ";
        cmd.Parameters.AddWithValue("@evId", eventoId.ToString());

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(ReadFoto(reader));
        }

        return list;
    }

    public async Task<List<Foto>> GetFotoByAtletaAsync(Guid atletaId, CancellationToken cancellationToken = default)
    {
        var list = new List<Foto>();
        await using var conn = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, EventoId, AtletaId, DisciplinaId, NomeFileOriginale, PathRelativo, Formato,
                   DataScatto, Fotografo, WatermarkApplicato, DimensioneByte, HashMd5, IsPremiazione
            FROM Foto
            WHERE AtletaId = @atId;
        ";
        cmd.Parameters.AddWithValue("@atId", atletaId.ToString());

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(ReadFoto(reader));
        }

        return list;
    }

    public async Task AddFotoBatchAsync(IEnumerable<Foto> fotoList, CancellationToken cancellationToken = default)
    {
        var batch = fotoList.ToList();
        if (batch.Count == 0) return;

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var conn = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var tx = conn.BeginTransaction();

            var sql = @"
                INSERT INTO Foto (
                    Id, EventoId, AtletaId, DisciplinaId, NomeFileOriginale, PathRelativo, Formato,
                    DataScatto, Fotografo, WatermarkApplicato, DimensioneByte, HashMd5, IsPremiazione
                ) VALUES (
                    @id, @evId, @atId, @discId, @nome, @path, @formato,
                    @scatto, @foto, @wm, @bytes, @hash, @prem
                ) ON CONFLICT(Id) DO UPDATE SET
                    PathRelativo = excluded.PathRelativo,
                    WatermarkApplicato = excluded.WatermarkApplicato,
                    DimensioneByte = excluded.DimensioneByte,
                    HashMd5 = excluded.HashMd5,
                    Fotografo = excluded.Fotografo,
                    IsPremiazione = excluded.IsPremiazione;
            ";

            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;

            var pId = cmd.Parameters.Add("@id", SqliteType.Text);
            var pEvId = cmd.Parameters.Add("@evId", SqliteType.Text);
            var pAtId = cmd.Parameters.Add("@atId", SqliteType.Text);
            var pDiscId = cmd.Parameters.Add("@discId", SqliteType.Text);
            var pNome = cmd.Parameters.Add("@nome", SqliteType.Text);
            var pPath = cmd.Parameters.Add("@path", SqliteType.Text);
            var pFormato = cmd.Parameters.Add("@formato", SqliteType.Text);
            var pScatto = cmd.Parameters.Add("@scatto", SqliteType.Text);
            var pFoto = cmd.Parameters.Add("@foto", SqliteType.Text);
            var pWm = cmd.Parameters.Add("@wm", SqliteType.Integer);
            var pBytes = cmd.Parameters.Add("@bytes", SqliteType.Integer);
            var pHash = cmd.Parameters.Add("@hash", SqliteType.Text);
            var pPrem = cmd.Parameters.Add("@prem", SqliteType.Integer);

            foreach (var foto in batch)
            {
                pId.Value = foto.Id.ToString();
                pEvId.Value = foto.EventoId.ToString();
                pAtId.Value = foto.AtletaId.ToString();
                pDiscId.Value = foto.DisciplinaId.ToString();
                pNome.Value = foto.NomeFileOriginale;
                pPath.Value = foto.PathRelativo;
                pFormato.Value = foto.Formato;
                pScatto.Value = foto.DataScatto.HasValue ? foto.DataScatto.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : DBNull.Value;
                pFoto.Value = (object?)foto.Fotografo ?? DBNull.Value;
                pWm.Value = foto.WatermarkApplicato ? 1 : 0;
                pBytes.Value = foto.DimensioneByte;
                pHash.Value = (object?)foto.HashMd5 ?? DBNull.Value;
                pPrem.Value = foto.IsPremiazione ? 1 : 0;

                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task UpdateFotoAsync(Foto foto, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var conn = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE Foto SET
                    PathRelativo = @path,
                    WatermarkApplicato = @wm,
                    DimensioneByte = @bytes,
                    HashMd5 = @hash,
                    Fotografo = @foto,
                    IsPremiazione = @prem
                WHERE Id = @id;
            ";
            cmd.Parameters.AddWithValue("@id", foto.Id.ToString());
            cmd.Parameters.AddWithValue("@path", foto.PathRelativo);
            cmd.Parameters.AddWithValue("@wm", foto.WatermarkApplicato ? 1 : 0);
            cmd.Parameters.AddWithValue("@bytes", foto.DimensioneByte);
            cmd.Parameters.AddWithValue("@hash", (object?)foto.HashMd5 ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@foto", (object?)foto.Fotografo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@prem", foto.IsPremiazione ? 1 : 0);

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task DeleteFotoAsync(Guid fotoId, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var conn = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Foto WHERE Id = @id;";
            cmd.Parameters.AddWithValue("@id", fotoId.ToString());
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static Foto ReadFoto(SqliteDataReader reader)
    {
        DateTime? scatto = null;
        if (!reader.IsDBNull(7))
        {
            if (DateTime.TryParse(reader.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            {
                scatto = dt;
            }
        }

        var id = Guid.TryParse(reader.GetString(0), out var parsedId) ? parsedId : Guid.NewGuid();
        var evId = Guid.TryParse(reader.GetString(1), out var parsedEvId) ? parsedEvId : Guid.Empty;
        var atId = Guid.TryParse(reader.GetString(2), out var parsedAtId) ? parsedAtId : Guid.Empty;
        var discId = Guid.TryParse(reader.GetString(3), out var parsedDiscId) ? parsedDiscId : Guid.Empty;

        return new Foto
        {
            Id = id,
            EventoId = evId,
            AtletaId = atId,
            DisciplinaId = discId,
            NomeFileOriginale = reader.GetString(4),
            PathRelativo = reader.GetString(5),
            Formato = reader.GetString(6),
            DataScatto = scatto,
            Fotografo = reader.IsDBNull(8) ? null : reader.GetString(8),
            WatermarkApplicato = reader.GetInt32(9) != 0,
            DimensioneByte = reader.GetInt64(10),
            HashMd5 = reader.IsDBNull(11) ? null : reader.GetString(11),
            IsPremiazione = reader.GetInt32(12) != 0
        };
    }

    #endregion

    #region Catalogo Prezzi

    public async Task<List<PrezzoCatalogoItem>> GetCatalogoPrezziAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<PrezzoCatalogoItem>();
        await using var conn = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Categoria, Nome, Prezzo, QuantitaFoto, Descrizione FROM ListinoPrezzi;";

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            Enum.TryParse<CategoriaPrezzo>(reader.GetString(1), out var cat);
            list.Add(new PrezzoCatalogoItem
            {
                Id = Guid.Parse(reader.GetString(0)),
                Categoria = cat,
                Nome = reader.GetString(2),
                Prezzo = Convert.ToDecimal(reader.GetDouble(3)),
                QuantitaFotoIncluse = reader.GetInt32(4),
                Descrizione = reader.IsDBNull(5) ? null : reader.GetString(5)
            });
        }

        return list;
    }

    public async Task SaveCatalogoPrezziAsync(IEnumerable<PrezzoCatalogoItem> items, CancellationToken cancellationToken = default)
    {
        var itemList = items.ToList();
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var conn = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var tx = conn.BeginTransaction();

            await using (var delCmd = conn.CreateCommand())
            {
                delCmd.Transaction = tx;
                delCmd.CommandText = "DELETE FROM ListinoPrezzi;";
                await delCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            foreach (var item in itemList)
            {
                await using var insCmd = conn.CreateCommand();
                insCmd.Transaction = tx;
                insCmd.CommandText = @"
                    INSERT INTO ListinoPrezzi (Id, Categoria, Nome, Prezzo, QuantitaFoto, Descrizione)
                    VALUES (@id, @cat, @nome, @prezzo, @qta, @desc);
                ";
                insCmd.Parameters.AddWithValue("@id", item.Id.ToString());
                insCmd.Parameters.AddWithValue("@cat", item.Categoria.ToString());
                insCmd.Parameters.AddWithValue("@nome", item.Nome);
                insCmd.Parameters.AddWithValue("@prezzo", (double)item.Prezzo);
                insCmd.Parameters.AddWithValue("@qta", item.QuantitaFotoIncluse);
                insCmd.Parameters.AddWithValue("@desc", (object?)item.Descrizione ?? DBNull.Value);
                await insCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task UpsertPrezzoCatalogoItemAsync(PrezzoCatalogoItem item, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var conn = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO ListinoPrezzi (Id, Categoria, Nome, Prezzo, QuantitaFoto, Descrizione)
                VALUES (@id, @cat, @nome, @prezzo, @qta, @desc)
                ON CONFLICT(Id) DO UPDATE SET
                    Categoria = excluded.Categoria,
                    Nome = excluded.Nome,
                    Prezzo = excluded.Prezzo,
                    QuantitaFoto = excluded.QuantitaFoto,
                    Descrizione = excluded.Descrizione;
            ";
            cmd.Parameters.AddWithValue("@id", item.Id.ToString());
            cmd.Parameters.AddWithValue("@cat", item.Categoria.ToString());
            cmd.Parameters.AddWithValue("@nome", item.Nome);
            cmd.Parameters.AddWithValue("@prezzo", (double)item.Prezzo);
            cmd.Parameters.AddWithValue("@qta", item.QuantitaFotoIncluse);
            cmd.Parameters.AddWithValue("@desc", (object?)item.Descrizione ?? DBNull.Value);

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task DeletePrezzoCatalogoItemAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var conn = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM ListinoPrezzi WHERE Id = @id;";
            cmd.Parameters.AddWithValue("@id", itemId.ToString());
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    #endregion

    #region Acquisti

    public async Task<List<AcquistoFoto>> GetAcquistiByEventoAsync(Guid eventoId, CancellationToken cancellationToken = default)
    {
        var list = new List<AcquistoFoto>();
        await using var conn = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, EventoId, DataAcquisto, AtletaId, NomeAtleta, NumeroPettorale,
                   DisciplinaId, NomeDisciplina, TotaleQuantita, TotaleCalcolato, TotalePagato,
                   EmailCliente, TelefonoCliente, InteraCartella, FileFotoSelezionate,
                   CartellaPathRiferimento, VociJson, Note, Stato
            FROM Acquisti
            WHERE EventoId = @evId
            ORDER BY DataAcquisto DESC;
        ";
        cmd.Parameters.AddWithValue("@evId", eventoId.ToString());

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var dataAcquisto = DateTime.TryParse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.None, out var da) ? da : DateTime.Now;
            var atletaId = Guid.TryParse(reader.GetString(3), out var aid) ? aid : Guid.Empty;
            Guid? disciplinaId = reader.IsDBNull(6) ? null : (Guid.TryParse(reader.GetString(6), out var did) ? did : null);

            var filesRaw = reader.IsDBNull(14) ? string.Empty : reader.GetString(14);
            var fileList = string.IsNullOrWhiteSpace(filesRaw)
                ? new List<string>()
                : filesRaw.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();

            var vociJson = reader.IsDBNull(16) ? null : reader.GetString(16);
            List<VoceAcquisto> voci = new();
            if (!string.IsNullOrWhiteSpace(vociJson))
            {
                try { voci = JsonSerializer.Deserialize<List<VoceAcquisto>>(vociJson) ?? new(); } catch { }
            }

            list.Add(new AcquistoFoto
            {
                Id = Guid.TryParse(reader.GetString(0), out var id) ? id : Guid.NewGuid(),
                EventoId = Guid.TryParse(reader.GetString(1), out var evId) ? evId : Guid.Empty,
                DataAcquisto = dataAcquisto,
                AtletaId = atletaId,
                NomeAtleta = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                NumeroPettorale = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                DisciplinaId = disciplinaId,
                NomeDisciplina = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                TotaleQuantita = reader.GetInt32(8),
                TotaleCalcolato = Convert.ToDecimal(reader.GetDouble(9)),
                TotalePagato = Convert.ToDecimal(reader.GetDouble(10)),
                EmailCliente = reader.IsDBNull(11) ? string.Empty : reader.GetString(11),
                TelefonoCliente = reader.IsDBNull(12) ? string.Empty : reader.GetString(12),
                InteraCartella = reader.GetInt32(13) != 0,
                FileFotoSelezionate = fileList,
                CartellaPathRiferimento = reader.IsDBNull(15) ? null : reader.GetString(15),
                Voci = voci,
                Note = reader.IsDBNull(17) ? null : reader.GetString(17),
                Stato = reader.GetString(18)
            });
        }

        return list;
    }

    public async Task UpsertAcquistoFotoAsync(AcquistoFoto acquisto, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var filesStr = acquisto.FileFotoSelezionate != null && acquisto.FileFotoSelezionate.Count > 0
                ? string.Join(";", acquisto.FileFotoSelezionate)
                : string.Empty;

            var vociJson = acquisto.Voci != null && acquisto.Voci.Count > 0
                ? JsonSerializer.Serialize(acquisto.Voci)
                : "[]";

            await using var conn = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO Acquisti (
                    Id, EventoId, DataAcquisto, AtletaId, NomeAtleta, NumeroPettorale,
                    DisciplinaId, NomeDisciplina, TotaleQuantita, TotaleCalcolato, TotalePagato,
                    EmailCliente, TelefonoCliente, InteraCartella, FileFotoSelezionate,
                    CartellaPathRiferimento, VociJson, VociSommario, Note, Stato
                ) VALUES (
                    @id, @evId, @data, @atId, @atNome, @pett,
                    @discId, @discNome, @qta, @totCalc, @totPag,
                    @email, @tel, @intera, @files,
                    @path, @vociJson, @vociSomm, @note, @stato
                ) ON CONFLICT(Id) DO UPDATE SET
                    DataAcquisto = excluded.DataAcquisto,
                    AtletaId = excluded.AtletaId,
                    NomeAtleta = excluded.NomeAtleta,
                    NumeroPettorale = excluded.NumeroPettorale,
                    DisciplinaId = excluded.DisciplinaId,
                    NomeDisciplina = excluded.NomeDisciplina,
                    TotaleQuantita = excluded.TotaleQuantita,
                    TotaleCalcolato = excluded.TotaleCalcolato,
                    TotalePagato = excluded.TotalePagato,
                    EmailCliente = excluded.EmailCliente,
                    TelefonoCliente = excluded.TelefonoCliente,
                    InteraCartella = excluded.InteraCartella,
                    FileFotoSelezionate = excluded.FileFotoSelezionate,
                    CartellaPathRiferimento = excluded.CartellaPathRiferimento,
                    VociJson = excluded.VociJson,
                    VociSommario = excluded.VociSommario,
                    Note = excluded.Note,
                    Stato = excluded.Stato;
            ";

            cmd.Parameters.AddWithValue("@id", acquisto.Id.ToString());
            cmd.Parameters.AddWithValue("@evId", acquisto.EventoId.ToString());
            cmd.Parameters.AddWithValue("@data", acquisto.DataAcquisto.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@atId", acquisto.AtletaId.ToString());
            cmd.Parameters.AddWithValue("@atNome", acquisto.NomeAtleta);
            cmd.Parameters.AddWithValue("@pett", (object?)acquisto.NumeroPettorale ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@discId", acquisto.DisciplinaId.HasValue ? (object)acquisto.DisciplinaId.Value.ToString() : DBNull.Value);
            cmd.Parameters.AddWithValue("@discNome", (object?)acquisto.NomeDisciplina ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@qta", acquisto.TotaleQuantita);
            cmd.Parameters.AddWithValue("@totCalc", (double)acquisto.TotaleCalcolato);
            cmd.Parameters.AddWithValue("@totPag", (double)acquisto.TotalePagato);
            cmd.Parameters.AddWithValue("@email", (object?)acquisto.EmailCliente ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@tel", (object?)acquisto.TelefonoCliente ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@intera", acquisto.InteraCartella ? 1 : 0);
            cmd.Parameters.AddWithValue("@files", filesStr);
            cmd.Parameters.AddWithValue("@path", (object?)acquisto.CartellaPathRiferimento ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@vociJson", vociJson);
            cmd.Parameters.AddWithValue("@vociSomm", acquisto.VociSommarioDisplay);
            cmd.Parameters.AddWithValue("@note", (object?)acquisto.Note ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@stato", acquisto.Stato);

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task DeleteAcquistoFotoAsync(Guid acquistoId, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var conn = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Acquisti WHERE Id = @id;";
            cmd.Parameters.AddWithValue("@id", acquistoId.ToString());
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    #endregion

    #region Single-Pass Caricamento Consolidato

    public async Task<EventDataBundle> GetEventDataBundleAsync(Guid eventoId, CancellationToken cancellationToken = default)
    {
        var bundle = new EventDataBundle();
        await using var conn = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        // 1. Atleti
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT Id, EventoId, NumeroPettorale, Nome, Cognome, Categoria, Note FROM Atleti WHERE EventoId = @evId ORDER BY Cognome, Nome;";
            cmd.Parameters.AddWithValue("@evId", eventoId.ToString());
            await using var r = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await r.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                bundle.Atleti.Add(new Atleta
                {
                    Id = Guid.TryParse(r.GetString(0), out var aid) ? aid : Guid.NewGuid(),
                    EventoId = Guid.TryParse(r.GetString(1), out var evid) ? evid : Guid.Empty,
                    NumeroPettorale = r.IsDBNull(2) ? string.Empty : r.GetString(2),
                    Nome = r.GetString(3),
                    Cognome = r.GetString(4),
                    Categoria = r.IsDBNull(5) ? null : r.GetString(5),
                    Note = r.IsDBNull(6) ? null : r.GetString(6)
                });
            }
        }

        // 2. Discipline
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT Id, EventoId, NomeDisciplina, Descrizione FROM Discipline WHERE EventoId = @evId ORDER BY NomeDisciplina;";
            cmd.Parameters.AddWithValue("@evId", eventoId.ToString());
            await using var r = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await r.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                bundle.Discipline.Add(new Disciplina
                {
                    Id = Guid.TryParse(r.GetString(0), out var did) ? did : Guid.NewGuid(),
                    EventoId = Guid.TryParse(r.GetString(1), out var evid) ? evid : Guid.Empty,
                    NomeDisciplina = r.GetString(2),
                    Descrizione = r.IsDBNull(3) ? null : r.GetString(3)
                });
            }
        }

        // 3. Foto
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT Id, EventoId, AtletaId, DisciplinaId, NomeFileOriginale, PathRelativo, Formato,
                       DataScatto, Fotografo, WatermarkApplicato, DimensioneByte, HashMd5, IsPremiazione
                FROM Foto
                WHERE EventoId = @evId;
            ";
            cmd.Parameters.AddWithValue("@evId", eventoId.ToString());
            await using var r = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await r.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                bundle.Foto.Add(ReadFoto(r));
            }
        }

        // 4. Catalogo Prezzi
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT Id, Categoria, Nome, Prezzo, QuantitaFoto, Descrizione FROM ListinoPrezzi;";
            await using var r = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await r.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                Enum.TryParse<CategoriaPrezzo>(r.GetString(1), out var cat);
                bundle.CatalogoPrezzi.Add(new PrezzoCatalogoItem
                {
                    Id = Guid.Parse(r.GetString(0)),
                    Categoria = cat,
                    Nome = r.GetString(2),
                    Prezzo = Convert.ToDecimal(r.GetDouble(3)),
                    QuantitaFotoIncluse = r.GetInt32(4),
                    Descrizione = r.IsDBNull(5) ? null : r.GetString(5)
                });
            }
        }

        // 5. Acquisti
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT Id, EventoId, DataAcquisto, AtletaId, NomeAtleta, NumeroPettorale,
                       DisciplinaId, NomeDisciplina, TotaleQuantita, TotaleCalcolato, TotalePagato,
                       EmailCliente, TelefonoCliente, InteraCartella, FileFotoSelezionate,
                       CartellaPathRiferimento, VociJson, Note, Stato
                FROM Acquisti
                WHERE EventoId = @evId
                ORDER BY DataAcquisto DESC;
            ";
            cmd.Parameters.AddWithValue("@evId", eventoId.ToString());
            await using var r = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await r.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var filesRaw = r.IsDBNull(14) ? string.Empty : r.GetString(14);
                var fileList = string.IsNullOrWhiteSpace(filesRaw)
                    ? new List<string>()
                    : filesRaw.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();

                var vociJson = r.IsDBNull(16) ? null : r.GetString(16);
                List<VoceAcquisto> voci = new();
                if (!string.IsNullOrWhiteSpace(vociJson))
                {
                    try { voci = JsonSerializer.Deserialize<List<VoceAcquisto>>(vociJson) ?? new(); } catch { }
                }

                bundle.Acquisti.Add(new AcquistoFoto
                {
                    Id = Guid.TryParse(r.GetString(0), out var id) ? id : Guid.NewGuid(),
                    EventoId = Guid.TryParse(r.GetString(1), out var evId) ? evId : Guid.Empty,
                    DataAcquisto = DateTime.TryParse(r.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.None, out var da) ? da : DateTime.Now,
                    AtletaId = Guid.TryParse(r.GetString(3), out var aid) ? aid : Guid.Empty,
                    NomeAtleta = r.IsDBNull(4) ? string.Empty : r.GetString(4),
                    NumeroPettorale = r.IsDBNull(5) ? string.Empty : r.GetString(5),
                    DisciplinaId = r.IsDBNull(6) ? null : (Guid.TryParse(r.GetString(6), out var did) ? did : null),
                    NomeDisciplina = r.IsDBNull(7) ? string.Empty : r.GetString(7),
                    TotaleQuantita = r.GetInt32(8),
                    TotaleCalcolato = Convert.ToDecimal(r.GetDouble(9)),
                    TotalePagato = Convert.ToDecimal(r.GetDouble(10)),
                    EmailCliente = r.IsDBNull(11) ? string.Empty : r.GetString(11),
                    TelefonoCliente = r.IsDBNull(12) ? string.Empty : r.GetString(12),
                    InteraCartella = r.GetInt32(13) != 0,
                    FileFotoSelezionate = fileList,
                    CartellaPathRiferimento = r.IsDBNull(15) ? null : r.GetString(15),
                    Voci = voci,
                    Note = r.IsDBNull(17) ? null : r.GetString(17),
                    Stato = r.GetString(18)
                });
            }
        }

        return bundle;
    }

    #endregion
}
