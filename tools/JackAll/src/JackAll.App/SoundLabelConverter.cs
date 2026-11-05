using JackAll.Core.Vfs;
using System.Globalization;
using System.Windows.Data;

namespace JackAll.App;

/// <summary>A Files-tab row's sound label, empty for anything unlabelled.</summary>
public sealed class SoundLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        => value is VfsFile file ? MainViewModel.SoundLabelText(file) ?? string.Empty : string.Empty;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
