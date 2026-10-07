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

    [Fact]
    public void SlideshowConfigViewModel_Initializes10Transitions_WithGpuCpuBadges()
    {
        var mockRepo = new Mock<IExcelRepository>();
        var ev = new Evento { NomeEvento = "Rally Legend" };
        var screens = new List<DisplayScreenInfo>
        {
            new DisplayScreenInfo { Index = 0, DisplayName = "Monitor 1", Width = 1920, Height = 1080, IsPrimary = true },
            new DisplayScreenInfo { Index = 1, DisplayName = "Monitor 2", Width = 2560, Height = 1440, IsPrimary = false }
        };

        var vm = new SlideshowConfigViewModel(mockRepo.Object, new[] { ev }, screens, ev);

        // 10 transizioni
        vm.Transitions.Should().HaveCount(10);
        vm.Transitions.Should().AllSatisfy(t =>
        {
            t.Nome.Should().NotBeNullOrWhiteSpace();
            t.Descrizione.Should().NotBeNullOrWhiteSpace();
            t.BadgeText.Should().Match(b => b == "GPU" || b == "CPU" || b == "CPU / GPU");
        });

        // Schermo esterno pre-selezionato se disponibile
        vm.SelectedScreen.Should().NotBeNull();
        vm.SelectedScreen!.Index.Should().Be(1);
    }

    [Fact]
    public async Task SlideshowConfigViewModel_SelectAndDeselectAll_Works()
    {
        var mockRepo = new Mock<IExcelRepository>();
        var ev = new Evento { NomeEvento = "GP Monza" };
        var atleti = new List<Atleta>
        {
            new Atleta { Id = Guid.NewGuid(), NumeroPettorale = "1", Nome = "Max", Cognome = "Verstappen" },
            new Atleta { Id = Guid.NewGuid(), NumeroPettorale = "16", Nome = "Charles", Cognome = "Leclerc" },
            new Atleta { Id = Guid.NewGuid(), NumeroPettorale = "44", Nome = "Lewis", Cognome = "Hamilton" }
        };

        mockRepo.Setup(r => r.GetAtletiByEventoAsync(ev.Id, default)).ReturnsAsync(atleti);

        var vm = new SlideshowConfigViewModel(mockRepo.Object, new[] { ev }, Array.Empty<DisplayScreenInfo>(), ev);
        await vm.LoadAtletiForSelectedEventoAsync(ev.Id);

        vm.Atleti.Should().HaveCount(3);
        vm.Atleti.Should().AllSatisfy(a => a.IsSelected.Should().BeTrue());

        // Deseleziona tutti
        vm.DeselectAllAtletiCommand.Execute(null);
        vm.Atleti.Should().AllSatisfy(a => a.IsSelected.Should().BeFalse());

        // Seleziona tutti
        vm.SelectAllAtletiCommand.Execute(null);
        vm.Atleti.Should().AllSatisfy(a => a.IsSelected.Should().BeTrue());

        // Deseleziona transizioni
        vm.DeselectAllTransitionsCommand.Execute(null);
        vm.Transitions.Should().AllSatisfy(t => t.IsSelected.Should().BeFalse());

        // Seleziona transizioni
        vm.SelectAllTransitionsCommand.Execute(null);
        vm.Transitions.Should().AllSatisfy(t => t.IsSelected.Should().BeTrue());
    }

    [Fact]
    public async Task SlideshowConfigViewModel_Validation_HandlesMissingSelections()
    {
        var mockRepo = new Mock<IExcelRepository>();
        var ev = new Evento { NomeEvento = "Dolomiti Skyrace" };
        var atleta = new Atleta { Id = Guid.NewGuid(), NumeroPettorale = "10", Nome = "Alex", Cognome = "Rossi" };

        mockRepo.Setup(r => r.GetAtletiByEventoAsync(ev.Id, default)).ReturnsAsync(new List<Atleta> { atleta });
        mockRepo.Setup(r => r.GetFotoByEventoAsync(ev.Id, default)).ReturnsAsync(new List<Foto>());

        var vm = new SlideshowConfigViewModel(mockRepo.Object, new[] { ev }, Array.Empty<DisplayScreenInfo>(), ev);
        await vm.LoadAtletiForSelectedEventoAsync(ev.Id);

        SlideshowConfig? startedConfig = null;
        vm.RequestStartSlideshow += c => startedConfig = c;

        // Caso 1: Nessun atleta selezionato
        vm.DeselectAllAtletiCommand.Execute(null);
        await vm.StartSlideshowAsync();
        startedConfig.Should().BeNull();
        vm.ErrorMessage.Should().Contain("partecipante");

        // Caso 2: Nessun formato selezionato
        vm.SelectAllAtletiCommand.Execute(null);
        vm.IncludeJpegPng = false;
        vm.IncludeRaw = false;
        await vm.StartSlideshowAsync();
        startedConfig.Should().BeNull();
        vm.ErrorMessage.Should().Contain("formato");

        // Caso 3: Nessuna transizione selezionata
        vm.IncludeJpegPng = true;
        vm.DeselectAllTransitionsCommand.Execute(null);
        await vm.StartSlideshowAsync();
        startedConfig.Should().BeNull();
        vm.ErrorMessage.Should().Contain("transizione");

        // Caso 4: Nessuna foto trovata
        vm.SelectAllTransitionsCommand.Execute(null);
        await vm.StartSlideshowAsync();
        startedConfig.Should().BeNull();
        vm.ErrorMessage.Should().Contain("Nessuna foto trovata");
    }

    [Fact]
    public async Task SlideshowConfigViewModel_Validation_SuccessTriggersStart()
    {
        var mockRepo = new Mock<IExcelRepository>();
        var ev = new Evento { NomeEvento = "Giro 2026" };
        var atletaId = Guid.NewGuid();
        var atleta = new Atleta { Id = atletaId, NumeroPettorale = "7", Nome = "Fausto", Cognome = "Coppi" };
        var foto = new Foto { Id = Guid.NewGuid(), EventoId = ev.Id, AtletaId = atletaId, Formato = "JPEG" };

        mockRepo.Setup(r => r.GetAtletiByEventoAsync(ev.Id, default)).ReturnsAsync(new List<Atleta> { atleta });
        mockRepo.Setup(r => r.GetFotoByEventoAsync(ev.Id, default)).ReturnsAsync(new List<Foto> { foto });

        var screens = new List<DisplayScreenInfo>
        {
            new DisplayScreenInfo { Index = 0, DisplayName = "Monitor TV", Width = 3840, Height = 2160, IsPrimary = false }
        };

        var vm = new SlideshowConfigViewModel(mockRepo.Object, new[] { ev }, screens, ev);
        await vm.LoadAtletiForSelectedEventoAsync(ev.Id);

        SlideshowConfig? startedConfig = null;
        vm.RequestStartSlideshow += c => startedConfig = c;

        await vm.StartSlideshowAsync();

        startedConfig.Should().NotBeNull();
        startedConfig!.EventoId.Should().Be(ev.Id);
        startedConfig.SelectedAtletiIds.Should().Contain(atletaId);
        startedConfig.SelectedTransitionIds.Should().HaveCount(10);
        startedConfig.TargetScreen!.DisplayName.Should().Be("Monitor TV");
    }

    [Fact]
    public void MainViewModel_SlideshowControls_OpenAndStopWork()
    {
        var mockExcel = new Mock<IExcelRepository>();
        var mockFileOrg = new Mock<IFileOrganizationService>();
        var mockPipeline = new Mock<IIngestionPipelineService>();
        var mockSd = new Mock<ISdCardWatcherService>();

        var mainVm = new MainViewModel(mockExcel.Object, mockFileOrg.Object, mockPipeline.Object, mockSd.Object);

        // Apertura schermata configurazione
        mainVm.IsModalOpen.Should().BeFalse();
        mainVm.OpenSlideshowConfigCommand.Execute(null);

        mainVm.IsModalOpen.Should().BeTrue();
        mainVm.CurrentModal.Should().BeOfType<SlideshowConfigViewModel>();

        // Avvio simulato e interruzione
        bool stopRequested = false;
        mainVm.RequestStopSlideshow += () => stopRequested = true;

        mainVm.IsSlideshowActive = true;
        mainVm.StopSlideshowCommand.Execute(null);

        mainVm.IsSlideshowActive.Should().BeFalse();
        stopRequested.Should().BeTrue();
    }

    [Fact]
    public async Task SettingsViewModel_CatalogoPrezzi_Add_And_Reset_Work()
    {
        var mockExcel = new Mock<IExcelRepository>();
        var mockPrefs = new Mock<IAppPreferencesService>();

        mockPrefs.Setup(p => p.RecentDatabases).Returns(new List<string>());

        var defaultItems = new List<PrezzoCatalogoItem>
        {
            new() { Nome = "Foto Singola", Prezzo = 10m, Categoria = CategoriaPrezzo.FotoSingola }
        };
        mockExcel.Setup(x => x.GetCatalogoPrezziAsync(default)).ReturnsAsync(defaultItems);

        var vm = new SettingsViewModel(mockExcel.Object, mockPrefs.Object);
        await vm.InitializeAsync();

        vm.CatalogoPrezzi.Should().HaveCount(1);

        // Aggiunta articolo
        vm.NewItemNome = "Pacchetto 20 Foto";
        vm.NewItemCategoria = CategoriaPrezzo.PacchettoFoto;
        vm.NewItemPrezzo = 100m;
        vm.NewItemQuantitaFoto = 20;

        await vm.AddPrezzoItemCommand.ExecuteAsync(null);

        vm.CatalogoPrezzi.Should().HaveCount(2);
        vm.CatalogoPrezzi.Should().Contain(p => p.Nome == "Pacchetto 20 Foto");
        mockExcel.Verify(x => x.UpsertPrezzoCatalogoItemAsync(It.IsAny<PrezzoCatalogoItem>(), default), Times.Once);
    }

    [Fact]
    public async Task EventDetailViewModel_Acquisto_Calculations_And_Registration_Work()
    {
        var mockExcel = new Mock<IExcelRepository>();
        var mockFileOrg = new Mock<IFileOrganizationService>();

        var evento = new Evento { NomeEvento = "Rally Legend", CartellaDestinazioneRoot = @"C:\Paddock" };
        var atleta = new Atleta { Nome = "Mario", Cognome = "Rossi", NumeroPettorale = "1" };
        var disciplina = new Disciplina { NomeDisciplina = "WRC" };

        mockExcel.Setup(x => x.GetAtletiByEventoAsync(evento.Id, default)).ReturnsAsync(new List<Atleta> { atleta });
        mockExcel.Setup(x => x.GetDisciplineByEventoAsync(evento.Id, default)).ReturnsAsync(new List<Disciplina> { disciplina });
        mockExcel.Setup(x => x.GetFotoByEventoAsync(evento.Id, default)).ReturnsAsync(new List<Foto>());
        mockExcel.Setup(x => x.GetAcquistiByEventoAsync(evento.Id, default)).ReturnsAsync(new List<AcquistoFoto>());
        mockExcel.Setup(x => x.GetCatalogoPrezziAsync(default)).ReturnsAsync(new List<PrezzoCatalogoItem>
        {
            new() { Nome = "Foto Singola", Prezzo = 10.00m, Categoria = CategoriaPrezzo.FotoSingola },
            new() { Nome = "Pacchetto 5 Foto", Prezzo = 40.00m, Categoria = CategoriaPrezzo.PacchettoFoto }
        });

        var vm = new EventDetailViewModel(evento, mockExcel.Object, mockFileOrg.Object);
        await vm.LoadEventDataAsync();

        vm.SelectedAcquistoAtleta = atleta;
        vm.SelectedAcquistoDisciplina = disciplina;
        vm.EmailCliente = "mario@example.com";
        vm.TelefonoCliente = "+39 333 1234567";

        // Aggiungi 2 Foto Singole
        vm.SelectedCatalogoItemToAdd = vm.CatalogoDisponibile.First(c => c.Nome == "Foto Singola");
        vm.QuantitaToAdd = 2;
        vm.AddVoceAcquistoCommand.Execute(null);

        // Aggiungi 1 Pacchetto 5 Foto
        vm.SelectedCatalogoItemToAdd = vm.CatalogoDisponibile.First(c => c.Nome == "Pacchetto 5 Foto");
        vm.QuantitaToAdd = 1;
        vm.AddVoceAcquistoCommand.Execute(null);

        // Verifica totali: 2*10 + 1*40 = 60
        vm.TotaleCalcolato.Should().Be(60.00m);
        vm.TotalePagato.Should().Be(60.00m);

        // Applica sconto personalizzato a 50
        vm.TotalePagato = 50.00m;

        // Registra acquisto
        await vm.RegistraAcquistoCommand.ExecuteAsync(null);

        vm.Acquisti.Should().HaveCount(1);
        vm.Acquisti[0].NomeAtleta.Should().Be("Rossi Mario");
        vm.Acquisti[0].TotaleCalcolato.Should().Be(60.00m);
        vm.Acquisti[0].TotalePagato.Should().Be(50.00m);
        vm.TotaleIncassatoEvento.Should().Be(50.00m);
        vm.TotaleOrdiniEvento.Should().Be(1);

        mockExcel.Verify(x => x.UpsertAcquistoFotoAsync(It.IsAny<AcquistoFoto>(), default), Times.Once);
    }

    [Fact]
    public async Task SettingsViewModel_WatermarkSettings_LoadAndSave()
    {
        var mockRepo = new Mock<IExcelRepository>();
        mockRepo.Setup(r => r.DatabaseFilePath).Returns(@"C:\Data\Photos.xlsx");
        mockRepo.Setup(r => r.GetBasePathAsync(It.IsAny<CancellationToken>())).ReturnsAsync(@"C:\Photos");

        var mockPrefs = new Mock<IAppPreferencesService>();
        mockPrefs.SetupProperty(p => p.DefaultWatermarkEnabled, false);
        mockPrefs.SetupProperty(p => p.DefaultWatermarkImagePath, @"C:\OldLogo.png");
        mockPrefs.SetupProperty(p => p.DefaultWatermarkOpacity, 0.50f);
        mockPrefs.SetupProperty(p => p.DefaultWatermarkPosition, WatermarkPosition.TopLeft);
        mockPrefs.SetupProperty(p => p.DefaultWatermarkScalePercent, 0.25f);
        mockPrefs.SetupProperty(p => p.DefaultPhotographerName, "Mario Rossi");
        mockPrefs.SetupProperty(p => p.DefaultCopyrightNotice, "© 2026 Mario Rossi");
        mockPrefs.Setup(p => p.RecentDatabases).Returns(new List<string>());

        var vm = new SettingsViewModel(mockRepo.Object, mockPrefs.Object);

        // Verifica caricamento iniziale
        vm.DefaultWatermarkEnabled.Should().BeFalse();
        vm.DefaultWatermarkImagePath.Should().Be(@"C:\OldLogo.png");
        vm.DefaultWatermarkOpacity.Should().Be(0.50f);
        vm.DefaultWatermarkPosition.Should().Be(WatermarkPosition.TopLeft);
        vm.DefaultWatermarkScalePercent.Should().Be(0.25f);
        vm.DefaultPhotographerName.Should().Be("Mario Rossi");
        vm.DefaultCopyrightNotice.Should().Be("© 2026 Mario Rossi");

        // Modifica valori
        vm.DefaultWatermarkEnabled = true;
        vm.DefaultWatermarkImagePath = @"C:\NewLogo.png";
        vm.DefaultWatermarkOpacity = 0.80f;
        vm.DefaultWatermarkPosition = WatermarkPosition.BottomRight;
        vm.DefaultWatermarkScalePercent = 0.30f;
        vm.DefaultPhotographerName = "Luigi Verdi";
        vm.DefaultCopyrightNotice = "© 2026 Luigi Verdi";

        await vm.SaveWatermarkSettingsAsync();

        // Verifica salvataggio nel mock
        mockPrefs.Object.DefaultWatermarkEnabled.Should().BeTrue();
        mockPrefs.Object.DefaultWatermarkImagePath.Should().Be(@"C:\NewLogo.png");
        mockPrefs.Object.DefaultWatermarkOpacity.Should().Be(0.80f);
        mockPrefs.Object.DefaultWatermarkPosition.Should().Be(WatermarkPosition.BottomRight);
        mockPrefs.Object.DefaultWatermarkScalePercent.Should().Be(0.30f);
        mockPrefs.Object.DefaultPhotographerName.Should().Be("Luigi Verdi");
        mockPrefs.Object.DefaultCopyrightNotice.Should().Be("© 2026 Luigi Verdi");
        mockPrefs.Verify(p => p.SaveAsync(), Times.Once);
    }

    [Fact]
    public void IngestionWizardViewModel_LoadsDefaultsFromPreferences()
    {
        var evento = new Evento { NomeEvento = "Test Event" };
        var atleta = new Atleta { NumeroPettorale = "99", Nome = "Giacomo", Cognome = "Leopardi" };
        var disc = new Disciplina { NomeDisciplina = "Poesia" };
        var mockWatcher = new Mock<ISdCardWatcherService>();

        var mockPrefs = new Mock<IAppPreferencesService>();
        mockPrefs.Setup(p => p.DefaultWatermarkEnabled).Returns(true);
        mockPrefs.Setup(p => p.DefaultWatermarkImagePath).Returns(@"C:\Brand\Logo.png");
        mockPrefs.Setup(p => p.DefaultWatermarkOpacity).Returns(0.75f);
        mockPrefs.Setup(p => p.DefaultWatermarkPosition).Returns(WatermarkPosition.Center);
        mockPrefs.Setup(p => p.DefaultWatermarkScalePercent).Returns(0.22f);
        mockPrefs.Setup(p => p.DefaultPhotographerName).Returns("Paddock Pro Studio");
        mockPrefs.Setup(p => p.DefaultCopyrightNotice).Returns("© Paddock Pro 2026");

        var vm = new IngestionWizardViewModel(
            evento,
            new[] { atleta },
            new[] { disc },
            mockWatcher.Object,
            mockPrefs.Object);

        // Verifica precompilazione
        vm.WatermarkEnabled.Should().BeTrue();
        vm.WatermarkImagePath.Should().Be(@"C:\Brand\Logo.png");
        vm.WatermarkOpacity.Should().Be(0.75f);
        vm.WatermarkPosition.Should().Be(WatermarkPosition.Center);
        vm.WatermarkScalePercent.Should().Be(0.22f);
        vm.PhotographerName.Should().Be("Paddock Pro Studio");
        vm.CopyrightNotice.Should().Be("© Paddock Pro 2026");

        // Verifica generazione richiesta di ingestione con i valori precompilati
        var tempSource = Path.Combine(Path.GetTempPath(), "TempSource_Defaults_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempSource);
        try
        {
            vm.SourceDirectory = tempSource;
            IngestionJobRequest? request = null;
            vm.RequestClose += r => request = r;

            vm.StartIngestionCommand.Execute(null);

            request.Should().NotBeNull();
            request!.Watermark.Enabled.Should().BeTrue();
            request.Watermark.WatermarkImagePath.Should().Be(@"C:\Brand\Logo.png");
            request.Watermark.Opacity.Should().Be(0.75f);
            request.Watermark.Position.Should().Be(WatermarkPosition.Center);
            request.Watermark.ScalePercent.Should().Be(0.22f);
            request.Metadata.PhotographerName.Should().Be("Paddock Pro Studio");
            request.Metadata.CopyrightNotice.Should().Be("© Paddock Pro 2026");
        }
        finally
        {
            Directory.Delete(tempSource, recursive: true);
        }
    }
}

