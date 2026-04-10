using System;
using System.Globalization;
using System.Windows.Data;

namespace DeathAdderManager;

public class TabVisConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int selected && parameter is string tabIdxStr && int.TryParse(tabIdxStr, out int tabIdx))
        {
            return selected == tabIdx ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        }
        return System.Windows.Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class PollingRateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int selected && parameter is string param && int.TryParse(param, out int paramValue))
        {
            return selected == paramValue;
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isChecked && isChecked && parameter is string param && int.TryParse(param, out int paramValue))
        {
            return paramValue;
        }
        return 1000;
    }
}

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && parameter is string param && bool.TryParse(param, out bool paramValue))
        {
            return b == paramValue ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        }
        return System.Windows.Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isNull = value == null;
        if (parameter != null && parameter.ToString() == "Invert") isNull = !isNull;
        return isNull ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class SelectedActionToBrushConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2) return System.Windows.Media.Brushes.White;
        bool isMatch = values[0]?.ToString() == values[1]?.ToString();
        return isMatch ? (System.Windows.Media.Brush)App.Current.Resources["AccentBrush"] : System.Windows.Media.Brushes.White;
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotImplementedException();
}
