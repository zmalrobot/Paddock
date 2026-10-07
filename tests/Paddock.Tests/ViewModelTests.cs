using FluentAssertions;
using Moq;
using Paddock.Core.DTOs;
using Paddock.Core.Enums;
using Paddock.Core.Interfaces;
using Paddock.Core.Models;
using Paddock.UI.ViewModels;
using Xunit;

namespace Paddock.Tests;

public class ViewModelTests
{
    [Fact]
    public void DeleteEventDialogViewModel_SafeMode_ReturnsDatabaseOnly()
    {
        var evento = new Evento { NomeEvento = "Trial 2026" };
        var vm = new DeleteEventDialogViewModel(evento);

        DeleteMode? resultMode = null;
        vm.RequestClose += mode => resultMode = mode;

        vm.ChooseDatabaseOnlyCommand.Execute(null);

        resultMode.Should().Be(DeleteMode.DatabaseOnly);
    }

    [Fact]
    public void DeleteEventDialogViewModel_DestructiveMode_RequiresCheckbox()
    {
        var evento = new Evento { NomeEvento = "Trial 2026" };
        var vm = new DeleteEventDialogViewModel(evento);

        DeleteMode? resultMode = null;
        vm.RequestClose += mode => resultMode = mode;

        // Senza spuntare la casella
        vm.ChooseDatabaseAndFilesCommand.Execute(null);
        resultMode.Should().BeNull();
        vm.ErrorMessage.Should().NotBeNullOrWhiteSpace();

        // Con spunta di sicurezza
        vm.ConfirmFilesPurgeChecked = true;
        vm.ChooseDatabaseAndFilesCommand.Execute(null);
        resultMode.Should().Be(DeleteMode.DatabaseAndFiles);
    }

    [Fact]
    public void EventEditDialogViewModel_Validation_FailsWhenRequiredFieldsMissing()
    {
        var vm = new EventEditDialogViewModel(defaultRootPath: @"D:\FotoArchivio");
        vm.DialogTitle.Should().Be("Nuovo Evento Sportivo");
        vm.CartellaDestinazioneRoot.Should().Be(@"D:\FotoArchivio");

        var existingVm = new EventEditDialogViewModel(new Evento { NomeEvento = "Giro 2026", CartellaDestinazioneRoot = @"D:\VecchiaCartella" });
        existingVm.DialogTitle.Should().Be("Modifica Evento Sportivo");
        existingVm.CartellaDestinazioneRoot.Should().Be(@"D:\VecchiaCartella");

        bool? closed = null;
        vm.RequestClose += ok => closed = ok;

        // Nome vuoto
        vm.NomeEvento = "";
        vm.SaveCommand.Execute(null);

        closed.Should().BeNull();
        vm.ErrorMessage.Should().Contain("obbligatorio");

        // Nome valido e date
        vm.NomeEvento = "Maratona Roma";
        vm.DataInizio = new DateTime(2026, 10, 15);
        vm.DataFine = new DateTime(2026, 10, 17);
        vm.SaveCommand.Execute(null);

        closed.Should().BeTrue();
        vm.Evento.NomeEvento.Should().Be("Maratona Roma");
        vm.Evento.CartellaDestinazioneRoot.Should().Be(@"D:\FotoArchivio");
        vm.Evento.DataInizio.Should().Be(new DateTime(2026, 10, 15));
        vm.Evento.DataFine.Should().Be(new DateTime(2026, 10, 17));
    }

    [Fact]
    public void IngestionWizardViewModel_Validation_RequiresSelections()
    {
        var evento = new Evento { NomeEvento = "Test Event" };
        var atleta = new Atleta { NumeroPettorale = "10", Nome = "A", Cognome = "B" };
        var disc = new Disciplina { NomeDisciplina = "Corsa" };
        var mockWatcher = new Mock<ISdCardWatcherService>();

        var vm = new IngestionWizardViewModel(evento, new[] { atleta }, new[] { disc }, mockWatcher.Object);

        // Cartella sorgente inesistente
        vm.SourceDirectory = @"Z:\NonExistent_Folder_12345";
        vm.StartIngestionCommand.Execute(null);
        vm.ErrorMessage.Should().NotBeNullOrWhiteSpace();

        // Con cartella temporanea esistente
        var tempSource = Path.Combine(Path.GetTempPath(), "TempSource_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempSource);
        try
        {
            vm.SourceDirectory = tempSource;
            vm.SelectedAtleta = atleta;
            vm.SelectedDisciplina = disc;
            vm.WatermarkEnabled = true;
            vm.PhotographerName = "Studio Foto";

            IngestionJobRequest? generatedRequest = null;
            vm.RequestClose += req => generatedRequest = req;

            vm.StartIngestionCommand.Execute(null);

            generatedRequest.Should().NotBeNull();
            generatedRequest!.AtletaTarget.Should().Be(atleta);
            generatedRequest.DisciplinaTarget.Should().Be(disc);
            generatedRequest.Metadata.PhotographerName.Should().Be("Studio Foto");
            generatedRequest.Watermark.Enabled.Should().BeTrue();
        }
        finally
        {
            Directory.Delete(tempSource, recursive: true);
        }
    }

    [Fact]
    public async Task SettingsViewModel_UpdatesBasePath_And_SwitchesDatabase()
    {
        var mockRepo = new Mock<IExcelRepository>();
        mockRepo.Setup(r => r.DatabaseFilePath).Returns(@"C:\Data\Photos.xlsx");
        mockRepo.Setup(r => r.GetBasePathAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(@"C:\Photos");

        var mockPrefs = new Mock<IAppPreferencesService>();
        mockPrefs.Setup(p => p.RecentDatabases).Returns(new List<string> { @"C:\Data\Photos.xlsx", @"D:\Old\Archive.xlsx" });

        var vm = new SettingsViewModel(mockRepo.Object, mockPrefs.Object);
        await vm.InitializeAsync();

        vm.BasePath.Should().Be(@"C:\Photos");
        vm.RecentDatabases.Should().ContainSingle(p => p == @"D:\Old\Archive.xlsx");

        // Aggiorna BasePath
        vm.BasePath = @"E:\NewPhotosRoot";
        await vm.UpdateBasePathAsync();

        mockRepo.Verify(r => r.UpdateAllEventRootsAsync(@"E:\NewPhotosRoot", It.IsAny<CancellationToken>()), Times.Once);
        vm.StatusMessage.Should().Contain("successo");
        vm.IsErrorMessage.Should().BeFalse();

        // Commuta Database
        await vm.SwitchDatabaseAsync(@"E:\SecondDb.xlsx");
        mockRepo.Verify(r => r.SwitchDatabaseAsync(@"E:\SecondDb.xlsx", It.IsAny<CancellationToken>()), Times.Once);
        mockPrefs.Verify(p => p.AddRecentDatabase(@"E:\SecondDb.xlsx"), Times.Once);
    }

    [Fact]
    public async Task DatabaseStartupDialogViewModel_HandlesSelection()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var mockPrefs = new Mock<IAppPreferencesService>();
            mockPrefs.Setup(p => p.LastDatabasePath).Returns(tempFile);
            mockPrefs.Setup(p => p.RecentDatabases).Returns(new List<string> { tempFile });

            var vm = new DatabaseStartupDialogViewModel(mockPrefs.Object);
            vm.HasLastDatabase.Should().BeTrue();

            string? chosenPath = null;
            vm.DatabaseSelected += path => chosenPath = path;

            await vm.ContinueWithLastDatabaseCommand.ExecuteAsync(null);
            chosenPath.Should().Be(tempFile);

            chosenPath = null;
            await vm.SelectDatabasePathAsync(@"C:\Another\Database.xlsx");
            chosenPath.Should().Be(@"C:\Another\Database.xlsx");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Application_EmbedsLogoImageResource()
    {
        var assembly = typeof(Paddock.UI.MainWindow).Assembly;
        var manifestResources = assembly.GetManifestResourceNames();
        manifestResources.Should().Contain("!AvaloniaResources");

        var currentDir = new DirectoryInfo(AppContext.BaseDirectory);
        while (currentDir != null && !File.Exists(Path.Combine(currentDir.FullName, "Paddock.slnx")))
        {
            currentDir = currentDir.Parent;
        }

        currentDir.Should().NotBeNull();
        var assetFile = Path.Combine(currentDir!.FullName, "src", "Paddock.UI", "Assets", "logo.jpg");
        File.Exists(assetFile).Should().BeTrue();
    }
}

