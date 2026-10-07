using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paddock.Core.DTOs;
using Paddock.Core.Enums;
using Paddock.Core.Interfaces;
using Paddock.Core.Models;

namespace Paddock.UI.ViewModels;

public partial class IngestionWizardViewModel : ViewModelBase
{
    private readonly ISdCardWatcherService _sdCardWatcher;

    public Evento Evento { get; }

    [ObservableProperty]
    private string _sourceDirectory = string.Empty;

    [ObservableProperty]
    private Atleta? _selectedAtleta;

    [ObservableProperty]
    private Disciplina? _selectedDisciplina;

    [ObservableProperty]
    private bool _watermarkEnabled = false;

    [ObservableProperty]
    private string? _watermarkImagePath;

    [ObservableProperty]
    private float _watermarkOpacity = 0.65f;

    [ObservableProperty]
    private WatermarkPosition _watermarkPosition = WatermarkPosition.BottomRight;

    [ObservableProperty]
    private bool _injectMetadata = true;

    [ObservableProperty]
    private string _photographerName = string.Empty;

    [ObservableProperty]
    private string _copyrightNotice = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    public ObservableCollection<Atleta> Atleti { get; } = new();
    public ObservableCollection<Disciplina> Discipline { get; } = new();
    public ObservableCollection<RemovableDriveInfo> DetectedDrives { get; } = new();
    public List<WatermarkPosition> AvailablePositions { get; } = Enum.GetValues<WatermarkPosition>().ToList();

    public event Action<IngestionJobRequest?>? RequestClose;

    public IngestionWizardViewModel(
        Evento evento,
        IEnumerable<Atleta> atleti,
        IEnumerable<Disciplina> discipline,
        ISdCardWatcherService sdCardWatcher)
    {
        Evento = evento;
        _sdCardWatcher = sdCardWatcher;

        foreach (var a in atleti) Atleti.Add(a);
        foreach (var d in discipline) Discipline.Add(d);

        SelectedAtleta = Atleti.FirstOrDefault();
        SelectedDisciplina = Discipline.FirstOrDefault();

        RefreshDrives();
        _sdCardWatcher.RemovableDrivesChanged += (s, drives) => RefreshDrives();
    }

    private void RefreshDrives()
    {
        DetectedDrives.Clear();
        var drives = _sdCardWatcher.GetCurrentRemovableDrives();
        if (drives != null)
        {
            foreach (var d in drives)
            {
                DetectedDrives.Add(d);
            }
        }

        if (string.IsNullOrEmpty(SourceDirectory) && DetectedDrives.Count > 0)
        {
            SourceDirectory = DetectedDrives[0].DcimPath;
        }
    }

    [RelayCommand]
    private void SelectDrive(RemovableDriveInfo drive)
    {
        SourceDirectory = drive.DcimPath;
    }

    [RelayCommand]
    private void StartIngestion()
    {
        if (string.IsNullOrWhiteSpace(SourceDirectory) || !Directory.Exists(SourceDirectory))
        {
            ErrorMessage = "Seleziona una cartella sorgente o scheda SD valida.";
            return;
        }

        if (SelectedAtleta == null)
        {
            ErrorMessage = "Seleziona l'atleta target per questa importazione.";
            return;
        }

        if (SelectedDisciplina == null)
        {
            ErrorMessage = "Seleziona la disciplina sportiva target.";
            return;
        }

        var request = new IngestionJobRequest
        {
            SourceDirectory = SourceDirectory,
            EventoTarget = Evento,
            AtletaTarget = SelectedAtleta,
            DisciplinaTarget = SelectedDisciplina,
            Watermark = new WatermarkOptions
            {
                Enabled = WatermarkEnabled,
                WatermarkImagePath = WatermarkImagePath,
                Opacity = WatermarkOpacity,
                Position = WatermarkPosition
            },
            Metadata = new MetadataOptions
            {
                InjectPhotographer = InjectMetadata,
                PhotographerName = PhotographerName.Trim(),
                CopyrightNotice = CopyrightNotice.Trim()
            }
        };

        RequestClose?.Invoke(request);
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose?.Invoke(null);
    }
}
