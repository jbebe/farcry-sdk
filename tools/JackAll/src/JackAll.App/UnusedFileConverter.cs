using JackAll.Core.Vfs;
using System.Globalization;
using System.Windows.Data;

namespace JackAll.App;

/// <summary>
/// Whether a Files-tab row is a base-game file the engine can never open, so the grid can
/// de-emphasise it even while the "Hide unused game files" filter is off.
/// </summary>
public sealed class UnusedFileConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        => value is VfsFile file && MainViewModel.IsUnusedFile(file);

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
