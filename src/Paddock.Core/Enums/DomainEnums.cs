namespace Paddock.Core.Enums;

public enum FormatoFoto
{
    Jpeg,
    Raw
}

public enum WatermarkPosition
{
    BottomRight,
    BottomLeft,
    TopRight,
    TopLeft,
    Center,
    Tiled
}

public enum IngestionStatus
{
    Queued,
    Running,
    Completed,
    Cancelled,
    Failed
}

public enum DeleteMode
{
    DatabaseOnly,
    DatabaseAndFiles,
    Cancel
}

