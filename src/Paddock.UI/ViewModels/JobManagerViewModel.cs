using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paddock.Core.DTOs;
using Paddock.Core.Enums;
using Paddock.Core.Interfaces;

namespace Paddock.UI.ViewModels;

public partial class JobManagerViewModel : ViewModelBase
{
    private readonly IIngestionPipelineService _pipelineService;

    [ObservableProperty]
    private bool _isDrawerOpen = false;

    [ObservableProperty]
    private int _activeJobsCount;

    public bool HasActiveJobs => ActiveJobsCount > 0;

    [ObservableProperty]
    private string _overallSummary = "Nessuna operazione in corso";

    public ObservableCollection<IngestionJobItemViewModel> Jobs { get; } = new();

    public JobManagerViewModel(IIngestionPipelineService pipelineService)
    {
        _pipelineService = pipelineService;
        _pipelineService.JobProgressUpdated += OnJobProgressUpdated;
        _pipelineService.JobCompleted += OnJobCompleted;
    }

    private void OnJobProgressUpdated(object? sender, IngestionProgressReport report)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var existing = Jobs.FirstOrDefault(j => j.JobId == report.JobId);
            if (existing == null)
            {
                existing = new IngestionJobItemViewModel(report.JobId, _pipelineService);
                Jobs.Insert(0, existing);
                IsDrawerOpen = true; // Auto-apri cassetto all'inizio di un nuovo job
            }

            existing.UpdateFromReport(report);
            RefreshSummary();
        });
    }

    private void OnJobCompleted(object? sender, IngestionProgressReport report)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var existing = Jobs.FirstOrDefault(j => j.JobId == report.JobId);
            if (existing != null)
            {
                existing.UpdateFromReport(report);
            }
            RefreshSummary();
        });
    }

    private void RefreshSummary()
    {
        ActiveJobsCount = Jobs.Count(j => j.Status == IngestionStatus.Running || j.Status == IngestionStatus.Queued);
        OnPropertyChanged(nameof(HasActiveJobs));
        if (ActiveJobsCount > 0)
        {
            var running = Jobs.Where(j => j.Status == IngestionStatus.Running).ToList();
            var totalSpeed = running.Sum(j => j.SpeedMegaBytesPerSecond);
            OverallSummary = $"{ActiveJobsCount} job attivi — {totalSpeed:0.0} MB/s totali";
        }
        else
        {
            OverallSummary = Jobs.Count > 0 
                ? $"{Jobs.Count} job completati/inattivi" 
                : "Nessuna operazione in corso";
        }
    }

    [RelayCommand]
    private void ToggleDrawer()
    {
        IsDrawerOpen = !IsDrawerOpen;
    }

    [RelayCommand]
    private void CancelAll()
    {
        _pipelineService.CancelAllJobs();
    }

    [RelayCommand]
    private void ClearFinished()
    {
        var finished = Jobs.Where(j => j.Status != IngestionStatus.Running && j.Status != IngestionStatus.Queued).ToList();
        foreach (var job in finished)
        {
            Jobs.Remove(job);
        }
        RefreshSummary();
    }
}
