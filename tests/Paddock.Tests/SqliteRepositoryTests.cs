using FluentAssertions;
using Paddock.Core.Models;
using Paddock.Infrastructure.Sqlite;
using Xunit;

namespace Paddock.Tests;

public class SqliteRepositoryTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly string _tempFolder;

    public SqliteRepositoryTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), "Paddock_SqliteTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempFolder);
        _testDbPath = Path.Combine(_tempFolder, "TestDatabase.db");
    }

    public void Dispose()
    {
        try
        {
            // Forza garbage collection per rilasciare connessioni SQLite aperte nel pool di test
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            GC.Collect();
            GC.WaitForPendingFinalizers();

            if (Directory.Exists(_tempFolder))
            {
                Directory.Delete(_tempFolder, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public async Task EnsureDatabaseInitializedAsync_CreatesAllTablesAndIndexes()
    {
        // Arrange
        var repo = new SqliteRepository(_testDbPath);

        // Act
        await repo.EnsureDatabaseInitializedAsync();

        // Assert
        File.Exists(_testDbPath).Should().BeTrue();
        repo.EngineType.Should().Be(Paddock.Core.Interfaces.DatabaseEngine.Sqlite);

        // Verifica che le impostazioni di default siano state create
        var basePath = await repo.GetBasePathAsync();
        basePath.Should().Be(@"..\Foto");

        // Verifica che il listino di default contenga gli articoli standard
        var catalogo = await repo.GetCatalogoPrezziAsync();
        catalogo.Should().NotBeEmpty();
        catalogo.Should().Contain(c => c.Nome == "1 Percorso");
    }

    [Fact]
    public async Task Evento_And_Children_CRUD_WorksAccurately()
    {
        // Arrange
        var repo = new SqliteRepository(_testDbPath);
        await repo.EnsureDatabaseInitializedAsync();

        var evento = new Evento
        {
            NomeEvento = "Maratona delle Dolomiti SQLite 2026",
            DataInizio = new DateTime(2026, 7, 10),
            DataFine = new DateTime(2026, 7, 12),
            Luogo = "Corvara in Badia",
            CartellaDestinazioneRoot = @"C:\PhotoStorage\Dolomiti2026",
            Note = "Gara alpina ad alta quota"
        };

        // Act - Upsert Evento
        await repo.UpsertEventoAsync(evento);
        var eventi = await repo.GetEventiAsync();
        eventi.Should().ContainSingle(e => e.Id == evento.Id && e.NomeEvento == "Maratona delle Dolomiti SQLite 2026");

        var retrievedEvento = await repo.GetEventoByIdAsync(evento.Id);
        retrievedEvento.Should().NotBeNull();
        retrievedEvento!.NomeEvento.Should().Be("Maratona delle Dolomiti SQLite 2026");

        // Act - Upsert Disciplina
        var disciplina = new Disciplina
        {
            EventoId = evento.Id,
            NomeDisciplina = "Ciclismo Granfondo",
            Descrizione = "Percorso lungo 138km"
        };
        await repo.UpsertDisciplinaAsync(disciplina);
        var discipline = await repo.GetDisciplineByEventoAsync(evento.Id);
        discipline.Should().ContainSingle(d => d.Id == disciplina.Id && d.NomeDisciplina == "Ciclismo Granfondo");

        // Act - Upsert Atleta
        var atleta = new Atleta
        {
            EventoId = evento.Id,
            NumeroPettorale = "105",
            Nome = "Mario",
            Cognome = "Rossi",
            Categoria = "Master M40"
        };
        await repo.UpsertAtletaAsync(atleta);
        var atleti = await repo.GetAtletiByEventoAsync(evento.Id);
        atleti.Should().ContainSingle(a => a.Id == atleta.Id && a.NumeroPettorale == "105");

        // Act - Add Foto Batch
        var foto1 = new Foto
        {
            EventoId = evento.Id,
            AtletaId = atleta.Id,
            DisciplinaId = disciplina.Id,
            NomeFileOriginale = "IMG_001.JPG",
            PathRelativo = @"2026\105_Rossi\IMG_001.JPG",
            Formato = "JPEG",
            DataScatto = new DateTime(2026, 7, 11, 10, 30, 0),
            Fotografo = "Simone",
            WatermarkApplicato = true,
            DimensioneByte = 5000000,
            HashMd5 = "ABC123MD5"
        };
        await repo.AddFotoBatchAsync(new[] { foto1 });

        var fotoList = await repo.GetFotoByEventoAsync(evento.Id);
        fotoList.Should().ContainSingle(f => f.Id == foto1.Id && f.NomeFileOriginale == "IMG_001.JPG");

        var fotoAtleta = await repo.GetFotoByAtletaAsync(atleta.Id);
        fotoAtleta.Should().ContainSingle(f => f.Id == foto1.Id);

        // Aggiorna foto
        foto1.WatermarkApplicato = false;
        await repo.UpdateFotoAsync(foto1);
        var updatedFoto = (await repo.GetFotoByEventoAsync(evento.Id)).First(f => f.Id == foto1.Id);
        updatedFoto.WatermarkApplicato.Should().BeFalse();

        // Cancella foto
        await repo.DeleteFotoAsync(foto1.Id);
        (await repo.GetFotoByEventoAsync(evento.Id)).Should().BeEmpty();

        // Cancella Atleta
        await repo.DeleteAtletaAsync(atleta.Id);
        (await repo.GetAtletiByEventoAsync(evento.Id)).Should().BeEmpty();

        // Cancella Disciplina
        await repo.DeleteDisciplinaAsync(disciplina.Id);
        (await repo.GetDisciplineByEventoAsync(evento.Id)).Should().BeEmpty();

        // Cancella Evento
        await repo.DeleteEventoAsync(evento.Id);
        (await repo.GetEventiAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task AddFotoBatchAsync_InsertsMultiplePhotosInSingleTransaction()
    {
        // Arrange
        var repo = new SqliteRepository(_testDbPath);
        await repo.EnsureDatabaseInitializedAsync();

        var evento = new Evento { NomeEvento = "Bulk Test", Luogo = "Milano" };
        await repo.UpsertEventoAsync(evento);

        var disciplina = new Disciplina { EventoId = evento.Id, NomeDisciplina = "Corsa" };
        await repo.UpsertDisciplinaAsync(disciplina);

        var atleta = new Atleta { EventoId = evento.Id, Nome = "Luigi", Cognome = "Verdi" };
        await repo.UpsertAtletaAsync(atleta);

        var batch = new List<Foto>();
        for (int i = 0; i < 50; i++)
        {
            batch.Add(new Foto
            {
                EventoId = evento.Id,
                AtletaId = atleta.Id,
                DisciplinaId = disciplina.Id,
                NomeFileOriginale = $"PHOTO_{i:D4}.JPG",
                PathRelativo = $@"Bulk\PHOTO_{i:D4}.JPG",
                Formato = "JPEG",
                DimensioneByte = 1024 * i
            });
        }

        // Act
        await repo.AddFotoBatchAsync(batch);

        // Assert
        var fotoList = await repo.GetFotoByEventoAsync(evento.Id);
        fotoList.Should().HaveCount(50);
    }

    [Fact]
    public async Task GetEventDataBundleAsync_RetrievesConsolidatedBundleAccurately()
    {
        // Arrange
        var repo = new SqliteRepository(_testDbPath);
        await repo.EnsureDatabaseInitializedAsync();

        var evento = new Evento { NomeEvento = "Bundle Test Event", Luogo = "Roma" };
        await repo.UpsertEventoAsync(evento);

        var disciplina = new Disciplina { EventoId = evento.Id, NomeDisciplina = "Nuoto" };
        await repo.UpsertDisciplinaAsync(disciplina);

        var atleta = new Atleta { EventoId = evento.Id, Nome = "Anna", Cognome = "Bianchi", NumeroPettorale = "42" };
        await repo.UpsertAtletaAsync(atleta);

        var foto = new Foto
        {
            EventoId = evento.Id,
            AtletaId = atleta.Id,
            DisciplinaId = disciplina.Id,
            NomeFileOriginale = "SWIM_01.JPG",
            PathRelativo = @"Nuoto\SWIM_01.JPG"
        };
        await repo.AddFotoBatchAsync(new[] { foto });

        var acquisto = new AcquistoFoto
        {
            EventoId = evento.Id,
            AtletaId = atleta.Id,
            NomeAtleta = "Anna Bianchi",
            NumeroPettorale = "42",
            TotaleCalcolato = 15.00m,
            TotalePagato = 15.00m
        };
        await repo.UpsertAcquistoFotoAsync(acquisto);

        // Act
        var bundle = await repo.GetEventDataBundleAsync(evento.Id);

        // Assert
        bundle.Should().NotBeNull();
        bundle.Atleti.Should().ContainSingle(a => a.Id == atleta.Id);
        bundle.Discipline.Should().ContainSingle(d => d.Id == disciplina.Id);
        bundle.Foto.Should().ContainSingle(f => f.Id == foto.Id);
        bundle.Acquisti.Should().ContainSingle(acq => acq.Id == acquisto.Id);
        bundle.CatalogoPrezzi.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CatalogoPrezzi_And_Acquisti_CRUD_WorkAccurately()
    {
        // Arrange
        var repo = new SqliteRepository(_testDbPath);
        await repo.EnsureDatabaseInitializedAsync();

        var newItem = new PrezzoCatalogoItem
        {
            Nome = "Poster 50x70",
            Categoria = CategoriaPrezzo.FotoSingola,
            Prezzo = 25.50m,
            QuantitaFotoIncluse = 1,
            Descrizione = "Stampa su carta fine art"
        };

        // Act - Upsert Item
        await repo.UpsertPrezzoCatalogoItemAsync(newItem);
        var catalogo = await repo.GetCatalogoPrezziAsync();
        catalogo.Should().Contain(c => c.Id == newItem.Id && c.Nome == "Poster 50x70" && c.Prezzo == 25.50m);

        // Act - Delete Item
        await repo.DeletePrezzoCatalogoItemAsync(newItem.Id);
        (await repo.GetCatalogoPrezziAsync()).Should().NotContain(c => c.Id == newItem.Id);

        // Acquisti
        var evento = new Evento { NomeEvento = "Acquisto Test Event" };
        await repo.UpsertEventoAsync(evento);
        var evId = evento.Id;
        var atId = Guid.NewGuid();
        var acquisto = new AcquistoFoto
        {
            EventoId = evId,
            AtletaId = atId,
            NomeAtleta = "Marco Neri",
            NumeroPettorale = "99",
            EmailCliente = "marco@example.com",
            TotaleCalcolato = 50.00m,
            TotalePagato = 50.00m,
            Voci = new List<VoceAcquisto>
            {
                new VoceAcquisto { NomeArticolo = "Poster", Quantita = 2, PrezzoUnitario = 25.00m }
            }
        };

        await repo.UpsertAcquistoFotoAsync(acquisto);
        var acquisti = await repo.GetAcquistiByEventoAsync(evId);
        acquisti.Should().ContainSingle(a => a.Id == acquisto.Id && a.EmailCliente == "marco@example.com");
        acquisti[0].Voci.Should().HaveCount(1);
        acquisti[0].Voci[0].NomeArticolo.Should().Be("Poster");

        await repo.DeleteAcquistoFotoAsync(acquisto.Id);
        (await repo.GetAcquistiByEventoAsync(evId)).Should().BeEmpty();
    }

    [Fact]
    public async Task AddFotoBatchAsync_HandlesPremiazioniAndUnassignedPhotosWithoutForeignKeyError()
    {
        // Arrange
        var repo = new SqliteRepository(_testDbPath);
        await repo.EnsureDatabaseInitializedAsync();

        var evento = new Evento { NomeEvento = "Premiazioni Test", Luogo = "Milano" };
        await repo.UpsertEventoAsync(evento);

        // Foto Premiazioni: AtletaId e DisciplinaId vuoti (Guid.Empty), IsPremiazione = true
        var fotoPrem = new Foto
        {
            EventoId = evento.Id,
            AtletaId = Guid.Empty,
            DisciplinaId = Guid.Empty,
            NomeFileOriginale = "PREMIAZIONE_001.JPG",
            PathRelativo = @"Premiazioni\PREMIAZIONE_001.JPG",
            Formato = "JPEG",
            IsPremiazione = true,
            DimensioneByte = 4500000
        };

        // Foto orfana: AtletaId e DisciplinaId non presenti nelle relative tabelle
        var fotoOrfana = new Foto
        {
            EventoId = evento.Id,
            AtletaId = Guid.NewGuid(),
            DisciplinaId = Guid.NewGuid(),
            NomeFileOriginale = "GENERIC_002.JPG",
            PathRelativo = @"Generiche\GENERIC_002.JPG",
            Formato = "JPEG",
            IsPremiazione = false,
            DimensioneByte = 3200000
        };

        // Act
        await repo.AddFotoBatchAsync(new[] { fotoPrem, fotoOrfana });

        // Assert
        var fotoList = await repo.GetFotoByEventoAsync(evento.Id);
        fotoList.Should().HaveCount(2);

        var retrievedPrem = fotoList.First(f => f.Id == fotoPrem.Id);
        retrievedPrem.IsPremiazione.Should().BeTrue();
        retrievedPrem.AtletaId.Should().Be(Guid.Empty);
        retrievedPrem.DisciplinaId.Should().Be(Guid.Empty);

        var retrievedOrfana = fotoList.First(f => f.Id == fotoOrfana.Id);
        retrievedOrfana.NomeFileOriginale.Should().Be("GENERIC_002.JPG");
    }

    [Fact]
    public async Task DeleteEventoAsync_CascadesDeletionAcrossAllTablesAtomically()
    {
        // Arrange
        var repo = new SqliteRepository(_testDbPath);
        await repo.EnsureDatabaseInitializedAsync();

        var evento = new Evento { NomeEvento = "Evento Da Eliminare" };
        await repo.UpsertEventoAsync(evento);

        var disciplina = new Disciplina { EventoId = evento.Id, NomeDisciplina = "Nuoto" };
        await repo.UpsertDisciplinaAsync(disciplina);

        var atleta = new Atleta { EventoId = evento.Id, Nome = "Paolo", Cognome = "Bianchi" };
        await repo.UpsertAtletaAsync(atleta);

        var foto = new Foto { EventoId = evento.Id, AtletaId = atleta.Id, DisciplinaId = disciplina.Id, NomeFileOriginale = "F1.JPG", PathRelativo = "F1.JPG" };
        await repo.AddFotoBatchAsync(new[] { foto });

        var acquisto = new AcquistoFoto { EventoId = evento.Id, AtletaId = atleta.Id, TotaleCalcolato = 20, TotalePagato = 20 };
        await repo.UpsertAcquistoFotoAsync(acquisto);

        // Act
        await repo.DeleteEventoAsync(evento.Id);

        // Assert
        (await repo.GetEventiAsync()).Should().NotContain(e => e.Id == evento.Id);
        (await repo.GetDisciplineByEventoAsync(evento.Id)).Should().BeEmpty();
        (await repo.GetAtletiByEventoAsync(evento.Id)).Should().BeEmpty();
        (await repo.GetFotoByEventoAsync(evento.Id)).Should().BeEmpty();
        (await repo.GetAcquistiByEventoAsync(evento.Id)).Should().BeEmpty();
    }
}
