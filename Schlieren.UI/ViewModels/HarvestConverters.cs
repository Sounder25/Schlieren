using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;

namespace Schlieren.UI.ViewModels;

/// <summary>Maps HarvestEntry.Outcome to the left-border accent brush.</summary>
public sealed class OutcomeToAccentConverter : IValueConverter
{
    public static readonly OutcomeToAccentConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return (value as string) switch
        {
            "EXECUTED_DIVERGENCE" => new SolidColorBrush(Color.Parse("#C43A52")),
            "EXECUTED_PASS"       => new SolidColorBrush(Color.Parse("#5A8A6C")),
            "CAPTURE_FAILED"
            or "EXECUTION_FAILED" => new SolidColorBrush(Color.Parse("#B85A32")),
            _                     => new SolidColorBrush(Color.Parse("#454B54"))
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Maps Outcome to badge background brush.</summary>
public sealed class OutcomeToBadgeBgConverter : IValueConverter
{
    public static readonly OutcomeToBadgeBgConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return (value as string) switch
        {
            "EXECUTED_DIVERGENCE" => new SolidColorBrush(Color.Parse("#1F0A0E")),
            "EXECUTED_PASS"       => new SolidColorBrush(Color.Parse("#0A130D")),
            "CAPTURE_FAILED"
            or "EXECUTION_FAILED" => new SolidColorBrush(Color.Parse("#1A0E08")),
            _                     => new SolidColorBrush(Color.Parse("#181A1D"))
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Maps Outcome to badge foreground brush.</summary>
public sealed class OutcomeToFgConverter : IValueConverter
{
    public static readonly OutcomeToFgConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return (value as string) switch
        {
            "EXECUTED_DIVERGENCE" => new SolidColorBrush(Color.Parse("#C43A52")),
            "EXECUTED_PASS"       => new SolidColorBrush(Color.Parse("#5A8A6C")),
            "CAPTURE_FAILED"
            or "EXECUTION_FAILED" => new SolidColorBrush(Color.Parse("#B85A32")),
            _                     => new SolidColorBrush(Color.Parse("#6A7280"))
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
