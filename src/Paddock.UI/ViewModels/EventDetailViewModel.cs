using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paddock.Core.Interfaces;
using Paddock.Core.Models;

namespace Paddock.UI.ViewModels;

public partial class EventDetailViewModel : ViewModelBase
{
    private readonly IExcelRepository _excelRepo;
    private readonly IFileOrganizationService _fileOrgService;

    [ObservableProperty]
    private Evento _evento;

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

    public ObservableCollection<AcquistoFoto> Acquisti { get; } = new();
    public ObservableCollection<PrezzoCatalogoItem> CatalogoDisponibile { get; } = new();
    public ObservableCollection<VoceAcquisto> VociAcquistoCorrente { get; } = new();
    public ObservableCollection<FotoSelectionItem> FotoDisponibiliAtleta { get; } = new();

    public event Action<Evento>? RequestStartIngestion;
    public event Action<Evento>? RequestEditEvent;
    public event Action<Evento>? RequestDeleteEvent;

    public EventDetailViewModel(
        Evento evento,
        IExcelRepository excelRepo,
        IFileOrganizationService fileOrgService)
    {
        _evento = evento;
        _excelRepo = excelRepo;
        _fileOrgService = fileOrgService;
    }

    public async Task LoadEventDataAsync()
    {
        // 1. Carica atleti
        var atleti = await _excelRepo.GetAtletiByEventoAsync(Evento.Id);
        AllAtleti.Clear();
        foreach (var a in atleti) AllAtleti.Add(a);
        ApplyAthleteFilter();

        // 2. Carica discipline
        var disc = await _excelRepo.GetDisciplineByEventoAsync(Evento.Id);
        Discipline.Clear();
        foreach (var d in disc) Discipline.Add(d);

        // 3. Carica foto
        var foto = await _excelRepo.GetFotoByEventoAsync(Evento.Id);
        AllFoto.Clear();
        foreach (var f in foto) AllFoto.Add(f);
        ApplyPhotoFilter();

        // 4. Carica catalogo e acquisti evento
        await LoadAcquistiDataAsync();

        // Aggiorna metriche evento
        Evento.TotaleAtleti = AllAtleti.Count;
        Evento.TotaleDiscipline = Discipline.Count;
        Evento.TotaleFoto = AllFoto.Count;
        Evento.TotaleByteOccupati = AllFoto.Sum(f => f.DimensioneByte);
        OnPropertyChanged(nameof(Evento));
    }

    partial void OnAthleteSearchFilterChanged(string value)
    {
        ApplyAthleteFilter();
    }

    partial void OnFotoFormatFilterChanged(string value)
    {
        ApplyPhotoFilter();
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

    private void ApplyPhotoFilter()
    {
        FilteredFoto.Clear();
        var query = FotoFormatFilter switch
        {
            "JPEG" => AllFoto.Where(f => !f.IsRaw),
            "RAW" => AllFoto.Where(f => f.IsRaw),
            _ => AllFoto.AsEnumerable()
        };

        foreach (var f in query)
        {
            FilteredFoto.Add(f);
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
    private async Task RefreshPhotosAsync()
    {
        var foto = await _excelRepo.GetFotoByEventoAsync(Evento.Id);
        AllFoto.Clear();
        foreach (var f in foto) AllFoto.Add(f);
        ApplyPhotoFilter();

        Evento.TotaleFoto = AllFoto.Count;
        Evento.TotaleByteOccupati = AllFoto.Sum(f => f.DimensioneByte);
        OnPropertyChanged(nameof(Evento));
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
            var cartellaPath = Path.Combine(
                Evento.CartellaDestinazioneRoot,
                Evento.NomeEvento,
                SelectedAcquistoAtleta.NomeCartellaSanitizzato);

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

