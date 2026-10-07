namespace Paddock.Core.Models;

public enum TransitionEngineType
{
    Gpu,
    Cpu,
    HybridCpuGpu
}

public class TransitionEffectInfo
{
    public string Id { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;
    public string Descrizione { get; set; } = string.Empty;
    public TransitionEngineType Engine { get; set; } = TransitionEngineType.Gpu;
    public string BadgeText => Engine switch
    {
        TransitionEngineType.Gpu => "GPU",
        TransitionEngineType.Cpu => "CPU",
        TransitionEngineType.HybridCpuGpu => "CPU / GPU",
        _ => "GPU"
    };
    public string BadgeColor => Engine switch
    {
        TransitionEngineType.Gpu => "#2ECC71",
        TransitionEngineType.Cpu => "#FF8C32",
        TransitionEngineType.HybridCpuGpu => "#3498DB",
        _ => "#2ECC71"
    };
    public bool IsSelected { get; set; } = true;
}

public class DisplayScreenInfo
{
    public int Index { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public int BoundsX { get; set; }
    public int BoundsY { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool IsPrimary { get; set; }

    public string Description => $"{DisplayName} — {Width}x{Height}{(IsPrimary ? " (Principale)" : " (Esterno)")}";
}

public class SlideshowConfig
{
    public Guid EventoId { get; set; }
    public string EventoNome { get; set; } = string.Empty;
    public List<Guid> SelectedAtletiIds { get; set; } = new();
    public bool IncludeJpegPng { get; set; } = true;
    public bool IncludeRaw { get; set; } = false;
    public int TargetScreenIndex { get; set; }
    public DisplayScreenInfo? TargetScreen { get; set; }
    public int DurationSeconds { get; set; } = 5;
    public bool IsRandomOrder { get; set; } = true;
    public List<string> SelectedTransitionIds { get; set; } = new();
}

public class AtletaSelectionItem
{
    public Atleta Atleta { get; set; }
    public bool IsSelected { get; set; } = true;

    public Guid Id => Atleta.Id;
    public string NumeroPettorale => Atleta.NumeroPettorale;
    public string NomeCompleto => Atleta.NomeCompleto;
    public string Categoria => Atleta.Categoria ?? string.Empty;
    public string DisplayText => string.IsNullOrWhiteSpace(NumeroPettorale)
        ? NomeCompleto
        : $"#{NumeroPettorale} — {NomeCompleto}";

    public AtletaSelectionItem(Atleta atleta, bool isSelected = true)
    {
        Atleta = atleta;
        IsSelected = isSelected;
    }
}

