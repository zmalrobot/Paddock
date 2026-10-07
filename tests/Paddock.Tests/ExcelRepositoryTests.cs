using FluentAssertions;
using Paddock.Core.Models;
using Paddock.Core.DTOs;
using Paddock.Infrastructure.Excel;
using Xunit;

namespace Paddock.Tests;

public class ExcelRepositoryTests : IDisposable
{
    private readonly string _testDbPath;

    public ExcelRepositoryTests()
    {
        var tempFolder = Path.Combine(Path.GetTempPath(), "Paddock_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);
        _testDbPath = Path.Combine(tempFolder, "TestDatabase.xlsx");
    }

    public void Dispose()
    {
        try
        {
            var dir = Path.GetDirectoryName(_testDbPath);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public async Task EnsureDatabaseInitializedAsync_CreatesAllSevenSheets()
    {
        // Arrange
        var repo = new ExcelRepository(_testDbPath);

        // Act
        await repo.EnsureDatabaseInitializedAsync();

        // Assert
        File.Exists(_testDbPath).Should().BeTrue();
        using var wb = new ClosedXML.Excel.XLWorkbook(_testDbPath);
        wb.Worksheets.Contains("Eventi").Should().BeTrue();
        wb.Worksheets.Contains("Discipline").Should().BeTrue();
        wb.Worksheets.Contains("Atleti").Should().BeTrue();
        wb.Worksheets.Contains("Foto").Should().BeTrue();
        wb.Worksheets.Contains("Impostazioni").Should().BeTrue();
        wb.Worksheets.Contains("ListinoPrezzi").Should().BeTrue();
        wb.Worksheets.Contains("Acquisti").Should().BeTrue();
    }

    [Fact]
    public async Task Evento_And_Children_CRUD_WorksAccurately()
    {
        // Arrange
        var repo = new ExcelRepository(_testDbPath);
        var evento = new Evento
        {
            NomeEvento = "Maratona delle Dolomiti 2026",
            DataInizio = new DateTime(2026, 7, 10),
            DataFine = new DateTime(2026, 7, 12),
            Luogo = "Corvara in Badia",
            CartellaDestinazioneRoot = @"C:\PhotoStorage\Dolomiti2026",
            Note = "Gara alpina ad alta quota"
        };

        // Act - Upsert Evento
        await repo.UpsertEventoAsync(evento);
        var eventi = await repo.GetEventiAsync();
        eventi.Should().ContainSingle(e => e.Id == evento.Id && e.NomeEvento == "Maratona delle Dolomiti 2026");

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
            NomeFileOriginale = "_DSC1001.JPG",
            PathRelativo = @"Maratona delle Dolomiti 2026\105_Rossi_Mario\Ciclismo Granfondo\Jpeg\_DSC1001.JPG",
            Formato = "JPEG",
            DataScatto = new DateTime(2026, 7, 10, 9, 30, 0),
            Fotografo = "Simone",
            WatermarkApplicato = true,
            DimensioneByte = 8_500_000,
            HashMd5 = "d41d8cd98f00b204e9800998ecf8427e"
        };
        var foto2 = new Foto
        {
            EventoId = evento.Id,
            AtletaId = atleta.Id,
            DisciplinaId = disciplina.Id,
            NomeFileOriginale = "_DSC1001.ARW",
            PathRelativo = @"Maratona delle Dolomiti 2026\105_Rossi_Mario\Ciclismo Granfondo\Raw\_DSC1001.ARW",
            Formato = "RAW",
            DataScatto = new DateTime(2026, 7, 10, 9, 30, 0),
            Fotografo = "Simone",
            WatermarkApplicato = false,
            DimensioneByte = 45_000_000,
            HashMd5 = "a41d8cd98f00b204e9800998ecf8427f"
        };
        await repo.AddFotoBatchAsync(new[] { foto1, foto2 });

        var fotoList = await repo.GetFotoByEventoAsync(evento.Id);
        fotoList.Should().HaveCount(2);

        // Verifica metriche aggregate calcolate dall'evento
        var reloadedEventi = await repo.GetEventiAsync();
        var reloadedEvent = reloadedEventi.First(e => e.Id == evento.Id);
        reloadedEvent.TotaleAtleti.Should().Be(1);
        reloadedEvent.TotaleDiscipline.Should().Be(1);
        reloadedEvent.TotaleFoto.Should().Be(2);
        reloadedEvent.TotaleByteOccupati.Should().Be(53_500_000);

        // Delete Evento e verifica cascata
        await repo.DeleteEventoAsync(evento.Id);
        var eventiAfterDelete = await repo.GetEventiAsync();
        eventiAfterDelete.Should().NotContain(e => e.Id == evento.Id);

        var atletiAfterDelete = await repo.GetAtletiByEventoAsync(evento.Id);
        atletiAfterDelete.Should().BeEmpty();

        var fotoAfterDelete = await repo.GetFotoByEventoAsync(evento.Id);
        fotoAfterDelete.Should().BeEmpty();
    }

    [Fact]
    public async Task ConcurrentAccess_DoesNotCorruptExcelDatabase()
    {
        // Arrange
        var repo = new ExcelRepository(_testDbPath);
        await repo.EnsureDatabaseInitializedAsync();

        var evento = new Evento { NomeEvento = "Test Concorrenza" };
        await repo.UpsertEventoAsync(evento);

        // Act - 10 compiti asincroni paralleli che inseriscono atleti contemporaneamente
        var tasks = Enumerable.Range(1, 10).Select(i => Task.Run(async () =>
        {
            var atleta = new Atleta
            {
                EventoId = evento.Id,
                NumeroPettorale = i.ToString(),
                Nome = $"Atleta_{i}",
                Cognome = "Test"
            };
            await repo.UpsertAtletaAsync(atleta);
        }));

        await Task.WhenAll(tasks);

        // Assert
        var atleti = await repo.GetAtletiByEventoAsync(evento.Id);
        atleti.Should().HaveCount(10);
    }

    [Fact]
    public async Task Settings_And_BasePath_Updates_WorkProperly()
    {
        // Arrange
        var repo = new ExcelRepository(_testDbPath);
        await repo.EnsureDatabaseInitializedAsync();

        // Act & Assert 1: Get/Set Setting
        var defaultBase = await repo.GetBasePathAsync();
        defaultBase.Should().NotBeNullOrWhiteSpace();

        var customPath = @"D:\ArchivioFoto2026";
        await repo.SetBasePathAsync(customPath);
        var retrievedPath = await repo.GetBasePathAsync();
        retrievedPath.Should().Be(customPath);

        // Act & Assert 2: UpdateAllEventRootsAsync updates both settings and existing events
        var ev1 = new Evento { NomeEvento = "Gara 1", CartellaDestinazioneRoot = @"C:\OldRoot" };
        var ev2 = new Evento { NomeEvento = "Gara 2", CartellaDestinazioneRoot = @"C:\OldRoot" };
        await repo.UpsertEventoAsync(ev1);
        await repo.UpsertEventoAsync(ev2);

        var newBase = @"E:\FotoSSD";
        await repo.UpdateAllEventRootsAsync(newBase);

        var updatedBase = await repo.GetBasePathAsync();
        updatedBase.Should().Be(newBase);

        var reloadedEvents = await repo.GetEventiAsync();
        reloadedEvents.Should().AllSatisfy(e => e.CartellaDestinazioneRoot.Should().Be(newBase));

        // Act & Assert 3: SwitchDatabaseAsync
        var switchDbPath = Path.Combine(Path.GetDirectoryName(_testDbPath)!, "SecondDatabase.xlsx");
        await repo.SwitchDatabaseAsync(switchDbPath);
        repo.DatabaseFilePath.Should().Be(switchDbPath);
        File.Exists(switchDbPath).Should().BeTrue();
    }

    [Fact]
    public async Task CatalogoPrezzi_CRUD_WorksAccurately()
    {
        // Arrange
        var repo = new ExcelRepository(_testDbPath);
        await repo.EnsureDatabaseInitializedAsync();

        // 1. Get initial default items
        var items = await repo.GetCatalogoPrezziAsync();
        items.Should().HaveCount(9);
        items.Should().Contain(i => i.Nome == "1 Foto" && i.Prezzo == 5.00m && i.Categoria == CategoriaPrezzo.FotoSingola);
        items.Should().Contain(i => i.Nome == "1 Percorso" && i.Prezzo == 15.00m && i.Categoria == CategoriaPrezzo.PacchettoGara);
        items.Should().Contain(i => i.Nome == "Giornata intera (Tutti i percorsi)" && i.Prezzo == 35.00m && i.Categoria == CategoriaPrezzo.PacchettoGara);
        items.Should().Contain(i => i.Nome == "Editing base (Luci e colore)" && i.Prezzo == 3.00m && i.Categoria == CategoriaPrezzo.EditingBase);
        items.Should().Contain(i => i.Nome == "Editing avanzato (rimozione oggetti)" && i.Prezzo == 6.00m && i.Categoria == CategoriaPrezzo.EditingAvanzato);

        // 2. Add custom item
        var customItem = new PrezzoCatalogoItem
        {
            Nome = "Pacchetto Super VIP 50 Foto",
            Categoria = CategoriaPrezzo.PacchettoFoto,
            Prezzo = 250.00m,
            QuantitaFotoIncluse = 50,
            Descrizione = "50 foto ritoccate"
        };
        await repo.UpsertPrezzoCatalogoItemAsync(customItem);

        var updated = await repo.GetCatalogoPrezziAsync();
        updated.Should().HaveCount(10);
        updated.Should().Contain(i => i.Nome == "Pacchetto Super VIP 50 Foto" && i.Prezzo == 250.00m);

        // 3. Delete custom item
        await repo.DeletePrezzoCatalogoItemAsync(customItem.Id);
        var afterDelete = await repo.GetCatalogoPrezziAsync();
        afterDelete.Should().HaveCount(9);
        afterDelete.Should().NotContain(i => i.Id == customItem.Id);
    }

    [Fact]
    public async Task AcquistiFoto_CRUD_WorksAccurately()
    {
        // Arrange
        var repo = new ExcelRepository(_testDbPath);
        await repo.EnsureDatabaseInitializedAsync();

        var eventoId = Guid.NewGuid();
        var atletaId = Guid.NewGuid();

        var acquisto = new AcquistoFoto
        {
            EventoId = eventoId,
            AtletaId = atletaId,
            NomeAtleta = "Mario Rossi",
            NumeroPettorale = "42",
            NomeDisciplina = "Slalom Gigante",
            Voci = new List<VoceAcquisto>
            {
                new() { NomeArticolo = "Foto Singola", PrezzoUnitario = 10.00m, Quantita = 2 },
                new() { NomeArticolo = "Editing Base", PrezzoUnitario = 5.00m, Quantita = 1 }
            },
            TotaleQuantita = 3,
            TotaleCalcolato = 25.00m,
            TotalePagato = 20.00m, // sconto
            EmailCliente = "mario.rossi@example.com",
            TelefonoCliente = "+39 333 1234567",
            InteraCartella = false,
            FileFotoSelezionate = new List<string> { "IMG_001.JPG", "IMG_002.JPG" },
            Note = "Consegna urgente",
            Stato = "Completato"
        };

        // Act 1: Insert
        await repo.UpsertAcquistoFotoAsync(acquisto);

        // Assert 1: Query by Evento
        var list = await repo.GetAcquistiByEventoAsync(eventoId);
        list.Should().HaveCount(1);
        var loaded = list[0];
        loaded.Id.Should().Be(acquisto.Id);
        loaded.NomeAtleta.Should().Be("Mario Rossi");
        loaded.NumeroPettorale.Should().Be("42");
        loaded.TotalePagato.Should().Be(20.00m);
        loaded.EmailCliente.Should().Be("mario.rossi@example.com");
        loaded.Voci.Should().HaveCount(2);
        loaded.FileFotoSelezionate.Should().HaveCount(2);

        // Act 2: Delete
        await repo.DeleteAcquistoFotoAsync(acquisto.Id);
        var emptyList = await repo.GetAcquistiByEventoAsync(eventoId);
        emptyList.Should().BeEmpty();
    }

    [Fact]
    public async Task ExcelRepository_GetEventDataBundleAsync_RetrievesAllData_InSinglePass()
    {
        // Arrange
        var repo = new ExcelRepository(_testDbPath);
        await repo.EnsureDatabaseInitializedAsync();

        var eventoId = Guid.NewGuid();
        var altroEventoId = Guid.NewGuid();

        var evento = new Evento { Id = eventoId, NomeEvento = "Coppa del Mondo 2026" };
        await repo.UpsertEventoAsync(evento);

        var atleta1 = new Atleta { EventoId = eventoId, NumeroPettorale = "101", Nome = "Sofia", Cognome = "Goggia" };
        var atleta2 = new Atleta { EventoId = altroEventoId, NumeroPettorale = "999", Nome = "Altro", Cognome = "Atleta" };
        await repo.UpsertAtletaAsync(atleta1);
        await repo.UpsertAtletaAsync(atleta2);

        var disciplina1 = new Disciplina { EventoId = eventoId, NomeDisciplina = "Discesa Libera" };
        var disciplina2 = new Disciplina { EventoId = altroEventoId, NomeDisciplina = "Altra Disciplina" };
        await repo.UpsertDisciplinaAsync(disciplina1);
        await repo.UpsertDisciplinaAsync(disciplina2);

        var foto1 = new Foto { EventoId = eventoId, AtletaId = atleta1.Id, DisciplinaId = disciplina1.Id, NomeFileOriginale = "SG01.jpg", PathRelativo = "SG01.jpg" };
        var foto2 = new Foto { EventoId = altroEventoId, NomeFileOriginale = "OTHER.jpg", PathRelativo = "OTHER.jpg" };
        await repo.AddFotoBatchAsync(new[] { foto1, foto2 });

        var acquisto1 = new AcquistoFoto
        {
            EventoId = eventoId,
            AtletaId = atleta1.Id,
            NomeAtleta = "Sofia Goggia",
            NumeroPettorale = "101",
            TotalePagato = 35.00m
        };
        var acquisto2 = new AcquistoFoto { EventoId = altroEventoId, NomeAtleta = "Altro", TotalePagato = 10.00m };
        await repo.UpsertAcquistoFotoAsync(acquisto1);
        await repo.UpsertAcquistoFotoAsync(acquisto2);

        // Act
        var bundle = await repo.GetEventDataBundleAsync(eventoId);

        // Assert
        bundle.Should().NotBeNull();
        bundle.Atleti.Should().HaveCount(1);
        bundle.Atleti[0].NumeroPettorale.Should().Be("101");

        bundle.Discipline.Should().HaveCount(1);
        bundle.Discipline[0].NomeDisciplina.Should().Be("Discesa Libera");

        bundle.Foto.Should().HaveCount(1);
        bundle.Foto[0].NomeFileOriginale.Should().Be("SG01.jpg");

        bundle.Acquisti.Should().HaveCount(1);
        bundle.Acquisti[0].NomeAtleta.Should().Be("Sofia Goggia");
        bundle.Acquisti[0].TotalePagato.Should().Be(35.00m);

        bundle.CatalogoPrezzi.Should().NotBeEmpty();
        bundle.CatalogoPrezzi.Should().HaveCount(9);
    }
}

