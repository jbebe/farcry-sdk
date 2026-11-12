using System.Runtime.InteropServices;
using System.Windows;

namespace JackAll.App.MapEditor;

/// <summary>
/// The viewport's OLE drop target. A drag over a child window never reaches WPF's own, so the
/// Library's archetypes and prefabs land here, in the same WPF data object the Library put them in.
/// </summary>
[ComVisible(true)]
internal sealed class ViewportDropTarget(D3DViewport viewport) : ViewportDropTarget.IOleDropTarget
{
    [ComImport, Guid("00000122-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IOleDropTarget
    {
        [PreserveSig]
        int DragEnter([In, MarshalAs(UnmanagedType.Interface)] object data, int keyState, long point, ref int effect);

        [PreserveSig]
        int DragOver(int keyState, long point, ref int effect);

        [PreserveSig]
        int DragLeave();

        [PreserveSig]
        int Drop([In, MarshalAs(UnmanagedType.Interface)] object data, int keyState, long point, ref int effect);
    }

    private IDataObject? _data;

    /// <summary>What the last hover said the drop would do; the drop lands where that hover was.</summary>
    private int _effect;

    public int DragEnter(object data, int keyState, long point, ref int effect)
    {
        // In process, OLE hands back the Library's own data object; from elsewhere WPF wraps it.
        _data = data as IDataObject ?? new DataObject(data);
        return DragOver(keyState, point, ref effect);
    }

    public int DragOver(int keyState, long point, ref int effect)
    {
        effect = _effect = _data is null ? 0 : (int)viewport.OnDragHover(_data, At(point));
        return 0;
    }

    public int DragLeave()
    {
        _data = null;
        return 0;
    }

    public int Drop(object data, int keyState, long point, ref int effect)
    {
        effect = _data is null ? 0 : _effect;
        if (effect != 0)
        {
            viewport.OnDragDrop(_data!, At(point));
        }
        _data = null;
        return 0;
    }

    /// <summary>A POINTL by value: x in the low half, y in the high, in screen pixels.</summary>
    private Point At(long point) => viewport.ScreenToDips((int)(point & 0xFFFFFFFF), (int)(point >> 32));
}
