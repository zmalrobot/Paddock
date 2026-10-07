using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Paddock.Core.Enums;

namespace Paddock.UI.Converters;

public class ByteToSizeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not long bytes && value is not int && value is not double)
            return "0 B";

        var byteCount = System.Convert.ToDouble(value);
        string[] suf = { "B", "KB", "MB", "GB", "TB" };
        if (byteCount <= 0) return "0 B";

        int place = System.Convert.ToInt32(Math.Floor(Math.Log(byteCount, 1024)));
        place = Math.Clamp(place, 0, suf.Length - 1);
        double num = Math.Round(byteCount / Math.Pow(1024, place), 1);
        return $"{num.ToString("0.#", CultureInfo.InvariantCulture)} {suf[place]}";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class SpeedConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double mbPerSec)
        {
            return $"{mbPerSec.ToString("0.0", CultureInfo.InvariantCulture)} MB/s";
        }
        return "0.0 MB/s";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class StatusToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is IngestionStatus status)
        {
            return status switch
            {
                IngestionStatus.Queued => Color.Parse("#FFB703"),      // Ambra chiaro
                IngestionStatus.Running => Color.Parse("#FF8C32"),     // Studio Amber
                IngestionStatus.Completed => Color.Parse("#2ECC71"),   // Emerald Green
                IngestionStatus.Cancelled => Color.Parse("#9EA3AE"),   // Slate Gray
                IngestionStatus.Failed => Color.Parse("#E74C3C"),      // Carmine Red
                _ => Color.Parse("#9EA3AE")
            };
        }
        return Color.Parse("#9EA3AE");
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class FormatToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var fmt = value?.ToString()?.ToUpperInvariant() ?? "";
        if (fmt == "RAW")
        {
            return new SolidColorBrush(Color.Parse("#00B4D8")); // Viewfinder Cyan
        }
        return new SolidColorBrush(Color.Parse("#4A5568")); // Slate Muted
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

