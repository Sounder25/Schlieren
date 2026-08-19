using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Schlieren.UI.ViewModels;

// Bool to background color (for file tabs)
public class BoolToColorConverter : IValueConverter
{
    public static readonly BoolToColorConverter Instance = new();
    
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isSelected && isSelected)
            return new SolidColorBrush(Color.Parse(
                Services.SkinService.Current.ActiveLineBg));
        return new SolidColorBrush(Color.Parse(
            Services.SkinService.Current.PanelDeep));
    }
    
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

// Bool to foreground color
public class BoolToFGConverter : IValueConverter
{
    public static readonly BoolToFGConverter Instance = new();
    
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isSelected && isSelected)
            return new SolidColorBrush(Color.Parse(
                Services.SkinService.Current.AccentAlt));
        return new SolidColorBrush(Color.Parse(
            Services.SkinService.Current.TextMuted));
    }
    
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

// Bool to font weight
public class BoolToFontWeightConverter : IValueConverter
{
    public static readonly BoolToFontWeightConverter Instance = new();
    
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isSelected && isSelected)
            return FontWeight.Bold;
        return FontWeight.Normal;
    }
    
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

// Bool to Run/Loading button label
public class BoolToRunLabelConverter : IValueConverter
{
    public static readonly BoolToRunLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "⏳ RUNNING..." : "▶ RUN";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

// Bool to Play/Pause button label
public class BoolToPlayLabelConverter : IValueConverter
{
    public static readonly BoolToPlayLabelConverter Instance = new();
    
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "⏸ PAUSE" : "▶ PLAY";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

// Progress ratio × container width → pixel width for the progress bar fill
public class ProgressWidthConverter : IMultiValueConverter
{
    public static readonly ProgressWidthConverter Instance = new();

    public object Convert(System.Collections.Generic.IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count >= 2
            && values[0] is double ratio
            && values[1] is double containerWidth
            && containerWidth > 0)
        {
            return Math.Max(0, Math.Min(containerWidth, ratio * containerWidth));
        }
        return 0.0;
    }
}
