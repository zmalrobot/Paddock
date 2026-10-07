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

public enum CategoriaPrezzo
{
    FotoSingola,
    PacchettoFoto,
    PacchettoGara,
    PacchettoDisciplina,
    EditingBase,
    EditingAvanzato
}

public class PrezzoCatalogoItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Nome { get; set; } = string.Empty;
    public CategoriaPrezzo Categoria { get; set; } = CategoriaPrezzo.FotoSingola;
    public decimal Prezzo { get; set; }
    public int QuantitaFotoIncluse { get; set; } = 1;
    public string? Descrizione { get; set; }

    public string CategoriaDisplay => Categoria switch
    {
        CategoriaPrezzo.FotoSingola => "Foto Singola",
        CategoriaPrezzo.PacchettoFoto => "Pacchetto Foto",
        CategoriaPrezzo.PacchettoGara => "Pacchetto Gara",
        CategoriaPrezzo.PacchettoDisciplina => "Pacchetto Disciplina",
        CategoriaPrezzo.EditingBase => "Editing Base",
        CategoriaPrezzo.EditingAvanzato => "Editing Avanzato",
        _ => Categoria.ToString()
    };
}

public class VoceAcquisto
{
    public Guid PrezzoItemId { get; set; }
    public string NomeArticolo { get; set; } = string.Empty;
    public CategoriaPrezzo Categoria { get; set; } = CategoriaPrezzo.FotoSingola;
    public decimal PrezzoUnitario { get; set; }
    public int Quantita { get; set; } = 1;
    public decimal Subtotale => PrezzoUnitario * Quantita;
}

public class AcquistoFoto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EventoId { get; set; }
    public DateTime DataAcquisto { get; set; } = DateTime.Now;

    // Atleta
    public Guid AtletaId { get; set; }
    public string NomeAtleta { get; set; } = string.Empty;
    public string NumeroPettorale { get; set; } = string.Empty;

    // Disciplina
    public Guid? DisciplinaId { get; set; }
    public string NomeDisciplina { get; set; } = string.Empty;

    // Pacchetti / Voci
    public List<VoceAcquisto> Voci { get; set; } = new();
    public int TotaleQuantita { get; set; }
    public decimal TotaleCalcolato { get; set; }
    public decimal TotalePagato { get; set; }

    // Recapiti
    public string EmailCliente { get; set; } = string.Empty;
    public string TelefonoCliente { get; set; } = string.Empty;

    // Riferimento Foto
    public bool InteraCartella { get; set; } = true;
    public List<string> FileFotoSelezionate { get; set; } = new();
    public string? CartellaPathRiferimento { get; set; }

    // Note e Stato
    public string? Note { get; set; }
    public string Stato { get; set; } = "Completato";

    public string RiferimentoFotoDisplay => InteraCartella
        ? "Intera Cartella Atleta"
        : $"{FileFotoSelezionate.Count} foto selezionate";

    public string VociSommarioDisplay => Voci != null && Voci.Count > 0
        ? string.Join(", ", Voci.Select(v => $"{v.NomeArticolo} x{v.Quantita}"))
        : "Nessun pacchetto";
}

