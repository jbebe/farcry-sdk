using JackAll.Core.Vfs;
using System.Globalization;
using System.Windows.Data;

namespace JackAll.App;

/// <summary>A Files-tab row's label, empty for anything unlabelled.</summary>
public sealed class LabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        => value is VfsFile file ? MainViewModel.LabelText(file) ?? string.Empty : string.Empty;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
