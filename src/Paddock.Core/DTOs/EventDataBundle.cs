using Paddock.Core.Models;

namespace Paddock.Core.DTOs;

public class EventDataBundle
{
    public List<Atleta> Atleti { get; set; } = new();
    public List<Disciplina> Discipline { get; set; } = new();
    public List<Foto> Foto { get; set; } = new();
    public List<AcquistoFoto> Acquisti { get; set; } = new();
    public List<PrezzoCatalogoItem> CatalogoPrezzi { get; set; } = new();
}
