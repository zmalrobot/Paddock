using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paddock.Core.Enums;
using Paddock.Core.Models;

namespace Paddock.UI.ViewModels;

public partial class DeleteEventDialogViewModel : ViewModelBase
{
    public Evento Evento { get; }

    [ObservableProperty]
    private bool _confirmFilesPurgeChecked;

    [ObservableProperty]
    private string? _errorMessage;

    public event Action<DeleteMode>? RequestClose;

    public DeleteEventDialogViewModel(Evento evento)
    {
        Evento = evento;
    }

    [RelayCommand]
    private void ChooseDatabaseOnly()
    {
        RequestClose?.Invoke(DeleteMode.DatabaseOnly);
    }

    [RelayCommand]
    private void ChooseDatabaseAndFiles()
    {
        if (!ConfirmFilesPurgeChecked)
        {
            ErrorMessage = "Devi spuntare la casella di conferma per eliminare definitivamente i file fisici dal disco.";
            return;
        }

        RequestClose?.Invoke(DeleteMode.DatabaseAndFiles);
    }

    [RelayCommand]
    private void ChooseCancel()
    {
        RequestClose?.Invoke(DeleteMode.Cancel);
    }
}

