using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paddock.Core.Models;

namespace Paddock.UI.ViewModels;

public partial class EventEditDialogViewModel : ViewModelBase
{
    public Evento Evento { get; }
    public bool IsNew { get; }

    public string DialogTitle => IsNew ? "Nuovo Evento Sportivo" : "Modifica Evento Sportivo";

    [ObservableProperty]
    private string _nomeEvento = string.Empty;

    [ObservableProperty]
    private DateTime? _dataInizio = DateTime.Today;

    [ObservableProperty]
    private DateTime? _dataFine = DateTime.Today;

    [ObservableProperty]
    private string _luogo = string.Empty;

    [ObservableProperty]
    private string _cartellaDestinazioneRoot = string.Empty;

    [ObservableProperty]
    private string _resolvedCartellaDestinazioneRoot = string.Empty;

    [ObservableProperty]
    private string? _note;

    [ObservableProperty]
    private string? _errorMessage;

    private readonly Func<string?, string>? _pathResolver;

    public event Action<bool>? RequestClose;

    public EventEditDialogViewModel(Evento? evento = null, string? defaultRootPath = null, Func<string?, string>? pathResolver = null)
    {
        IsNew = evento == null;
        Evento = evento ?? new Evento();
        _pathResolver = pathResolver;

        var fallbackPath = !string.IsNullOrWhiteSpace(defaultRootPath)
            ? defaultRootPath
            : @"..\Foto";

        if (evento != null)
        {
            NomeEvento = evento.NomeEvento;
            DataInizio = evento.DataInizio;
            DataFine = evento.DataFine;
            Luogo = evento.Luogo;
            CartellaDestinazioneRoot = !string.IsNullOrWhiteSpace(evento.CartellaDestinazioneRoot)
                ? evento.CartellaDestinazioneRoot
                : fallbackPath;
            Note = evento.Note;
        }
        else
        {
            CartellaDestinazioneRoot = fallbackPath;
        }

        UpdateResolvedRoot();
    }

    partial void OnCartellaDestinazioneRootChanged(string value)
    {
        UpdateResolvedRoot();
    }

    private void UpdateResolvedRoot()
    {
        if (string.IsNullOrWhiteSpace(CartellaDestinazioneRoot))
        {
            ResolvedCartellaDestinazioneRoot = string.Empty;
            return;
        }

        if (_pathResolver != null)
        {
            ResolvedCartellaDestinazioneRoot = _pathResolver(CartellaDestinazioneRoot);
        }
        else if (Path.IsPathRooted(CartellaDestinazioneRoot))
        {
            ResolvedCartellaDestinazioneRoot = Path.GetFullPath(CartellaDestinazioneRoot);
        }
        else
        {
            ResolvedCartellaDestinazioneRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, CartellaDestinazioneRoot));
        }
    }

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(NomeEvento))
        {
            ErrorMessage = "Il nome dell'evento è obbligatorio.";
            return;
        }

        if (string.IsNullOrWhiteSpace(CartellaDestinazioneRoot))
        {
            CartellaDestinazioneRoot = @"..\Foto";
        }

        Evento.NomeEvento = NomeEvento.Trim();
        Evento.DataInizio = DataInizio ?? DateTime.Today;
        Evento.DataFine = DataFine ?? DateTime.Today;
        Evento.Luogo = Luogo.Trim();
        Evento.CartellaDestinazioneRoot = CartellaDestinazioneRoot.Trim();
        Evento.Note = Note?.Trim();

        RequestClose?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose?.Invoke(false);
    }
}

