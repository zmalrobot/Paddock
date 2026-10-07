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

    public ObservableCollection<Atleta> AllAtleti { get; } = new();
    public ObservableCollection<Atleta> FilteredAtleti { get; } = new();
    public ObservableCollection<Disciplina> Discipline { get; } = new();
    public ObservableCollection<Foto> AllFoto { get; } = new();
    public ObservableCollection<Foto> FilteredFoto { get; } = new();

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
}

