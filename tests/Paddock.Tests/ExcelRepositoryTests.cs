using FluentAssertions;
using Paddock.Core.Models;
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
    public async Task EnsureDatabaseInitializedAsync_CreatesAllFiveSheets()
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
}

