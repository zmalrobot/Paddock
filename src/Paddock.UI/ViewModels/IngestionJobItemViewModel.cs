using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paddock.Core.DTOs;
using Paddock.Core.Enums;
using Paddock.Core.Interfaces;

namespace Paddock.UI.ViewModels;

public partial class IngestionJobItemViewModel : ViewModelBase
{
    private readonly IIngestionPipelineService _pipelineService;

    public Guid JobId { get; }

    [ObservableProperty]
    private string _sourceName = string.Empty;

    [ObservableProperty]
    private string _targetDescription = string.Empty;

    [ObservableProperty]
    private IngestionStatus _status = IngestionStatus.Queued;

    [ObservableProperty]
    private int _totalFiles;

    [ObservableProperty]
    private int _processedFiles;

    [ObservableProperty]
    private double _progressPercentage;

    [ObservableProperty]
    private double _speedMegaBytesPerSecond;

    [ObservableProperty]
    private string _speedDisplay = "0.0 MB/s";

    [ObservableProperty]
    private string _etaDisplay = "--:--";

    [ObservableProperty]
    private string? _currentProcessingFile;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _hasWarnings;

    [ObservableProperty]
    private string _warningSummary = string.Empty;

    public bool CanCancel => Status == IngestionStatus.Running || Status == IngestionStatus.Queued;

    public IngestionJobItemViewModel(Guid jobId, IIngestionPipelineService pipelineService)
    {
        JobId = jobId;
        _pipelineService = pipelineService;
    }

    public void UpdateFromReport(IngestionProgressReport report)
    {
        SourceName = report.SourceName;
        TargetDescription = report.TargetDescription;
        Status = report.Status;
        TotalFiles = report.TotalFiles;
        ProcessedFiles = report.ProcessedFiles;
        ProgressPercentage = report.ProgressPercentage;
        SpeedMegaBytesPerSecond = report.SpeedMegaBytesPerSecond;
        SpeedDisplay = $"{report.SpeedMegaBytesPerSecond:0.0} MB/s";
        CurrentProcessingFile = report.CurrentProcessingFile;
        StatusMessage = report.StatusMessage;

        if (report.EstimatedTimeRemaining.HasValue && report.EstimatedTimeRemaining.Value.TotalSeconds > 0)
        {
            var eta = report.EstimatedTimeRemaining.Value;
            EtaDisplay = eta.TotalHours >= 1 
                ? $"{(int)eta.TotalHours}h {eta.Minutes}m" 
                : $"{eta.Minutes}m {eta.Seconds}s";
        }
        else
        {
            EtaDisplay = Status == IngestionStatus.Completed ? "Completato" : "--:--";
        }

        if (report.Warnings.Count > 0)
        {
            HasWarnings = true;
            WarningSummary = string.Join("; ", report.Warnings.Take(3));
        }

        OnPropertyChanged(nameof(CanCancel));
    }

    [RelayCommand]
    private void Cancel()
    {
        _pipelineService.CancelJob(JobId);
        Status = IngestionStatus.Cancelled;
        StatusMessage = "Annullamento in corso...";
        OnPropertyChanged(nameof(CanCancel));
    }
}

