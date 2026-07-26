using System.Globalization;
using System.Windows.Data;
using JackAll.Core.Vfs;

namespace JackAll.App;

/// <summary>
/// Resolves the Files grid's "Source" column text via <see cref="MainViewModel.ModuleNameFor"/>, so a
/// file from a colliding archive name (e.g. two DLCs each shipping their own "menus.fat") shows
/// "dlc1/menus" instead of the bare, ambiguous "menus". A <see cref="System.Windows.Controls.DataGridColumn"/>
/// isn't part of the visual tree, so its binding can't reach the window's DataContext via
/// RelativeSource the way an ordinary cell template could - the two values this multi-binding
/// combines instead are the row itself (<c>{Binding}</c>) and the DataGrid's own DataContext
/// (<c>{Binding DataContext, RelativeSource={RelativeSource AncestorType=DataGrid}}</c>).
/// </summary>
public sealed class ModuleNameMultiConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
        => values is [VfsFile file, MainViewModel viewModel, ..] ? viewModel.ModuleNameFor(file) : string.Empty;

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
