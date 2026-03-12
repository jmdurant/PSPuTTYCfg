using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace PuTTYProfileManager.Avalonia.Views;

public class ExistsToColorConverter : IValueConverter
{
    public static readonly ExistsToColorConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool exists)
            return exists
                ? new SolidColorBrush(Color.Parse("#98C379"))
                : new SolidColorBrush(Color.Parse("#E06C75"));

        return new SolidColorBrush(Color.Parse("#8F98A0"));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class ExistsToTextConverter : IValueConverter
{
    public static readonly ExistsToTextConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool exists)
            return exists ? "Found" : "Missing";
        return "?";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
