using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paddock.Core.Interfaces;
using Paddock.Core.Models;

namespace Paddock.UI.ViewModels;

public partial class SlideshowConfigViewModel : ViewModelBase
{
    private readonly IExcelRepository _excelRepo;

    public ObservableCollection<Evento> Eventi { get; } = new();
    public ObservableCollection<AtletaSelectionItem> Atleti { get; } = new();
    public ObservableCollection<DisplayScreenInfo> Screens { get; } = new();
    public ObservableCollection<TransitionEffectInfo> Transitions { get; } = new();
    public List<int> DurationOptions { get; } = new() { 2, 3, 4, 5, 6, 8, 10, 15, 20, 30 };

    [ObservableProperty]
    private Evento? _selectedEvento;

    [ObservableProperty]
    private DisplayScreenInfo? _selectedScreen;

    [ObservableProperty]
    private bool _includeJpegPng = true;

    [ObservableProperty]
    private bool _includeRaw = false;

    [ObservableProperty]
    private int _durationSeconds = 5;

    [ObservableProperty]
    private bool _isRandomOrder = true;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isBusy;

    public event Action? RequestClose;
    public event Action<SlideshowConfig>? RequestStartSlideshow;

    public SlideshowConfigViewModel(
        IExcelRepository excelRepo,
        IEnumerable<Evento> eventi,
        IEnumerable<DisplayScreenInfo> screens,
        Evento? initialSelectedEvento = null)
    {
        _excelRepo = excelRepo;

        foreach (var ev in eventi)
        {
            Eventi.Add(ev);
        }

        foreach (var s in screens)
        {
            Screens.Add(s);
        }

        // Seleziona preferibilmente il secondo monitor (esterno), altrimenti il primario
        SelectedScreen = Screens.FirstOrDefault(s => !s.IsPrimary) ?? Screens.FirstOrDefault();

        InitializeTransitions();

        SelectedEvento = initialSelectedEvento ?? Eventi.FirstOrDefault();
    }

    private void InitializeTransitions()
    {
        Transitions.Clear();
        Transitions.Add(new TransitionEffectInfo
        {
            Id = "crossfade",
            Nome = "Dissolvenza Incrociata (Crossfade)",
            Descrizione = "Transizione classica morbida ed elegante tra l'opacità delle due immagini",
            Engine = TransitionEngineType.Gpu,
            IsSelected = true
        });
        Transitions.Add(new TransitionEffectInfo
        {
            Id = "slide_push",
            Nome = "Scorrimento Dinamico (Slide Push)",
            Descrizione = "La nuova foto entra lateralmente ad alta fluidità spingendo la precedente",
            Engine = TransitionEngineType.Gpu,
            IsSelected = true
        });
        Transitions.Add(new TransitionEffectInfo
        {
            Id = "ken_burns",
            Nome = "Ken Burns Cinematic (Zoom & Pan)",
            Descrizione = "Movimento lento continuo con zoom panoramico per esaltare l'azione sportiva",
            Engine = TransitionEngineType.Gpu,
            IsSelected = true
        });
        Transitions.Add(new TransitionEffectInfo
        {
            Id = "zoom_fade",
            Nome = "Zoom Esplosivo Sfumato (Zoom In Fade)",
            Descrizione = "La nuova foto emerge dal centro ingrandendosi progressivamente con dissolvenza",
            Engine = TransitionEngineType.Gpu,
            IsSelected = true
        });
        Transitions.Add(new TransitionEffectInfo
        {
            Id = "strobe_flash",
            Nome = "Flash Sportivo Paddock (Studio Strobe Flash)",
            Descrizione = "Lampo di luce bianco ultra-rapido che simula lo scatto dei flash a bordo pista",
            Engine = TransitionEngineType.Gpu,
            IsSelected = true
        });
        Transitions.Add(new TransitionEffectInfo
        {
            Id = "wipe_reveal",
            Nome = "Sfumatura a Tendina (Wipe Reveal)",
            Descrizione = "Svelamento lineare progressivo da sinistra a destra con bordo morbido",
            Engine = TransitionEngineType.HybridCpuGpu,
            IsSelected = true
        });
        Transitions.Add(new TransitionEffectInfo
        {
            Id = "iris_circle",
            Nome = "Espansione Circolare a Iride (Iris Circle)",
            Descrizione = "Apertura circolare geometrica ad iride dal centro che rivela la nuova inquadratura",
            Engine = TransitionEngineType.Gpu,
            IsSelected = true
        });
        Transitions.Add(new TransitionEffectInfo
        {
            Id = "action_glitch",
            Nome = "Glitch Digitale Azione (Action Glitch)",
            Descrizione = "Scatto visivo ad alto impatto con sfasamento cromatico RGB per motorsport",
            Engine = TransitionEngineType.Gpu,
            IsSelected = true
        });
        Transitions.Add(new TransitionEffectInfo
        {
            Id = "matrix_dissolve",
            Nome = "Mosaico a Blocchi (Pixel Matrix Dissolve)",
            Descrizione = "Dissolvenza dinamica a tessere e blocchi che si ricompongono progressivamente",
            Engine = TransitionEngineType.Cpu,
            IsSelected = true
        });
        Transitions.Add(new TransitionEffectInfo
        {
            Id = "motion_blur",
            Nome = "Sfocatura Direzionale (Motion Blur Pop)",
            Descrizione = "Scatto dinamico ad alta velocità con sfocatura rapida lungo la direzione",
            Engine = TransitionEngineType.HybridCpuGpu,
            IsSelected = true
        });
    }

    async partial void OnSelectedEventoChanged(Evento? value)
    {
        ErrorMessage = null;
        if (value == null)
        {
            Atleti.Clear();
            return;
        }

        await LoadAtletiForSelectedEventoAsync(value.Id);
    }

    public async Task LoadAtletiForSelectedEventoAsync(Guid eventoId)
    {
        try
        {
            IsBusy = true;
            var atleti = await _excelRepo.GetAtletiByEventoAsync(eventoId);
            Atleti.Clear();
            foreach (var a in atleti.OrderBy(a => a.NumeroPettorale).ThenBy(a => a.Cognome))
            {
                Atleti.Add(new AtletaSelectionItem(a, isSelected: true));
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Errore nel caricamento dei partecipanti: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void SelectAllAtleti()
    {
        foreach (var item in Atleti)
        {
            item.IsSelected = true;
        }
        // Notifica binding UI
        OnPropertyChanged(nameof(Atleti));
    }

    [RelayCommand]
    private void DeselectAllAtleti()
    {
        foreach (var item in Atleti)
        {
            item.IsSelected = false;
        }
        // Notifica binding UI
        OnPropertyChanged(nameof(Atleti));
    }

    [RelayCommand]
    private void SelectAllTransitions()
    {
        foreach (var t in Transitions)
        {
            t.IsSelected = true;
        }
        OnPropertyChanged(nameof(Transitions));
    }

    [RelayCommand]
    private void DeselectAllTransitions()
    {
        foreach (var t in Transitions)
        {
            t.IsSelected = false;
        }
        OnPropertyChanged(nameof(Transitions));
    }

    [RelayCommand]
    public async Task StartSlideshowAsync()
    {
        ErrorMessage = null;

        if (SelectedEvento == null)
        {
            ErrorMessage = "Selezionare un evento sportivo per la presentazione.";
            return;
        }

        var selectedAtletiIds = Atleti.Where(a => a.IsSelected).Select(a => a.Id).ToList();
        if (selectedAtletiIds.Count == 0)
        {
            ErrorMessage = "Selezionare almeno un partecipante/atleta per lo slideshow.";
            return;
        }

        if (!IncludeJpegPng && !IncludeRaw)
        {
            ErrorMessage = "Selezionare almeno un formato foto ('Usa Jpeg / PNG' o 'Usa RAW').";
            return;
        }

        var selectedTransitions = Transitions.Where(t => t.IsSelected).Select(t => t.Id).ToList();
        if (selectedTransitions.Count == 0)
        {
            ErrorMessage = "Selezionare almeno un effetto di transizione dalla tabella.";
            return;
        }

        if (SelectedScreen == null && Screens.Count > 0)
        {
            SelectedScreen = Screens[0];
        }

        // Verifica la presenza di foto per i criteri scelti
        try
        {
            IsBusy = true;
            var allFoto = await _excelRepo.GetFotoByEventoAsync(SelectedEvento.Id);
            var matchingFoto = allFoto.Where(f =>
                selectedAtletiIds.Contains(f.AtletaId) &&
                ((IncludeJpegPng && !f.IsRaw) || (IncludeRaw && f.IsRaw))
            ).ToList();

            if (matchingFoto.Count == 0)
            {
                ErrorMessage = "Nessuna foto trovata per gli atleti e i formati selezionati in questo evento.";
                return;
            }

            var config = new SlideshowConfig
            {
                EventoId = SelectedEvento.Id,
                EventoNome = SelectedEvento.NomeEvento,
                SelectedAtletiIds = selectedAtletiIds,
                IncludeJpegPng = IncludeJpegPng,
                IncludeRaw = IncludeRaw,
                TargetScreenIndex = SelectedScreen?.Index ?? 0,
                TargetScreen = SelectedScreen,
                DurationSeconds = Math.Max(1, DurationSeconds),
                IsRandomOrder = IsRandomOrder,
                SelectedTransitionIds = selectedTransitions
            };

            RequestStartSlideshow?.Invoke(config);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Errore durante la verifica dei file: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Close()
    {
        RequestClose?.Invoke();
    }
}

