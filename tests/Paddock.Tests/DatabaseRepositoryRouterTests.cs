using FluentAssertions;
using Paddock.Core.Interfaces;
using Paddock.Core.Models;
using Paddock.Infrastructure.Excel;
using Paddock.Infrastructure.Services;
using Paddock.Infrastructure.Sqlite;
using Xunit;

namespace Paddock.Tests;

public class DatabaseRepositoryRouterTests : IDisposable
{
    private readonly string _tempFolder;

    public DatabaseRepositoryRouterTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), "Paddock_RouterTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempFolder);
    }

    public void Dispose()
    {
        try
        {
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

    [Theory]
    [InlineData("Paddock_Database.db", DatabaseEngine.Sqlite)]
    [InlineData("test.sqlite", DatabaseEngine.Sqlite)]
    [InlineData("data.sqlite3", DatabaseEngine.Sqlite)]
    [InlineData("custom.db3", DatabaseEngine.Sqlite)]
    [InlineData("Paddock_Database.xlsx", DatabaseEngine.Excel)]
    [InlineData("export.xlsm", DatabaseEngine.Excel)]
    [InlineData("old.xls", DatabaseEngine.Excel)]
    [InlineData("", DatabaseEngine.Sqlite)]
    [InlineData(null, DatabaseEngine.Sqlite)]
    public void DetectEngine_IdentifiesEngineAccurately(string? path, DatabaseEngine expectedEngine)
    {
        var result = DatabaseRepositoryRouter.DetectEngine(path);
        result.Should().Be(expectedEngine);
    }

    [Fact]
    public async Task Router_InitializesWithCorrectEngine_AndSwitchesDynamically()
    {
        // Arrange
        var sqlitePath = Path.Combine(_tempFolder, "DB1.db");
        var excelPath = Path.Combine(_tempFolder, "DB2.xlsx");

        var router = new DatabaseRepositoryRouter(sqlitePath);
        router.EngineType.Should().Be(DatabaseEngine.Sqlite);

        await router.EnsureDatabaseInitializedAsync();
        File.Exists(sqlitePath).Should().BeTrue();

        // Salva un evento in SQLite
        var evSqlite = new Evento { NomeEvento = "SQLite Event", Luogo = "Milano" };
        await router.UpsertEventoAsync(evSqlite);
        (await router.GetEventiAsync()).Should().ContainSingle(e => e.NomeEvento == "SQLite Event");

        // Act: switch a Excel
        await router.SwitchDatabaseAsync(excelPath);
        router.EngineType.Should().Be(DatabaseEngine.Excel);
        File.Exists(excelPath).Should().BeTrue();

        // Su Excel l'evento precedente non c'è ancora
        (await router.GetEventiAsync()).Should().BeEmpty();

        // Salva un evento in Excel
        var evExcel = new Evento { NomeEvento = "Excel Event", Luogo = "Torino" };
        await router.UpsertEventoAsync(evExcel);
        (await router.GetEventiAsync()).Should().ContainSingle(e => e.NomeEvento == "Excel Event");

        // Act: torna a SQLite
        await router.SwitchDatabaseAsync(sqlitePath);
        router.EngineType.Should().Be(DatabaseEngine.Sqlite);
        (await router.GetEventiAsync()).Should().ContainSingle(e => e.NomeEvento == "SQLite Event");
    }

    [Fact]
    public async Task MigrateExcelToSqliteAsync_MigratesAllDataAccurately()
    {
        // Arrange: Popola un database Excel con dati completi
        var excelPath = Path.Combine(_tempFolder, "Source.xlsx");
        var sqlitePath = Path.Combine(_tempFolder, "Target.db");

        var excelRepo = new ExcelRepository(excelPath);
        await excelRepo.EnsureDatabaseInitializedAsync();
        await excelRepo.SetBasePathAsync(@"D:\FotoArchivio");

        var evento = new Evento { NomeEvento = "Giro d'Italia", Luogo = "Roma" };
        await excelRepo.UpsertEventoAsync(evento);

        var disciplina = new Disciplina { EventoId = evento.Id, NomeDisciplina = "Crono" };
        await excelRepo.UpsertDisciplinaAsync(disciplina);

        var atleta = new Atleta { EventoId = evento.Id, Nome = "Filippo", Cognome = "Ganna", NumeroPettorale = "1" };
        await excelRepo.UpsertAtletaAsync(atleta);

        var foto = new Foto
        {
            EventoId = evento.Id,
            AtletaId = atleta.Id,
            DisciplinaId = disciplina.Id,
            NomeFileOriginale = "GANNA_01.JPG",
            PathRelativo = @"Crono\GANNA_01.JPG",
            DimensioneByte = 8192
        };

        var fotoPrem = new Foto
        {
            EventoId = evento.Id,
            AtletaId = Guid.Empty,
            DisciplinaId = Guid.Empty,
            NomeFileOriginale = "PODIO_01.JPG",
            PathRelativo = @"Premiazioni\PODIO_01.JPG",
            IsPremiazione = true,
            DimensioneByte = 12000
        };

        var fotoOrfana = new Foto
        {
            EventoId = evento.Id,
            AtletaId = Guid.NewGuid(), // Atleta non presente nel foglio Atleti
            DisciplinaId = Guid.Empty,
            NomeFileOriginale = "PAESAGGIO.JPG",
            PathRelativo = @"Generiche\PAESAGGIO.JPG",
            DimensioneByte = 15000
        };

        await excelRepo.AddFotoBatchAsync(new[] { foto, fotoPrem, fotoOrfana });

        var acquisto = new AcquistoFoto
        {
            EventoId = evento.Id,
            AtletaId = atleta.Id,
            NomeAtleta = "Filippo Ganna",
            TotaleCalcolato = 30m,
            TotalePagato = 30m,
            EmailCliente = "fan@cycling.com"
        };
        await excelRepo.UpsertAcquistoFotoAsync(acquisto);

        var router = new DatabaseRepositoryRouter(excelPath);

        // Act
        await router.MigrateExcelToSqliteAsync(excelPath, sqlitePath);

        // Assert
        router.EngineType.Should().Be(DatabaseEngine.Sqlite);
        router.ResolvedDatabaseFilePath.Should().Be(Path.GetFullPath(sqlitePath));

        var basePath = await router.GetBasePathAsync();
        basePath.Should().Be(@"D:\FotoArchivio");

        var eventi = await router.GetEventiAsync();
        eventi.Should().ContainSingle(e => e.Id == evento.Id && e.NomeEvento == "Giro d'Italia");

        var bundle = await router.GetEventDataBundleAsync(evento.Id);
        bundle.Atleti.Should().ContainSingle(a => a.Nome == "Filippo" && a.Cognome == "Ganna");
        bundle.Discipline.Should().ContainSingle(d => d.NomeDisciplina == "Crono");
        bundle.Foto.Should().HaveCount(3);
        bundle.Foto.Should().Contain(f => f.NomeFileOriginale == "GANNA_01.JPG");
        bundle.Foto.Should().Contain(f => f.NomeFileOriginale == "PODIO_01.JPG" && f.IsPremiazione && f.AtletaId == Guid.Empty);
        bundle.Foto.Should().Contain(f => f.NomeFileOriginale == "PAESAGGIO.JPG");

        var acquisti = await router.GetAcquistiByEventoAsync(evento.Id);
        acquisti.Should().ContainSingle(a => a.EmailCliente == "fan@cycling.com");
    }
}

