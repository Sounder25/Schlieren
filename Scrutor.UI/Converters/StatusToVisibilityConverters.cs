using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Scrutor.UI.ViewModels;

namespace Scrutor.UI.Converters;

public class StatusToStartVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is NodeStatus status)
        {
            return (status == NodeStatus.Inactive || status == NodeStatus.Error) ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => DependencyProperty.UnsetValue;
}

public class StatusToStopVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is NodeStatus status)
        {
            return (status == NodeStatus.Active) ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => DependencyProperty.UnsetValue;
}
