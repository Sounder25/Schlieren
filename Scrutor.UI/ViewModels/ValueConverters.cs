using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Scrutor.UI.ViewModels;

// Bool to background color (for file tabs)
public class BoolToColorConverter : IValueConverter
{
    public static readonly BoolToColorConverter Instance = new();
    
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isSelected && isSelected)
            return new SolidColorBrush(Color.Parse("#1A1A1A"));
        return new SolidColorBrush(Color.Parse("#141414"));
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
            return new SolidColorBrush(Color.Parse("#00D4AA"));
        return new SolidColorBrush(Color.Parse("#888888"));
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
