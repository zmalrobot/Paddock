using Avalonia.Media;
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
    public void Application_EmbedsLogoIcoAndConfiguresApplicationIcon()
    {
        var currentDir = new DirectoryInfo(AppContext.BaseDirectory);
        while (currentDir != null && !File.Exists(Path.Combine(currentDir.FullName, "Paddock.slnx")))
        {
            currentDir = currentDir.Parent;
        }

        currentDir.Should().NotBeNull();
        var rootIco = Path.Combine(currentDir!.FullName, "logo.ico");
        var uiIco = Path.Combine(currentDir.FullName, "src", "Paddock.UI", "Assets", "logo.ico");
        var updaterIco = Path.Combine(currentDir.FullName, "src", "Paddock.Updater", "Assets", "logo.ico");

        File.Exists(rootIco).Should().BeTrue();
        File.Exists(uiIco).Should().BeTrue();
        File.Exists(updaterIco).Should().BeTrue();

        // Verifica validità formato binario ICO (Header: 2 byte reserved=0, 2 byte type=1, 2 byte count >= 4)
        var icoBytes = File.ReadAllBytes(uiIco);
        icoBytes.Length.Should().BeGreaterThan(6 + 16);
        BitConverter.ToUInt16(icoBytes, 0).Should().Be(0);
        BitConverter.ToUInt16(icoBytes, 2).Should().Be(1);
        var iconCount = BitConverter.ToUInt16(icoBytes, 4);
        iconCount.Should().BeGreaterThanOrEqualTo(4);

        // Verifica configurazione ApplicationIcon nei csproj
        var uiCsproj = Path.Combine(currentDir.FullName, "src", "Paddock.UI", "Paddock.UI.csproj");
        var updaterCsproj = Path.Combine(currentDir.FullName, "src", "Paddock.Updater", "Paddock.Updater.csproj");

        File.ReadAllText(uiCsproj).Should().Contain("<ApplicationIcon>Assets\\logo.ico</ApplicationIcon>");
        File.ReadAllText(updaterCsproj).Should().Contain("<ApplicationIcon>Assets\\logo.ico</ApplicationIcon>");
    }

    [Fact]
    public void LinuxPackaging_IncludesDesktopFileAndPngIcon()
    {
        var currentDir = new DirectoryInfo(AppContext.BaseDirectory);
        while (currentDir != null && !File.Exists(Path.Combine(currentDir.FullName, "Paddock.slnx")))
        {
            currentDir = currentDir.Parent;
        }

        currentDir.Should().NotBeNull();
        var desktopFile = Path.Combine(currentDir!.FullName, "paddock.desktop");
        var rootPng = Path.Combine(currentDir.FullName, "logo.png");
        var uiPng = Path.Combine(currentDir.FullName, "src", "Paddock.UI", "Assets", "logo.png");
        var updaterPng = Path.Combine(currentDir.FullName, "src", "Paddock.Updater", "Assets", "logo.png");

        File.Exists(desktopFile).Should().BeTrue();
        File.Exists(rootPng).Should().BeTrue();
        File.Exists(uiPng).Should().BeTrue();
        File.Exists(updaterPng).Should().BeTrue();

        var desktopContent = File.ReadAllText(desktopFile);
        desktopContent.Should().Contain("Exec=Paddock.UI");
        desktopContent.Should().Contain("Icon=logo");
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
        mockPrefs.Setup(p => p.DefaultAutoRotate).Returns(true);

        var vm = new IngestionWizardViewModel(
            evento,
            new[] { atleta },
            new[] { disc },
            mockWatcher.Object,
            mockPrefs.Object);

        // Verifica precompilazione
        vm.AutoRotate.Should().BeTrue();
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
            request!.AutoRotate.Should().BeTrue();
            request.Watermark.Enabled.Should().BeTrue();
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

    [Fact]
    public void IngestionWizardViewModel_AutoRotate_DefaultsToTrue_AndPropagatesToRequest()
    {
        var evento = new Evento { NomeEvento = "Gara Rotazione" };
        var atleta = new Atleta { NumeroPettorale = "10", Nome = "Paolo", Cognome = "Maldini" };
        var disc = new Disciplina { NomeDisciplina = "Calcio" };
        var mockWatcher = new Mock<ISdCardWatcherService>();

        var vm = new IngestionWizardViewModel(
            evento,
            new[] { atleta },
            new[] { disc },
            mockWatcher.Object);

        // Di default AutoRotate deve essere abilitato
        vm.AutoRotate.Should().BeTrue();

        var tempDir = Path.Combine(Path.GetTempPath(), "TempSource_AutoRotate_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            vm.SourceDirectory = tempDir;
            IngestionJobRequest? request = null;
            vm.RequestClose += r => request = r;

            // Avvio con AutoRotate true
            vm.StartIngestionCommand.Execute(null);
            request.Should().NotBeNull();
            request!.AutoRotate.Should().BeTrue();

            // Toggle a false
            vm.AutoRotate = false;
            vm.StartIngestionCommand.Execute(null);
            request!.AutoRotate.Should().BeFalse();
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task EventDetailViewModel_HierarchicalPhotoGroups_BuildsAndExpandCollapseWorks()
    {
        var evento = new Evento { Id = Guid.NewGuid(), NomeEvento = "Rally Legend" };
        var atleta1 = new Atleta { Id = Guid.NewGuid(), EventoId = evento.Id, NumeroPettorale = "10", Nome = "Mario", Cognome = "Rossi" };
        var atleta2 = new Atleta { Id = Guid.NewGuid(), EventoId = evento.Id, NumeroPettorale = "20", Nome = "Luigi", Cognome = "Bianchi" };
        var disc1 = new Disciplina { Id = Guid.NewGuid(), EventoId = evento.Id, NomeDisciplina = "Prove Libere" };
        var disc2 = new Disciplina { Id = Guid.NewGuid(), EventoId = evento.Id, NomeDisciplina = "Gara 1" };

        var foto1 = new Foto { Id = Guid.NewGuid(), EventoId = evento.Id, AtletaId = atleta1.Id, DisciplinaId = disc1.Id, NomeFileOriginale = "F1.jpg", Formato = "JPEG", DimensioneByte = 1000, PathRelativo = "F1.jpg" };
        var foto2 = new Foto { Id = Guid.NewGuid(), EventoId = evento.Id, AtletaId = atleta1.Id, DisciplinaId = disc2.Id, NomeFileOriginale = "F2.jpg", Formato = "JPEG", DimensioneByte = 2000, PathRelativo = "F2.jpg" };
        var foto3 = new Foto { Id = Guid.NewGuid(), EventoId = evento.Id, AtletaId = atleta2.Id, DisciplinaId = disc1.Id, NomeFileOriginale = "F3.raw", Formato = "RAW", DimensioneByte = 5000, PathRelativo = "F3.raw" };

        var mockRepo = new Mock<IExcelRepository>();
        mockRepo.Setup(r => r.GetAtletiByEventoAsync(evento.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Atleta> { atleta1, atleta2 });
        mockRepo.Setup(r => r.GetDisciplineByEventoAsync(evento.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Disciplina> { disc1, disc2 });
        mockRepo.Setup(r => r.GetFotoByEventoAsync(evento.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Foto> { foto1, foto2, foto3 });
        mockRepo.Setup(r => r.GetCatalogoPrezziAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<PrezzoCatalogoItem>());
        mockRepo.Setup(r => r.GetAcquistiByEventoAsync(evento.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<AcquistoFoto>());
        mockRepo.Setup(r => r.GetBasePathAsync(It.IsAny<CancellationToken>())).ReturnsAsync(@"C:\FotoTest");

        var mockOrg = new Mock<IFileOrganizationService>();
        var vm = new EventDetailViewModel(evento, mockRepo.Object, mockOrg.Object);

        await vm.LoadEventDataAsync();

        vm.AthletePhotoGroups.Should().HaveCount(2);
        var groupA1 = vm.AthletePhotoGroups.First(g => g.Atleta.Id == atleta1.Id);
        groupA1.DisciplineGroups.Should().HaveCount(2);
        groupA1.TotalPhotosCount.Should().Be(2);

        var groupA2 = vm.AthletePhotoGroups.First(g => g.Atleta.Id == atleta2.Id);
        groupA2.DisciplineGroups.Should().HaveCount(1);
        groupA2.TotalPhotosCount.Should().Be(1);

        // Test collapse all
        vm.CollapseAllPhotoGroupsCommand.Execute(null);
        vm.AthletePhotoGroups.All(a => !a.IsExpanded && a.DisciplineGroups.All(d => !d.IsExpanded)).Should().BeTrue();

        // Test expand all
        vm.ExpandAllPhotoGroupsCommand.Execute(null);
        vm.AthletePhotoGroups.All(a => a.IsExpanded && a.DisciplineGroups.All(d => d.IsExpanded)).Should().BeTrue();
    }

    [Fact]
    public async Task EventDetailViewModel_DeleteSinglePhoto_DeletesFromExcelAndUpdatesMetrics()
    {
        var evento = new Evento { Id = Guid.NewGuid(), NomeEvento = "Rally Legend", TotaleFoto = 1, TotaleByteOccupati = 1000 };
        var atleta = new Atleta { Id = Guid.NewGuid(), EventoId = evento.Id, NumeroPettorale = "10", Nome = "Mario", Cognome = "Rossi" };
        var disc = new Disciplina { Id = Guid.NewGuid(), EventoId = evento.Id, NomeDisciplina = "Prove Libere" };
        var foto = new Foto { Id = Guid.NewGuid(), EventoId = evento.Id, AtletaId = atleta.Id, DisciplinaId = disc.Id, NomeFileOriginale = "F1.jpg", Formato = "JPEG", DimensioneByte = 1000, PathRelativo = "F1.jpg" };

        var mockRepo = new Mock<IExcelRepository>();
        mockRepo.Setup(r => r.GetAtletiByEventoAsync(evento.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Atleta> { atleta });
        mockRepo.Setup(r => r.GetDisciplineByEventoAsync(evento.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Disciplina> { disc });
        mockRepo.Setup(r => r.GetFotoByEventoAsync(evento.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Foto> { foto });
        mockRepo.Setup(r => r.GetCatalogoPrezziAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<PrezzoCatalogoItem>());
        mockRepo.Setup(r => r.GetAcquistiByEventoAsync(evento.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<AcquistoFoto>());
        mockRepo.Setup(r => r.GetBasePathAsync(It.IsAny<CancellationToken>())).ReturnsAsync(@"C:\FotoTest");

        var mockOrg = new Mock<IFileOrganizationService>();
        var vm = new EventDetailViewModel(evento, mockRepo.Object, mockOrg.Object);
        await vm.LoadEventDataAsync();

        var photoItem = vm.FlatActivePhotoItems.First();

        var deleted = await vm.DeleteSinglePhotoAsync(photoItem);

        deleted.Should().BeTrue();
        mockRepo.Verify(r => r.DeleteFotoAsync(foto.Id, It.IsAny<CancellationToken>()), Times.Once);
        vm.AllFoto.Should().BeEmpty();
        vm.FilteredFoto.Should().BeEmpty();
        vm.FlatActivePhotoItems.Should().BeEmpty();
        vm.AthletePhotoGroups.Should().BeEmpty();
        vm.Evento.TotaleFoto.Should().Be(0);
        vm.Evento.TotaleByteOccupati.Should().Be(0);
    }

    [Fact]
    public async Task PhotoViewerViewModel_NavigationAndDeletion_BehavesCorrectly()
    {
        var foto1 = new Foto { Id = Guid.NewGuid(), NomeFileOriginale = "Photo1.jpg", Formato = "JPEG", DimensioneByte = 1024 };
        var foto2 = new Foto { Id = Guid.NewGuid(), NomeFileOriginale = "Photo2.jpg", Formato = "JPEG", DimensioneByte = 2048 };

        var item1 = new PhotoItemViewModel(foto1, @"C:\Test\Photo1.jpg") { AtletaDisplay = "Mario Rossi", DisciplinaDisplay = "Salto" };
        var item2 = new PhotoItemViewModel(foto2, @"C:\Test\Photo2.jpg") { AtletaDisplay = "Luigi Bianchi", DisciplinaDisplay = "Corsa" };

        var photos = new List<PhotoItemViewModel> { item1, item2 };
        bool deleteRequested = false;

        var viewer = new PhotoViewerViewModel(photos, initialIndex: 0, deleteCallback: p =>
        {
            deleteRequested = true;
            return Task.FromResult(true);
        });

        viewer.CurrentIndex.Should().Be(0);
        viewer.CanGoPrevious.Should().BeFalse();
        viewer.CanGoNext.Should().BeTrue();
        viewer.CounterDisplay.Should().Be("1 / 2");
        viewer.NomeFile.Should().Be("Photo1.jpg");

        // Naviga successiva
        viewer.NextPhotoCommand.Execute(null);
        viewer.CurrentIndex.Should().Be(1);
        viewer.CanGoPrevious.Should().BeTrue();
        viewer.CanGoNext.Should().BeFalse();
        viewer.CounterDisplay.Should().Be("2 / 2");
        viewer.NomeFile.Should().Be("Photo2.jpg");

        // Elimina seconda foto
        bool closeTriggered = false;
        viewer.RequestClose += () => closeTriggered = true;

        await viewer.DeleteCurrentPhotoAsync();

        deleteRequested.Should().BeTrue();
        viewer.PhotosCount.Should().Be(1);
        viewer.CurrentIndex.Should().Be(0);
        viewer.NomeFile.Should().Be("Photo1.jpg");
        closeTriggered.Should().BeFalse();

        // Elimina l'ultima foto rimasta -> chiusura visore
        await viewer.DeleteCurrentPhotoAsync();
        viewer.PhotosCount.Should().Be(0);
        closeTriggered.Should().BeTrue();
    }

    [Fact]
    public async Task SlideshowWindowViewModel_InitializesAndAlternatesLayersWithFallback()
    {
        var eventoId = Guid.NewGuid();
        var atletaId = Guid.NewGuid();
        var mockRepo = new Mock<IExcelRepository>();
        mockRepo.Setup(r => r.GetBasePathAsync(default)).ReturnsAsync(@"C:\Photos");
        mockRepo.Setup(r => r.GetFotoByEventoAsync(eventoId, default)).ReturnsAsync(new List<Foto>
        {
            new() { Id = Guid.NewGuid(), EventoId = eventoId, AtletaId = atletaId, PathRelativo = @"C:\Photos\img1.png", NomeFileOriginale = "img1.png", Formato = "PNG" },
            new() { Id = Guid.NewGuid(), EventoId = eventoId, AtletaId = atletaId, PathRelativo = @"C:\Photos\img2.png", NomeFileOriginale = "img2.png", Formato = "PNG" }
        });
        mockRepo.Setup(r => r.GetAtletiByEventoAsync(eventoId, default)).ReturnsAsync(new List<Atleta>
        {
            new() { Id = atletaId, Nome = "Mario", Cognome = "Rossi", NumeroPettorale = "10" }
        });

        var config = new SlideshowConfig
        {
            EventoId = eventoId,
            SelectedAtletiIds = new List<Guid> { atletaId },
            IncludeJpegPng = true,
            IncludeRaw = false,
            DurationSeconds = 5,
            IsRandomOrder = false,
            SelectedTransitionIds = new List<string> { "crossfade" }
        };

        var mockImg = new Mock<IImage>().Object;
        var vm = new SlideshowWindowViewModel(config, mockRepo.Object)
        {
            ImageLoader = _ => Task.FromResult<IImage?>(mockImg)
        };

        // Simula file presenti per LoadPhotosAsync
        typeof(SlideshowWindowViewModel)
            .GetField("_photos", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, new List<SlideshowPhotoItem>
            {
                new(new Foto { Id = Guid.NewGuid() }, @"C:\Photos\img1.png", "Mario Rossi", "PNG"),
                new(new Foto { Id = Guid.NewGuid() }, @"C:\Photos\img2.png", "Mario Rossi", "PNG")
            });

        // Prima foto: Layer A attivo, Layer B inattivo
        await vm.ShowNextPhotoAsync();
        vm.CurrentImage.Should().NotBeNull();
        vm.LayerAOpacity.Should().Be(1.0);
        vm.LayerBOpacity.Should().Be(0.0);
        vm.IsLayerBActive.Should().BeFalse();

        // Seconda foto: Layer B attivo, Layer A inattivo
        await vm.ShowNextPhotoAsync();
        vm.NextImage.Should().NotBeNull();
        vm.LayerBOpacity.Should().Be(1.0);
        vm.LayerAOpacity.Should().Be(0.0);
        vm.IsLayerBActive.Should().BeTrue();

        // Simulazione fallback su errore nelle transizioni:
        // L'immagine deve scorrere comunque (commutando su Layer A) ma senza transizione
        vm.TransitionsEnabled = false;
        await vm.ShowNextPhotoAsync();
        vm.LayerAOpacity.Should().Be(1.0);
        vm.LayerBOpacity.Should().Be(0.0);
        vm.IsLayerBActive.Should().BeFalse();
        vm.TransitionsEnabled.Should().BeFalse();

        vm.CloseSlideshow();
    }

    [Fact]
    public async Task SlideshowWindowViewModel_SkipsUnreadablePhoto_KeepsLoopActive()
    {
        var eventoId = Guid.NewGuid();
        var atletaId = Guid.NewGuid();
        var mockRepo = new Mock<IExcelRepository>();
        mockRepo.Setup(r => r.GetBasePathAsync(default)).ReturnsAsync(@"C:\Photos");

        var config = new SlideshowConfig
        {
            EventoId = eventoId,
            SelectedAtletiIds = new List<Guid> { atletaId },
            IncludeJpegPng = true,
            IncludeRaw = false,
            DurationSeconds = 5,
            IsRandomOrder = false,
            SelectedTransitionIds = new List<string> { "crossfade" }
        };

        var mockImg = new Mock<IImage>().Object;
        var vm = new SlideshowWindowViewModel(config, mockRepo.Object)
        {
            ImageLoader = path => path.Contains("missing")
                ? Task.FromResult<IImage?>(null)
                : Task.FromResult<IImage?>(mockImg)
        };

        typeof(SlideshowWindowViewModel)
            .GetField("_photos", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, new List<SlideshowPhotoItem>
            {
                new(new Foto { Id = Guid.NewGuid() }, @"C:\Photos\missing_nonexistent.png", "Luigi Verdi", "PNG"),
                new(new Foto { Id = Guid.NewGuid() }, @"C:\Photos\valid.png", "Luigi Verdi", "PNG")
            });

        // Il file illeggibile missing viene saltato e il file valido valid.png viene caricato
        await vm.ShowNextPhotoAsync();
        vm.CurrentImage.Should().NotBeNull();
        vm.LayerAOpacity.Should().Be(1.0);
        vm.CurrentAtletaText.Should().Contain("Luigi Verdi");

        vm.CloseSlideshow();
    }

    [Fact]
    public async Task EventDetailViewModel_LoadEventDataAsync_ConsumesSinglePassBundleAndSetsBusyState()
    {
        var evento = new Evento { Id = Guid.NewGuid(), NomeEvento = "Enduro Cup" };
        var atleta = new Atleta { Id = Guid.NewGuid(), EventoId = evento.Id, NumeroPettorale = "55", Nome = "Marco", Cognome = "Melis" };
        var disciplina = new Disciplina { Id = Guid.NewGuid(), EventoId = evento.Id, NomeDisciplina = "E-Bike" };
        var foto = new Foto { Id = Guid.NewGuid(), EventoId = evento.Id, AtletaId = atleta.Id, DisciplinaId = disciplina.Id, PathRelativo = "55_Melis/E-Bike/img.jpg", DimensioneByte = 5000 };
        var acquisto = new AcquistoFoto { Id = Guid.NewGuid(), EventoId = evento.Id, TotalePagato = 45.00m };
        var catalogo = new List<PrezzoCatalogoItem>
        {
            new() { Nome = "Foto Singola", Prezzo = 5.00m }
        };

        var bundle = new EventDataBundle
        {
            Atleti = new List<Atleta> { atleta },
            Discipline = new List<Disciplina> { disciplina },
            Foto = new List<Foto> { foto },
            Acquisti = new List<AcquistoFoto> { acquisto },
            CatalogoPrezzi = catalogo
        };

        var mockExcel = new Mock<IExcelRepository>();
        var mockFileOrg = new Mock<IFileOrganizationService>();

        mockExcel.Setup(x => x.GetEventDataBundleAsync(evento.Id, default)).ReturnsAsync(bundle);
        mockExcel.Setup(x => x.GetBasePathAsync(default)).ReturnsAsync(@"C:\PaddockRepo");

        var vm = new EventDetailViewModel(evento, mockExcel.Object, mockFileOrg.Object);
        await vm.LoadEventDataAsync();

        vm.IsBusy.Should().BeFalse();
        vm.AllAtleti.Should().HaveCount(1);
        vm.AllAtleti[0].NumeroPettorale.Should().Be("55");
        vm.Discipline.Should().HaveCount(1);
        vm.AllFoto.Should().HaveCount(1);
        vm.Acquisti.Should().HaveCount(1);
        vm.TotaleIncassatoEvento.Should().Be(45.00m);
        vm.TotaleOrdiniEvento.Should().Be(1);
        vm.CatalogoDisponibile.Should().HaveCount(1);
        vm.Evento.TotaleAtleti.Should().Be(1);
        vm.Evento.TotaleFoto.Should().Be(1);
        vm.Evento.TotaleByteOccupati.Should().Be(5000);
    }

    [Fact]
    public async Task SlideshowWindowViewModel_Displays_Evento_Atleta_Disciplina_And_PhotoName_InHud()
    {
        var config = new SlideshowConfig
        {
            EventoId = Guid.NewGuid(),
            SelectedAtletiIds = new List<Guid>(),
            IncludeJpegPng = true,
            IncludeRaw = false,
            DurationSeconds = 5,
            IsRandomOrder = false,
            SelectedTransitionIds = new List<string> { "crossfade" }
        };

        var mockRepo = new Mock<IExcelRepository>();
        var mockImg = new Mock<IImage>().Object;

        var vm = new SlideshowWindowViewModel(config, mockRepo.Object)
        {
            ImageLoader = path => Task.FromResult<IImage?>(mockImg)
        };

        var photoItem = new SlideshowPhotoItem(
            new Foto { Id = Guid.NewGuid(), NomeFileOriginale = "EOS600D_20261007_153022_04_4589.JPG" },
            @"C:\Photos\EOS600D_20261007_153022_04_4589.JPG",
            atletaInfo: "#105 Mario Rossi",
            disciplinaInfo: "Slalom Gigante",
            eventoInfo: "Coppa del Mondo 2026",
            fileName: "EOS600D_20261007_153022_04_4589.JPG");

        typeof(SlideshowWindowViewModel)
            .GetField("_photos", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, new List<SlideshowPhotoItem> { photoItem });

        await vm.ShowNextPhotoAsync();

        vm.CurrentEventoText.Should().Be("Coppa del Mondo 2026");
        vm.CurrentAtletaText.Should().Be("#105 Mario Rossi");
        vm.CurrentDisciplinaText.Should().Be("Slalom Gigante");
        vm.CurrentPhotoNameText.Should().Be("EOS600D_20261007_153022_04_4589.JPG");
        vm.CurrentCounterText.Should().Be("1 / 1");
    }

    [Fact]
    public void PhotoBrowserModels_DefaultIsExpanded_IsFalse()
    {
        var atleta = new Atleta { NumeroPettorale = "10", Nome = "Paolo", Cognome = "Rossi" };
        var disciplina = new Disciplina { NomeDisciplina = "Salto" };

        var athleteGroup = new PhotoBrowserAthleteGroup(atleta);
        var disciplinaGroup = new PhotoBrowserDisciplinaGroup(disciplina);

        athleteGroup.IsExpanded.Should().BeFalse("I gruppi atleta nel photo browser devono essere collassati per impostazione predefinita");
        disciplinaGroup.IsExpanded.Should().BeFalse("I gruppi disciplina nel photo browser devono essere collassati per impostazione predefinita");
    }

    [Fact]
    public async Task EventDetailViewModel_PhotoAthleteFilter_FiltersCorrectlyByPettoraleAndNome()
    {
        var eventoId = Guid.NewGuid();
        var evento = new Evento { Id = eventoId, NomeEvento = "Torneo Primavera" };

        var atleta1 = new Atleta { Id = Guid.NewGuid(), EventoId = eventoId, NumeroPettorale = "101", Nome = "Mario", Cognome = "Rossi" };
        var atleta2 = new Atleta { Id = Guid.NewGuid(), EventoId = eventoId, NumeroPettorale = "102", Nome = "Luigi", Cognome = "Bianchi" };
        var disciplina = new Disciplina { Id = Guid.NewGuid(), EventoId = eventoId, NomeDisciplina = "Nuoto" };

        var foto1 = new Foto { Id = Guid.NewGuid(), EventoId = eventoId, AtletaId = atleta1.Id, DisciplinaId = disciplina.Id, PathRelativo = "foto1.jpg", Formato = "JPEG" };
        var foto2 = new Foto { Id = Guid.NewGuid(), EventoId = eventoId, AtletaId = atleta2.Id, DisciplinaId = disciplina.Id, PathRelativo = "foto2.jpg", Formato = "JPEG" };

        var mockExcel = new Mock<IExcelRepository>();
        mockExcel.Setup(r => r.GetEventDataBundleAsync(eventoId)).ReturnsAsync(new EventDataBundle
        {
            Atleti = new List<Atleta> { atleta1, atleta2 },
            Discipline = new List<Disciplina> { disciplina },
            Foto = new List<Foto> { foto1, foto2 },
            CatalogoPrezzi = new List<PrezzoCatalogoItem>(),
            Acquisti = new List<AcquistoFoto>()
        });
        mockExcel.Setup(r => r.GetBasePathAsync()).ReturnsAsync(@"C:\Archivio");

        var mockFileOrg = new Mock<IFileOrganizationService>();
        var vm = new EventDetailViewModel(evento, mockExcel.Object, mockFileOrg.Object);
        await vm.LoadEventDataAsync();

        // Senza filtro: entrambi presenti
        vm.AthletePhotoGroups.Should().HaveCount(2);
        vm.FlatActivePhotoItems.Should().HaveCount(2);

        // Filtra per pettorale "101"
        vm.PhotoAthleteFilter = "101";
        vm.AthletePhotoGroups.Should().HaveCount(1);
        vm.AthletePhotoGroups[0].Atleta.NumeroPettorale.Should().Be("101");
        vm.FlatActivePhotoItems.Should().HaveCount(1);
        vm.FlatActivePhotoItems[0].Foto.Id.Should().Be(foto1.Id);

        // Filtra per cognome "Bianchi"
        vm.PhotoAthleteFilter = "Bianchi";
        vm.AthletePhotoGroups.Should().HaveCount(1);
        vm.AthletePhotoGroups[0].Atleta.Cognome.Should().Be("Bianchi");
        vm.FlatActivePhotoItems.Should().HaveCount(1);
        vm.FlatActivePhotoItems[0].Foto.Id.Should().Be(foto2.Id);

        // Filtro non corrispondente
        vm.PhotoAthleteFilter = "999";
        vm.AthletePhotoGroups.Should().BeEmpty();
        vm.FlatActivePhotoItems.Should().BeEmpty();

        // Ripristino filtro vuoto
        vm.PhotoAthleteFilter = "";
        vm.AthletePhotoGroups.Should().HaveCount(2);
        vm.FlatActivePhotoItems.Should().HaveCount(2);
    }

    [Fact]
    public void MainViewModel_OnJobCompleted_TriggersPhotoRefresh_ForActiveEvent()
    {
        var eventoId = Guid.NewGuid();
        var evento = new Evento { Id = eventoId, NomeEvento = "Gara Slalom" };

        var mockExcel = new Mock<IExcelRepository>();
        mockExcel.Setup(r => r.GetBasePathAsync()).ReturnsAsync(@"C:\Archivio");
        mockExcel.Setup(r => r.GetFotoByEventoAsync(eventoId)).ReturnsAsync(new List<Foto>());
        mockExcel.Setup(r => r.GetAtletiByEventoAsync(eventoId)).ReturnsAsync(new List<Atleta>());

        var mockPipeline = new Mock<IIngestionPipelineService>();
        var mockSd = new Mock<ISdCardWatcherService>();
        var mockFileOrg = new Mock<IFileOrganizationService>();

        var mainVm = new MainViewModel(mockExcel.Object, mockFileOrg.Object, mockPipeline.Object, mockSd.Object);
        var detailVm = new EventDetailViewModel(evento, mockExcel.Object, mockFileOrg.Object);
        mainVm.ActiveEventDetail = detailVm;

        // Raise JobCompleted with matching EventoId
        var report = new IngestionProgressReport
        {
            JobId = Guid.NewGuid(),
            EventoId = eventoId,
            Status = IngestionStatus.Completed
        };

        mockPipeline.Raise(p => p.JobCompleted += null, mockPipeline.Object, report);

        // Verifica che GetFotoByEventoAsync sia stato chiamato per l'evento attivo
        mockExcel.Verify(r => r.GetFotoByEventoAsync(eventoId), Times.AtLeastOnce());
    }

    [Fact]
    public void IngestionWizardViewModel_IsPremiazioni_ClearsAndDisablesAthleteAndDiscipline()
    {
        var evento = new Evento { Id = Guid.NewGuid(), NomeEvento = "Trial 2026" };
        var atleta = new Atleta { Id = Guid.NewGuid(), NumeroPettorale = "1", Nome = "Mario", Cognome = "Rossi" };
        var disc = new Disciplina { Id = Guid.NewGuid(), NomeDisciplina = "Slalom" };
        var mockWatcher = new Mock<ISdCardWatcherService>();

        var vm = new IngestionWizardViewModel(evento, new[] { atleta }, new[] { disc }, mockWatcher.Object);
        vm.SelectedAtleta = atleta;
        vm.SelectedDisciplina = disc;

        vm.IsPremiazioni = true;

        vm.SelectedAtleta.Should().BeNull();
        vm.SelectedDisciplina.Should().BeNull();

        // Deselezione Premiazioni: deve ripristinare il primo atleta e la prima disciplina come default
        vm.IsPremiazioni = false;

        vm.SelectedAtleta.Should().Be(atleta);
        vm.SelectedDisciplina.Should().Be(disc);
    }

    [Fact]
    public void IngestionWizardViewModel_StartIngestion_AllowsNullAthleteWhenPremiazioni()
    {
        var evento = new Evento { Id = Guid.NewGuid(), NomeEvento = "Trial 2026" };
        var mockWatcher = new Mock<ISdCardWatcherService>();

        var tempSource = Path.Combine(Path.GetTempPath(), "TempSourcePrem_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempSource);
        try
        {
            var vm = new IngestionWizardViewModel(evento, Array.Empty<Atleta>(), Array.Empty<Disciplina>(), mockWatcher.Object);
            vm.SourceDirectory = tempSource;
            vm.IsPremiazioni = true;

            IngestionJobRequest? generatedRequest = null;
            vm.RequestClose += req => generatedRequest = req;

            vm.StartIngestionCommand.Execute(null);

            generatedRequest.Should().NotBeNull();
            generatedRequest!.IsPremiazioni.Should().BeTrue();
            generatedRequest.AtletaTarget.Should().BeNull();
            generatedRequest.DisciplinaTarget.Should().BeNull();
            vm.ErrorMessage.Should().BeNull();
        }
        finally
        {
            Directory.Delete(tempSource, recursive: true);
        }
    }

    [Fact]
    public async Task EventDetailViewModel_SeparatesAthleteAndPremiazioniPhotos()
    {
        var eventoId = Guid.NewGuid();
        var atletaId = Guid.NewGuid();
        var evento = new Evento { Id = eventoId, NomeEvento = "Coppa del Mondo 2026" };
        var atleta = new Atleta { Id = atletaId, EventoId = eventoId, NumeroPettorale = "10", Nome = "Mario", Cognome = "Rossi" };
        var disc = new Disciplina { Id = Guid.NewGuid(), EventoId = eventoId, NomeDisciplina = "Slalom" };

        var fotoAtleta = new Foto
        {
            Id = Guid.NewGuid(),
            EventoId = eventoId,
            AtletaId = atletaId,
            DisciplinaId = disc.Id,
            NomeFileOriginale = "ROSSI_01.JPG",
            PathRelativo = @"Coppa del Mondo 2026\10_Rossi_Mario\Slalom\Jpeg\ROSSI_01.JPG",
            IsPremiazione = false,
            Formato = "JPEG"
        };

        var fotoPremiazione = new Foto
        {
            Id = Guid.NewGuid(),
            EventoId = eventoId,
            NomeFileOriginale = "PODIO_01.JPG",
            PathRelativo = @"Coppa del Mondo 2026\Premiazioni\PODIO_01.JPG",
            IsPremiazione = true,
            Formato = "JPEG"
        };

        var mockExcel = new Mock<IExcelRepository>();
        mockExcel.Setup(r => r.GetBasePathAsync(It.IsAny<CancellationToken>())).ReturnsAsync(@"C:\Foto");
        mockExcel.Setup(r => r.GetAtletiByEventoAsync(eventoId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Atleta> { atleta });
        mockExcel.Setup(r => r.GetDisciplineByEventoAsync(eventoId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Disciplina> { disc });
        mockExcel.Setup(r => r.GetFotoByEventoAsync(eventoId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Foto> { fotoAtleta, fotoPremiazione });
        mockExcel.Setup(r => r.GetAcquistiByEventoAsync(eventoId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<AcquistoFoto>());
        mockExcel.Setup(r => r.GetCatalogoPrezziAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<PrezzoCatalogoItem>());

        var mockFileOrg = new Mock<IFileOrganizationService>();

        var vm = new EventDetailViewModel(evento, mockExcel.Object, mockFileOrg.Object);
        await vm.LoadEventDataAsync();

        // I gruppi atleta devono contenere solo le foto degli atleti
        vm.AthletePhotoGroups.Should().HaveCount(1);
        vm.AthletePhotoGroups[0].Atleta.Id.Should().Be(atletaId);
        vm.FlatActivePhotoItems.Should().HaveCount(1);
        vm.FlatActivePhotoItems[0].Foto.Id.Should().Be(fotoAtleta.Id);

        // La collezione PremiazioniPhotos deve contenere la foto della premiazione
        vm.PremiazioniPhotos.Should().HaveCount(1);
        vm.PremiazioniPhotos[0].Foto.Id.Should().Be(fotoPremiazione.Id);
        vm.PremiazioniPhotos[0].AtletaDisplay.Should().Be("Premiazioni");
    }

    [Fact]
    public async Task SlideshowConfigViewModel_AllowsStartWithOnlyPremiazioni()
    {
        var mockRepo = new Mock<IExcelRepository>();
        var ev = new Evento { NomeEvento = "Coppa 2026" };
        var atleta = new Atleta { Id = Guid.NewGuid(), NumeroPettorale = "1", Nome = "Mario", Cognome = "Rossi" };
        var fotoPremiazione = new Foto
        {
            Id = Guid.NewGuid(),
            EventoId = ev.Id,
            NomeFileOriginale = "PODIO.JPG",
            PathRelativo = @"Coppa 2026\Premiazioni\PODIO.JPG",
            IsPremiazione = true,
            Formato = "JPEG"
        };

        mockRepo.Setup(r => r.GetAtletiByEventoAsync(ev.Id, default)).ReturnsAsync(new List<Atleta> { atleta });
        mockRepo.Setup(r => r.GetFotoByEventoAsync(ev.Id, default)).ReturnsAsync(new List<Foto> { fotoPremiazione });

        var vm = new SlideshowConfigViewModel(mockRepo.Object, new[] { ev }, Array.Empty<DisplayScreenInfo>(), ev);
        await vm.LoadAtletiForSelectedEventoAsync(ev.Id);

        // Deseleziona tutti gli atleti, ma mantieni IncludePremiazioni = true
        vm.DeselectAllAtletiCommand.Execute(null);
        vm.IncludePremiazioni = true;

        SlideshowConfig? startedConfig = null;
        vm.RequestStartSlideshow += c => startedConfig = c;

        await vm.StartSlideshowAsync();

        startedConfig.Should().NotBeNull();
        startedConfig!.IncludePremiazioni.Should().BeTrue();
        startedConfig.SelectedAtletiIds.Should().BeEmpty();
        vm.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task SlideshowWindowViewModel_LoadsPremiazioniPhotos_WithCustomHUD()
    {
        var evId = Guid.NewGuid();
        var ev = new Evento { Id = evId, NomeEvento = "Coppa 2026" };
        var fotoPremiazione = new Foto
        {
            Id = Guid.NewGuid(),
            EventoId = evId,
            NomeFileOriginale = "PODIO.JPG",
            PathRelativo = "PODIO.JPG",
            IsPremiazione = true,
            Formato = "JPEG"
        };

        var tempDir = Path.Combine(Path.GetTempPath(), "SlideshowPremTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var dummyPhoto = Path.Combine(tempDir, "PODIO.JPG");
        await File.WriteAllBytesAsync(dummyPhoto, new byte[] { 1, 2, 3 });

        try
        {
            var mockRepo = new Mock<IExcelRepository>();
            mockRepo.Setup(r => r.GetBasePathAsync(default)).ReturnsAsync(tempDir);
            mockRepo.Setup(r => r.GetEventoByIdAsync(evId, default)).ReturnsAsync(ev);
            mockRepo.Setup(r => r.GetFotoByEventoAsync(evId, default)).ReturnsAsync(new List<Foto> { fotoPremiazione });
            mockRepo.Setup(r => r.GetAtletiByEventoAsync(evId, default)).ReturnsAsync(new List<Atleta>());
            mockRepo.Setup(r => r.GetDisciplineByEventoAsync(evId, default)).ReturnsAsync(new List<Disciplina>());

            var config = new SlideshowConfig
            {
                EventoId = evId,
                EventoNome = "Coppa 2026",
                IncludePremiazioni = true,
                IncludeJpegPng = true,
                SelectedAtletiIds = new List<Guid>(),
                SelectedTransitionIds = new List<string> { "crossfade" }
            };

            var mockImg = new Mock<IImage>().Object;
            var vm = new SlideshowWindowViewModel(config, mockRepo.Object)
            {
                ImageLoader = path => Task.FromResult<IImage?>(mockImg)
            };
            await vm.StartAsync();

            vm.CurrentAtletaText.Should().Be("Premiazioni");
            vm.CurrentDisciplinaText.Should().Be("Podio & Premiazioni");
            vm.CurrentPhotoNameText.Should().Be("PODIO.JPG");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void SettingsViewModel_AboutInformation_ExposesComponentVersions_Copyright_And_GitHubUrl()
    {
        var mockRepo = new Mock<IExcelRepository>();
        var mockPrefs = new Mock<IAppPreferencesService>();

        var vm = new SettingsViewModel(mockRepo.Object, mockPrefs.Object);

        vm.CoreVersion.Should().Be("0.5.5");
        vm.InfrastructureVersion.Should().Be("0.5.5");
        vm.UiVersion.Should().Be("0.5.5");
        vm.CopyrightText.Should().Contain("zmalrobot").And.Contain("2026");
        vm.GitHubUrl.Should().Be("https://github.com/zmalrobot/Paddock");
    }

    [Fact]
    public void PhotoViewerViewModel_CanApplyWatermark_TrueOnlyForJpgAndPng()
    {
        // Arrange
        var tempJpg = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.jpg");
        var tempPng = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.png");
        var tempRaw = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.CR2");
        File.WriteAllBytes(tempJpg, new byte[] { 1, 2, 3 });
        File.WriteAllBytes(tempPng, new byte[] { 1, 2, 3 });
        File.WriteAllBytes(tempRaw, new byte[] { 1, 2, 3 });

        try
        {
            var fotoJpg = new Foto { NomeFileOriginale = "test.jpg", Formato = "JPEG" };
            var fotoPng = new Foto { NomeFileOriginale = "test.png", Formato = "PNG" };
            var fotoRaw = new Foto { NomeFileOriginale = "test.cr2", Formato = "RAW" };

            var itemJpg = new PhotoItemViewModel(fotoJpg, tempJpg);
            var itemPng = new PhotoItemViewModel(fotoPng, tempPng);
            var itemRaw = new PhotoItemViewModel(fotoRaw, tempRaw);

            var vm = new PhotoViewerViewModel(new[] { itemJpg, itemPng, itemRaw }, 0);

            // Act & Assert 1: JPG
            vm.CurrentIndex = 0;
            vm.CanApplyWatermark.Should().BeTrue();

            // Act & Assert 2: PNG
            vm.CurrentIndex = 1;
            vm.CanApplyWatermark.Should().BeTrue();

            // Act & Assert 3: RAW
            vm.CurrentIndex = 2;
            vm.CanApplyWatermark.Should().BeFalse();
        }
        finally
        {
            try { File.Delete(tempJpg); } catch { }
            try { File.Delete(tempPng); } catch { }
            try { File.Delete(tempRaw); } catch { }
        }
    }

    [Fact]
    public async Task PhotoViewerViewModel_ApplyWatermarkAndMetadata_UpdatesFileAndModel()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"paddock_test_wm_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var testPhotoPath = Path.Combine(tempDir, "sample.jpg");
        File.WriteAllBytes(testPhotoPath, new byte[] { 10, 20, 30 });

        var mockImageService = new Mock<IImageProcessingService>();
        mockImageService
            .Setup(s => s.ApplyWatermarkAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<WatermarkOptions>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, WatermarkOptions, CancellationToken>((src, dst, opt, ct) =>
            {
                File.WriteAllBytes(dst, new byte[] { 10, 20, 30, 40, 50 });
                return Task.CompletedTask;
            });

        var mockMetadataService = new Mock<IMetadataService>();
        mockMetadataService
            .Setup(s => s.WritePhotographerMetadataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var mockExcelRepo = new Mock<IExcelRepository>();
        var mockPrefs = new Mock<IAppPreferencesService>();

        try
        {
            var foto = new Foto
            {
                NomeFileOriginale = "sample.jpg",
                Formato = "JPEG",
                WatermarkApplicato = false,
                DimensioneByte = 3
            };
            var item = new PhotoItemViewModel(foto, testPhotoPath);
            var vm = new PhotoViewerViewModel(
                new[] { item },
                0,
                mockImageService.Object,
                null,
                mockMetadataService.Object,
                mockPrefs.Object,
                mockExcelRepo.Object);

            vm.CanApplyWatermark.Should().BeTrue();
            vm.IsWatermarkDialogOpen.Should().BeFalse();

            // Act: apri dialog
            vm.OpenWatermarkDialog();
            vm.IsWatermarkDialogOpen.Should().BeTrue();
            vm.WatermarkDialog.Should().NotBeNull();

            // Applica watermark e metadati
            var wmOpt = new WatermarkOptions { Enabled = true, WatermarkImagePath = "dummy.png" };
            var metaOpt = new MetadataOptions { InjectPhotographer = true, PhotographerName = "Fotografo Test", CopyrightNotice = "Copyright 2026" };
            var success = await vm.ApplyWatermarkAndMetadataAsync(wmOpt, metaOpt);

            // Assert
            success.Should().BeTrue();
            vm.WatermarkApplicato.Should().BeTrue();
            item.Foto.WatermarkApplicato.Should().BeTrue();
            item.Foto.Fotografo.Should().Be("Fotografo Test");
            item.Foto.DimensioneByte.Should().Be(5);
            mockExcelRepo.Verify(x => x.UpdateFotoAsync(It.IsAny<Foto>(), It.IsAny<CancellationToken>()), Times.Once);

            // Chiudi dialog
            vm.CloseWatermarkDialog();
            vm.IsWatermarkDialogOpen.Should().BeFalse();
            vm.WatermarkDialog.Should().BeNull();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }
}

