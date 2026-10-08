using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paddock.Core.Interfaces;
using Paddock.Core.Models;
using Paddock.Core.DTOs;

namespace Paddock.UI.ViewModels;

public partial class EventDetailViewModel : ViewModelBase
{
    private readonly IExcelRepository _excelRepo;
    private readonly IFileOrganizationService _fileOrgService;
    private readonly IImageProcessingService? _imageService;
    private readonly IMetadataService? _metadataService;
    private readonly IAppPreferencesService? _prefsService;

    [ObservableProperty]
    private Evento _evento;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _busyMessage = "Caricamento evento in corso...";

    [ObservableProperty]
    private int _selectedTabIndex = 0; // 0 = Atleti, 1 = Discipline, 2 = Browser Foto

    // Ricerca atleti
    [ObservableProperty]
    private string _athleteSearchFilter = string.Empty;

    // Form rapido nuovo atleta
    [ObservableProperty]
    private string _newPettorale = string.Empty;

    [ObservableProperty]
    private string _newNome = string.Empty;

    [ObservableProperty]
    private string _newCognome = string.Empty;

    [ObservableProperty]
    private string _newCategoria = string.Empty;

    // Form rapido nuova disciplina
    [ObservableProperty]
    private string _newNomeDisciplina = string.Empty;

    [ObservableProperty]
    private string _newDescrizioneDisciplina = string.Empty;

    // Filtro foto
    [ObservableProperty]
    private string _fotoFormatFilter = "TUTTI"; // "TUTTI", "JPEG", "RAW"

    [ObservableProperty]
    private string _photoAthleteFilter = string.Empty;

    // Gestione Acquisti Foto
    [ObservableProperty]
    private Atleta? _selectedAcquistoAtleta;

    [ObservableProperty]
    private Disciplina? _selectedAcquistoDisciplina;

    [ObservableProperty]
    private string _emailCliente = string.Empty;

    [ObservableProperty]
    private string _telefonoCliente = string.Empty;

    [ObservableProperty]
    private bool _interaCartella = true;

    [ObservableProperty]
    private string _noteAcquisto = string.Empty;

    [ObservableProperty]
    private PrezzoCatalogoItem? _selectedCatalogoItemToAdd;

    [ObservableProperty]
    private int _quantitaToAdd = 1;

    [ObservableProperty]
    private decimal _totaleCalcolato = 0.0m;

    [ObservableProperty]
    private decimal _totalePagato = 0.0m;

    [ObservableProperty]
    private decimal _totaleIncassatoEvento = 0.0m;

    [ObservableProperty]
    private int _totaleOrdiniEvento = 0;

    [ObservableProperty]
    private string? _acquistoStatusMessage;

    [ObservableProperty]
    private bool _isAcquistoErrorMessage;

    public ObservableCollection<Atleta> AllAtleti { get; } = new();
    public ObservableCollection<Atleta> FilteredAtleti { get; } = new();
    public ObservableCollection<Disciplina> Discipline { get; } = new();
    public ObservableCollection<Foto> AllFoto { get; } = new();
    public ObservableCollection<Foto> FilteredFoto { get; } = new();

    // Browser Foto Gerarchico & Viewer Standalone
    public ObservableCollection<PhotoBrowserAthleteGroup> AthletePhotoGroups { get; } = new();
    public List<PhotoItemViewModel> FlatActivePhotoItems { get; } = new();
    public ObservableCollection<PhotoItemViewModel> PremiazioniPhotos { get; } = new();

    public ObservableCollection<AcquistoFoto> Acquisti { get; } = new();
    public ObservableCollection<PrezzoCatalogoItem> CatalogoDisponibile { get; } = new();
    public ObservableCollection<VoceAcquisto> VociAcquistoCorrente { get; } = new();
    public ObservableCollection<FotoSelectionItem> FotoDisponibiliAtleta { get; } = new();

    public event Action<Evento>? RequestStartIngestion;
    public event Action<Evento>? RequestEditEvent;
    public event Action<Evento>? RequestDeleteEvent;
    public event Action<PhotoViewerViewModel>? RequestOpenPhotoViewer;
    public event Action<Evento, Atleta>? RequestOpenRawConversion;

    public EventDetailViewModel(
        Evento evento,
        IExcelRepository excelRepo,
        IFileOrganizationService fileOrgService,
        IImageProcessingService? imageService = null,
        IMetadataService? metadataService = null,
        IAppPreferencesService? prefsService = null)
    {
        _evento = evento;
        _excelRepo = excelRepo;
        _fileOrgService = fileOrgService;
        _imageService = imageService;
        _metadataService = metadataService;
        _prefsService = prefsService;
    }

    public async Task LoadEventDataAsync()
    {
        IsBusy = true;
        BusyMessage = "Caricamento evento in corso...";
        try
        {
            var bundle = await _excelRepo.GetEventDataBundleAsync(Evento.Id);
            if (bundle == null)
            {
                var atleti = await _excelRepo.GetAtletiByEventoAsync(Evento.Id);
                var disc = await _excelRepo.GetDisciplineByEventoAsync(Evento.Id);
                var foto = await _excelRepo.GetFotoByEventoAsync(Evento.Id);
                var catalogo = await _excelRepo.GetCatalogoPrezziAsync();
                var acquisti = await _excelRepo.GetAcquistiByEventoAsync(Evento.Id);
                bundle = new EventDataBundle
                {
                    Atleti = atleti,
                    Discipline = disc,
                    Foto = foto,
                    CatalogoPrezzi = catalogo,
                    Acquisti = acquisti
                };
            }

            // 1. Carica atleti
            AllAtleti.Clear();
            foreach (var a in bundle.Atleti ?? Enumerable.Empty<Atleta>()) AllAtleti.Add(a);
            ApplyAthleteFilter();

            // 2. Carica discipline
            Discipline.Clear();
            foreach (var d in bundle.Discipline ?? Enumerable.Empty<Disciplina>()) Discipline.Add(d);

            // 3. Carica foto
            AllFoto.Clear();
            foreach (var f in bundle.Foto ?? Enumerable.Empty<Foto>()) AllFoto.Add(f);
            await RebuildHierarchicalPhotoGroupsAsync();
            await RebuildPremiazioniPhotosAsync();

            // 4. Carica catalogo e acquisti evento
            CatalogoDisponibile.Clear();
            foreach (var c in bundle.CatalogoPrezzi ?? Enumerable.Empty<PrezzoCatalogoItem>()) CatalogoDisponibile.Add(c);
            SelectedCatalogoItemToAdd = CatalogoDisponibile.FirstOrDefault();

            Acquisti.Clear();
            foreach (var a in bundle.Acquisti ?? Enumerable.Empty<AcquistoFoto>()) Acquisti.Add(a);

            TotaleIncassatoEvento = Acquisti.Sum(a => a.TotalePagato);
            TotaleOrdiniEvento = Acquisti.Count;

            // Aggiorna metriche evento
            Evento.TotaleAtleti = AllAtleti.Count;
            Evento.TotaleDiscipline = Discipline.Count;
            Evento.TotaleFoto = AllFoto.Count;
            Evento.TotaleByteOccupati = AllFoto.Sum(f => f.DimensioneByte);
            OnPropertyChanged(nameof(Evento));
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnAthleteSearchFilterChanged(string value)
    {
        ApplyAthleteFilter();
    }

    partial void OnFotoFormatFilterChanged(string value)
    {
        _ = RebuildHierarchicalPhotoGroupsAsync();
    }

    partial void OnPhotoAthleteFilterChanged(string value)
    {
        _ = RebuildHierarchicalPhotoGroupsAsync();
    }

    private void ApplyAthleteFilter()
    {
        FilteredAtleti.Clear();
        var q = AthleteSearchFilter.Trim();
        var query = string.IsNullOrWhiteSpace(q)
            ? AllAtleti
            : AllAtleti.Where(a => 
                a.NumeroPettorale.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.Nome.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.Cognome.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (a.Categoria?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));

        foreach (var a in query)
        {
            FilteredAtleti.Add(a);
        }
    }

    public async Task RebuildHierarchicalPhotoGroupsAsync()
    {
        FilteredFoto.Clear();
        FlatActivePhotoItems.Clear();
        AthletePhotoGroups.Clear();

        var athletePhotos = AllFoto.Where(f => !f.IsPremiazione);

        var query = FotoFormatFilter switch
        {
            "JPEG" => athletePhotos.Where(f => !f.IsRaw),
            "RAW" => athletePhotos.Where(f => f.IsRaw),
            _ => athletePhotos
        };

        var filteredList = query.ToList();
        foreach (var f in filteredList)
        {
            FilteredFoto.Add(f);
        }

        if (filteredList.Count == 0)
        {
            return;
        }

        var basePath = await _excelRepo.GetResolvedBasePathAsync();
        if (string.IsNullOrWhiteSpace(basePath))
        {
            basePath = (await _excelRepo.GetBasePathAsync()) ?? string.Empty;
        }

        var items = new List<PhotoItemViewModel>();
        foreach (var f in filteredList)
        {
            var fullPath = !string.IsNullOrWhiteSpace(basePath)
                ? Path.Combine(basePath, f.PathRelativo)
                : f.PathRelativo;
            var item = new PhotoItemViewModel(f, fullPath);

            var atleta = AllAtleti.FirstOrDefault(a => a.Id == f.AtletaId);
            item.AtletaDisplay = atleta != null ? atleta.DisplayPettoraleNome : "Atleta Non Assegnato";

            var disciplina = Discipline.FirstOrDefault(d => d.Id == f.DisciplinaId);
            item.DisciplinaDisplay = disciplina != null ? disciplina.NomeDisciplina : "Generale";

            items.Add(item);
        }

        var groupedByAtleta = items
            .GroupBy(i => i.Foto.AtletaId)
            .OrderBy(g => AllAtleti.FirstOrDefault(a => a.Id == g.Key)?.NumeroPettorale)
            .ThenBy(g => AllAtleti.FirstOrDefault(a => a.Id == g.Key)?.Cognome);

        var athleteFilter = PhotoAthleteFilter?.Trim() ?? string.Empty;

        foreach (var atletaGroup in groupedByAtleta)
        {
            var atleta = AllAtleti.FirstOrDefault(a => a.Id == atletaGroup.Key)
                ?? new Atleta { Cognome = "Atleta", Nome = "Non Assegnato" };

            if (!string.IsNullOrWhiteSpace(athleteFilter))
            {
                var matches = (atleta.NumeroPettorale?.Contains(athleteFilter, StringComparison.OrdinalIgnoreCase) ?? false) ||
                              (atleta.Nome?.Contains(athleteFilter, StringComparison.OrdinalIgnoreCase) ?? false) ||
                              (atleta.Cognome?.Contains(athleteFilter, StringComparison.OrdinalIgnoreCase) ?? false) ||
                              (atleta.DisplayPettoraleNome?.Contains(athleteFilter, StringComparison.OrdinalIgnoreCase) ?? false);
                if (!matches)
                {
                    continue;
                }
            }

            var athleteGroupVm = new PhotoBrowserAthleteGroup(atleta);

            var groupedByDisciplina = atletaGroup
                .GroupBy(i => i.Foto.DisciplinaId)
                .OrderBy(g => Discipline.FirstOrDefault(d => d.Id == g.Key)?.NomeDisciplina ?? "Generale");

            foreach (var disciplinaGroup in groupedByDisciplina)
            {
                var disciplina = Discipline.FirstOrDefault(d => d.Id == disciplinaGroup.Key)
                    ?? new Disciplina { NomeDisciplina = "Generale" };

                var disciplinaGroupVm = new PhotoBrowserDisciplinaGroup(disciplina);
                foreach (var photoItem in disciplinaGroup)
                {
                    disciplinaGroupVm.Photos.Add(photoItem);
                    FlatActivePhotoItems.Add(photoItem);
                }

                athleteGroupVm.DisciplineGroups.Add(disciplinaGroupVm);
            }

            athleteGroupVm.SetImageService(_imageService);
            if (athleteGroupVm.IsExpanded)
            {
                athleteGroupVm.LoadThumbnails(_imageService);
            }

            AthletePhotoGroups.Add(athleteGroupVm);
        }
    }

    public async Task RebuildPremiazioniPhotosAsync()
    {
        PremiazioniPhotos.Clear();

        var premiazioniList = AllFoto.Where(f => f.IsPremiazione).ToList();
        if (premiazioniList.Count == 0) return;

        var basePath = await _excelRepo.GetResolvedBasePathAsync();
        if (string.IsNullOrWhiteSpace(basePath))
        {
            basePath = (await _excelRepo.GetBasePathAsync()) ?? string.Empty;
        }

        foreach (var f in premiazioniList)
        {
            var fullPath = !string.IsNullOrWhiteSpace(basePath)
                ? Path.Combine(basePath, f.PathRelativo)
                : f.PathRelativo;
            var item = new PhotoItemViewModel(f, fullPath)
            {
                AtletaDisplay = "Premiazioni",
                DisciplinaDisplay = "Podio & Premiazioni"
            };

            PremiazioniPhotos.Add(item);
            _ = item.LoadThumbnailAsync(_imageService);
        }
    }

    [RelayCommand]
    public void ExpandAllPhotoGroups()
    {
        foreach (var ag in AthletePhotoGroups)
        {
            ag.IsExpanded = true;
            foreach (var dg in ag.DisciplineGroups)
            {
                dg.IsExpanded = true;
            }
        }
    }

    [RelayCommand]
    public void CollapseAllPhotoGroups()
    {
        foreach (var ag in AthletePhotoGroups)
        {
            ag.IsExpanded = false;
            foreach (var dg in ag.DisciplineGroups)
            {
                dg.IsExpanded = false;
            }
        }
    }

    [RelayCommand]
    public void OpenGenerateMissingJpegs(Atleta? atleta)
    {
        if (atleta != null)
        {
            RequestOpenRawConversion?.Invoke(Evento, atleta);
        }
    }

    [RelayCommand]
    public void OpenPremiazioniPhotoViewer(PhotoItemViewModel photoItem)
    {
        if (photoItem == null) return;

        var list = PremiazioniPhotos.ToList();
        var index = list.IndexOf(photoItem);
        if (index < 0) index = 0;

        var viewerVm = new PhotoViewerViewModel(
            list,
            index,
            _imageService,
            deleteCallback: DeleteSinglePhotoAsync,
            metadataService: _metadataService,
            prefsService: _prefsService,
            excelRepo: _excelRepo);

        RequestOpenPhotoViewer?.Invoke(viewerVm);
    }

    [RelayCommand]
    public void OpenPhotoViewer(PhotoItemViewModel photoItem)
    {
        if (photoItem == null) return;

        if (photoItem.Foto.IsPremiazione || PremiazioniPhotos.Contains(photoItem))
        {
            OpenPremiazioniPhotoViewer(photoItem);
            return;
        }

        var index = FlatActivePhotoItems.IndexOf(photoItem);
        if (index < 0) index = 0;

        var viewerVm = new PhotoViewerViewModel(
            FlatActivePhotoItems,
            index,
            _imageService,
            deleteCallback: DeleteSinglePhotoAsync,
            metadataService: _metadataService,
            prefsService: _prefsService,
            excelRepo: _excelRepo);

        RequestOpenPhotoViewer?.Invoke(viewerVm);
    }

    [RelayCommand]
    public async Task<bool> DeleteSinglePhotoAsync(PhotoItemViewModel photoItem)
    {
        if (photoItem == null) return false;

        try
        {
            await _excelRepo.DeleteFotoAsync(photoItem.Foto.Id);

            if (File.Exists(photoItem.FullPath))
            {
                try
                {
                    File.Delete(photoItem.FullPath);
                }
                catch
                {
                    // Fallback se rimosso o bloccato
                }
            }

            var fotoObj = AllFoto.FirstOrDefault(f => f.Id == photoItem.Foto.Id);
            if (fotoObj != null)
            {
                AllFoto.Remove(fotoObj);
            }
            FilteredFoto.Remove(photoItem.Foto);
            FlatActivePhotoItems.Remove(photoItem);
            PremiazioniPhotos.Remove(photoItem);

            foreach (var ag in AthletePhotoGroups)
            {
                foreach (var dg in ag.DisciplineGroups)
                {
                    if (dg.Photos.Remove(photoItem))
                    {
                        ag.NotifyCountChanged();
                        break;
                    }
                }
            }

            for (int i = AthletePhotoGroups.Count - 1; i >= 0; i--)
            {
                var ag = AthletePhotoGroups[i];
                for (int j = ag.DisciplineGroups.Count - 1; j >= 0; j--)
                {
                    if (ag.DisciplineGroups[j].Photos.Count == 0)
                    {
                        ag.DisciplineGroups.RemoveAt(j);
                    }
                }
                if (ag.DisciplineGroups.Count == 0)
                {
                    AthletePhotoGroups.RemoveAt(i);
                }
            }

            Evento.TotaleFoto = AllFoto.Count;
            Evento.TotaleByteOccupati = AllFoto.Sum(f => f.DimensioneByte);
            await _excelRepo.UpsertEventoAsync(Evento);
            OnPropertyChanged(nameof(Evento));

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Errore eliminazione foto: {ex.Message}");
            return false;
        }
    }

    #region Atleti Commands

    [RelayCommand]
    private async Task AddAtletaAsync()
    {
        if (string.IsNullOrWhiteSpace(NewCognome) && string.IsNullOrWhiteSpace(NewNome))
            return;

        var atleta = new Atleta
        {
            EventoId = Evento.Id,
            NumeroPettorale = NewPettorale.Trim(),
            Nome = NewNome.Trim(),
            Cognome = NewCognome.Trim(),
            Categoria = string.IsNullOrWhiteSpace(NewCategoria) ? null : NewCategoria.Trim()
        };

        await _excelRepo.UpsertAtletaAsync(atleta);
        AllAtleti.Add(atleta);
        ApplyAthleteFilter();

        NewPettorale = string.Empty;
        NewNome = string.Empty;
        NewCognome = string.Empty;
        NewCategoria = string.Empty;

        Evento.TotaleAtleti = AllAtleti.Count;
        OnPropertyChanged(nameof(Evento));
    }

    [RelayCommand]
    private async Task DeleteAtletaAsync(Atleta atleta)
    {
        await _excelRepo.DeleteAtletaAsync(atleta.Id);
        AllAtleti.Remove(atleta);
        ApplyAthleteFilter();

        Evento.TotaleAtleti = AllAtleti.Count;
        OnPropertyChanged(nameof(Evento));
    }

    #endregion

    #region Discipline Commands

    [RelayCommand]
    private async Task AddDisciplinaAsync()
    {
        if (string.IsNullOrWhiteSpace(NewNomeDisciplina))
            return;

        var disciplina = new Disciplina
        {
            EventoId = Evento.Id,
            NomeDisciplina = NewNomeDisciplina.Trim(),
            Descrizione = string.IsNullOrWhiteSpace(NewDescrizioneDisciplina) ? null : NewDescrizioneDisciplina.Trim()
        };

        await _excelRepo.UpsertDisciplinaAsync(disciplina);
        Discipline.Add(disciplina);

        NewNomeDisciplina = string.Empty;
        NewDescrizioneDisciplina = string.Empty;

        Evento.TotaleDiscipline = Discipline.Count;
        OnPropertyChanged(nameof(Evento));
    }

    [RelayCommand]
    private async Task DeleteDisciplinaAsync(Disciplina disciplina)
    {
        await _excelRepo.DeleteDisciplinaAsync(disciplina.Id);
        Discipline.Remove(disciplina);

        Evento.TotaleDiscipline = Discipline.Count;
        OnPropertyChanged(nameof(Evento));
    }

    #endregion

    #region Actions

    [RelayCommand]
    private void StartIngestion()
    {
        RequestStartIngestion?.Invoke(Evento);
    }

    [RelayCommand]
    private void EditEvent()
    {
        RequestEditEvent?.Invoke(Evento);
    }

    [RelayCommand]
    private void DeleteEvent()
    {
        RequestDeleteEvent?.Invoke(Evento);
    }

    [RelayCommand]
    public async Task RefreshPhotosAsync()
    {
        IsBusy = true;
        BusyMessage = "Aggiornamento foto in corso...";
        try
        {
            var atleti = await _excelRepo.GetAtletiByEventoAsync(Evento.Id);
            if (atleti != null && atleti.Count > 0)
            {
                AllAtleti.Clear();
                foreach (var a in atleti) AllAtleti.Add(a);
                ApplyAthleteFilter();
            }

            var foto = await _excelRepo.GetFotoByEventoAsync(Evento.Id);
            AllFoto.Clear();
            foreach (var f in foto) AllFoto.Add(f);
            await RebuildHierarchicalPhotoGroupsAsync();
            await RebuildPremiazioniPhotosAsync();

            Evento.TotaleFoto = AllFoto.Count;
            Evento.TotaleByteOccupati = AllFoto.Sum(f => f.DimensioneByte);
            OnPropertyChanged(nameof(Evento));
        }
        finally
        {
            IsBusy = false;
        }
    }

    #endregion

    #region Gestione Acquisti Foto

    public async Task LoadAcquistiDataAsync()
    {
        try
        {
            var catalogo = await _excelRepo.GetCatalogoPrezziAsync();
            CatalogoDisponibile.Clear();
            foreach (var c in catalogo) CatalogoDisponibile.Add(c);
            SelectedCatalogoItemToAdd = CatalogoDisponibile.FirstOrDefault();

            var acquisti = await _excelRepo.GetAcquistiByEventoAsync(Evento.Id);
            Acquisti.Clear();
            foreach (var a in acquisti) Acquisti.Add(a);

            TotaleIncassatoEvento = Acquisti.Sum(a => a.TotalePagato);
            TotaleOrdiniEvento = Acquisti.Count;
        }
        catch (Exception ex)
        {
            AcquistoStatusMessage = $"Errore nel caricamento acquisti: {ex.Message}";
            IsAcquistoErrorMessage = true;
        }
    }

    partial void OnSelectedAcquistoAtletaChanged(Atleta? value)
    {
        _ = CaricaFotoPerAtletaSelezionatoAsync(value);
    }

    public async Task CaricaFotoPerAtletaSelezionatoAsync(Atleta? atleta)
    {
        FotoDisponibiliAtleta.Clear();
        if (atleta == null) return;

        try
        {
            var fotoAtleta = await _excelRepo.GetFotoByAtletaAsync(atleta.Id);
            foreach (var f in fotoAtleta)
            {
                FotoDisponibiliAtleta.Add(new FotoSelectionItem(f, isSelected: true));
            }
        }
        catch
        {
            // Ignora errori di caricamento foto singola
        }
    }

    [RelayCommand]
    public void AddVoceAcquisto()
    {
        if (SelectedCatalogoItemToAdd == null) return;
        var qta = QuantitaToAdd > 0 ? QuantitaToAdd : 1;

        var existing = VociAcquistoCorrente.FirstOrDefault(v => v.PrezzoItemId == SelectedCatalogoItemToAdd.Id);
        if (existing != null)
        {
            existing.Quantita += qta;
        }
        else
        {
            VociAcquistoCorrente.Add(new VoceAcquisto
            {
                PrezzoItemId = SelectedCatalogoItemToAdd.Id,
                NomeArticolo = SelectedCatalogoItemToAdd.Nome,
                Categoria = SelectedCatalogoItemToAdd.Categoria,
                PrezzoUnitario = SelectedCatalogoItemToAdd.Prezzo,
                Quantita = qta
            });
        }

        RecalculateTotals();
        QuantitaToAdd = 1;
    }

    [RelayCommand]
    public void RemoveVoceAcquisto(VoceAcquisto voce)
    {
        if (voce == null) return;
        VociAcquistoCorrente.Remove(voce);
        RecalculateTotals();
    }

    private void RecalculateTotals()
    {
        TotaleCalcolato = VociAcquistoCorrente.Sum(v => v.Subtotale);
        TotalePagato = TotaleCalcolato;
    }

    [RelayCommand]
    public void SelectAllFotoAtleta()
    {
        foreach (var item in FotoDisponibiliAtleta)
        {
            item.IsSelected = true;
        }
    }

    [RelayCommand]
    public void DeselectAllFotoAtleta()
    {
        foreach (var item in FotoDisponibiliAtleta)
        {
            item.IsSelected = false;
        }
    }

    [RelayCommand]
    public async Task RegistraAcquistoAsync()
    {
        AcquistoStatusMessage = null;
        IsAcquistoErrorMessage = false;

        if (SelectedAcquistoAtleta == null)
        {
            AcquistoStatusMessage = "Selezionare l'atleta acquirente.";
            IsAcquistoErrorMessage = true;
            return;
        }

        if (VociAcquistoCorrente.Count == 0)
        {
            AcquistoStatusMessage = "Aggiungere almeno un pacchetto o articolo al carrello dell'acquisto.";
            IsAcquistoErrorMessage = true;
            return;
        }

        if (string.IsNullOrWhiteSpace(EmailCliente) && string.IsNullOrWhiteSpace(TelefonoCliente))
        {
            AcquistoStatusMessage = "Inserire almeno un recapito (Email o Telefono/WhatsApp) per l'invio delle foto.";
            IsAcquistoErrorMessage = true;
            return;
        }

        try
        {
            var resolvedRoot = _excelRepo.ResolvePath(Evento.CartellaDestinazioneRoot);
            var rootDir = !string.IsNullOrWhiteSpace(resolvedRoot)
                ? resolvedRoot
                : (Evento.CartellaDestinazioneRoot ?? string.Empty);
            var cartellaPath = !string.IsNullOrWhiteSpace(rootDir)
                ? Path.Combine(rootDir, Evento.NomeEvento, SelectedAcquistoAtleta.NomeCartellaSanitizzato)
                : Path.Combine(Evento.NomeEvento, SelectedAcquistoAtleta.NomeCartellaSanitizzato);

            var fileSelezionati = FotoDisponibiliAtleta
                .Where(f => f.IsSelected)
                .Select(f => f.Foto.NomeFileOriginale)
                .ToList();

            var acquisto = new AcquistoFoto
            {
                EventoId = Evento.Id,
                DataAcquisto = DateTime.Now,
                AtletaId = SelectedAcquistoAtleta.Id,
                NomeAtleta = SelectedAcquistoAtleta.NomeCompleto,
                NumeroPettorale = SelectedAcquistoAtleta.NumeroPettorale,
                DisciplinaId = SelectedAcquistoDisciplina?.Id,
                NomeDisciplina = SelectedAcquistoDisciplina?.NomeDisciplina ?? "Tutte",
                Voci = VociAcquistoCorrente.ToList(),
                TotaleQuantita = VociAcquistoCorrente.Sum(v => v.Quantita),
                TotaleCalcolato = TotaleCalcolato,
                TotalePagato = TotalePagato,
                EmailCliente = EmailCliente.Trim(),
                TelefonoCliente = TelefonoCliente.Trim(),
                InteraCartella = InteraCartella,
                FileFotoSelezionate = fileSelezionati,
                CartellaPathRiferimento = cartellaPath,
                Note = NoteAcquisto.Trim(),
                Stato = "Completato"
            };

            await _excelRepo.UpsertAcquistoFotoAsync(acquisto);
            Acquisti.Insert(0, acquisto);

            TotaleIncassatoEvento = Acquisti.Sum(a => a.TotalePagato);
            TotaleOrdiniEvento = Acquisti.Count;

            // Reset form
            VociAcquistoCorrente.Clear();
            RecalculateTotals();
            EmailCliente = string.Empty;
            TelefonoCliente = string.Empty;
            NoteAcquisto = string.Empty;
            InteraCartella = true;

            AcquistoStatusMessage = $"Acquisto registrato con successo per {acquisto.NomeAtleta}! (€ {acquisto.TotalePagato:N2})";
            IsAcquistoErrorMessage = false;
        }
        catch (Exception ex)
        {
            AcquistoStatusMessage = $"Errore durante la registrazione dell'acquisto: {ex.Message}";
            IsAcquistoErrorMessage = true;
        }
    }

    [RelayCommand]
    public async Task DeleteAcquistoAsync(AcquistoFoto acquisto)
    {
        if (acquisto == null) return;

        try
        {
            await _excelRepo.DeleteAcquistoFotoAsync(acquisto.Id);
            Acquisti.Remove(acquisto);
            TotaleIncassatoEvento = Acquisti.Sum(a => a.TotalePagato);
            TotaleOrdiniEvento = Acquisti.Count;
            AcquistoStatusMessage = $"Ordine eliminato.";
            IsAcquistoErrorMessage = false;
        }
        catch (Exception ex)
        {
            AcquistoStatusMessage = $"Errore durante l'eliminazione dell'ordine: {ex.Message}";
            IsAcquistoErrorMessage = true;
        }
    }

    #endregion
}

public partial class FotoSelectionItem : ObservableObject
{
    public Foto Foto { get; set; }

    [ObservableProperty]
    private bool _isSelected;

    public FotoSelectionItem(Foto foto, bool isSelected = true)
    {
        Foto = foto;
        _isSelected = isSelected;
    }
}

