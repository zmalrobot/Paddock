namespace Paddock.Core.Models;

public class Evento
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string NomeEvento { get; set; } = string.Empty;
    public DateTime DataInizio { get; set; } = DateTime.Today;
    public DateTime DataFine { get; set; } = DateTime.Today;
    public string Luogo { get; set; } = string.Empty;
    public string CartellaDestinazioneRoot { get; set; } = string.Empty;
    public string? Note { get; set; }

    // Statistiche calcolate / telemetria
    public int TotaleAtleti { get; set; }
    public int TotaleDiscipline { get; set; }
    public int TotaleFoto { get; set; }
    public long TotaleByteOccupati { get; set; }

    public string DateRangeDisplay => DataInizio.Date == DataFine.Date
        ? DataInizio.ToString("dd/MM/yyyy")
        : $"{DataInizio:dd/MM/yyyy} - {DataFine:dd/MM/yyyy}";
}

public class Disciplina
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EventoId { get; set; }
    public string NomeDisciplina { get; set; } = string.Empty;
    public string? Descrizione { get; set; }
}

public class Atleta
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EventoId { get; set; }
    public string NumeroPettorale { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;
    public string Cognome { get; set; } = string.Empty;
    public string? Categoria { get; set; }
    public string? Note { get; set; }

    public string NomeCompleto => $"{Cognome} {Nome}".Trim();
    public string DisplayPettoraleNome => string.IsNullOrWhiteSpace(NumeroPettorale) 
        ? NomeCompleto 
        : $"#{NumeroPettorale} — {NomeCompleto}";

    /// <summary>
    /// Ritorna una stringa normalizzata per il nome della directory su filesystem (es. "Pettorale_Cognome_Nome")
    /// </summary>
    public string NomeCartellaSanitizzato
    {
        get
        {
            var raw = string.IsNullOrWhiteSpace(NumeroPettorale)
                ? $"{Cognome}_{Nome}"
                : $"{NumeroPettorale}_{Cognome}_{Nome}";

            var invalid = Path.GetInvalidFileNameChars();
            var sanitized = string.Concat(raw.Select(c => invalid.Contains(c) ? '_' : c)).Trim();
            return string.IsNullOrWhiteSpace(sanitized) ? "Atleta_Sconosciuto" : sanitized;
        }
    }
}

public class Foto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EventoId { get; set; }
    public Guid AtletaId { get; set; }
    public Guid DisciplinaId { get; set; }
    public string NomeFileOriginale { get; set; } = string.Empty;
    public string PathRelativo { get; set; } = string.Empty;
    public string Formato { get; set; } = "JPEG"; // "JPEG" o "RAW"
    public DateTime? DataScatto { get; set; }
    public string? Fotografo { get; set; }
    public bool WatermarkApplicato { get; set; }
    public long DimensioneByte { get; set; }
    public string? HashMd5 { get; set; }

    // Helper per determinare se è RAW
    public bool IsRaw => Formato.Equals("RAW", StringComparison.OrdinalIgnoreCase);
}

