using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static JackAll.App.MapEditor.Gpu.GpuDevice;

namespace JackAll.App.MapEditor.Gpu;

/// <summary>Immutable textures from data on the CPU, in texels for plain formats and in 4x4 blocks
/// for the block-compressed ones, handed back as the view: a view holds its texture.</summary>
public static class Upload
{
    /// <summary>One level from tightly packed texels.</summary>
    public static ID3D11ShaderResourceView Texture(Array data, Format format, int width, int height)
        => Immutable(format, width, height, [data]);

    /// <summary>A texture from all its subresources, slice by slice and each slice's levels in order.</summary>
    public static ID3D11ShaderResourceView Immutable(
        Format format, int width, int height, IReadOnlyList<Array> subresources, int mipLevels = 1, int arraySize = 1)
    {
        var pins = new GCHandle[subresources.Count];
        var initial = new SubresourceData[subresources.Count];
        try
        {
            for (int i = 0; i < subresources.Count; i++)
            {
                pins[i] = GCHandle.Alloc(subresources[i], GCHandleType.Pinned);
                initial[i] = new SubresourceData(pins[i].AddrOfPinnedObject(),
                    (uint)RowPitch(format, Math.Max(1, width >> (i % mipLevels))), 0);
            }
            var description = new Texture2DDescription(format, (uint)width, (uint)height, (uint)arraySize,
                (uint)mipLevels, BindFlags.ShaderResource, ResourceUsage.Immutable);
            using ID3D11Texture2D texture = Device.CreateTexture2D(description, initial);
            return Device.CreateShaderResourceView(texture);
        }
        finally
        {
            foreach (GCHandle pin in pins)
            {
                if (pin.IsAllocated)
                {
                    pin.Free();
                }
            }
        }
    }

    /// <summary>Bytes in one row of blocks, or of texels, of a region this wide.</summary>
    public static int RowPitch(Format format, int width) => BlockBytes(format) is int block and > 0
        ? Math.Max(1, (width + 3) / 4) * block
        : (int)(width * format.GetBitsPerPixel() / 8);

    /// <summary>8 for BC1, 16 for BC2 and BC3, 0 for a format that is not block-compressed.</summary>
    public static int BlockBytes(Format format) => format switch
    {
        Format.BC1_UNorm or Format.BC1_UNorm_SRgb => 8,
        Format.BC2_UNorm or Format.BC2_UNorm_SRgb or Format.BC3_UNorm or Format.BC3_UNorm_SRgb => 16,
        _ => 0,
    };

    /// <summary>
    /// A texture assembled piece by piece on the CPU and made in one call. Writing the pieces into a
    /// live texture instead leaves the driver holding an upload pool the size of the whole burst.
    /// </summary>
    public sealed class Staged(Format format, int width, int height, int mipLevels = 1, int arraySize = 1)
    {
        private readonly byte[][] _levels = [.. Enumerable.Range(0, mipLevels * arraySize)
            .Select(i => new byte[RowPitch(format, Math.Max(1, width >> (i % mipLevels))) * Rows(format, height, i % mipLevels)])];

        /// <summary>Rows of texels, or of blocks, in one level.</summary>
        private static int Rows(Format format, int height, int level)
        {
            int side = Math.Max(1, height >> level);
            return BlockBytes(format) > 0 ? (side + 3) / 4 : side;
        }

        /// <summary>Copies a tightly packed rectangle into one level of one slice. A block-compressed
        /// rectangle starts on a block.</summary>
        public void Write(int level, int slice, int x, int y, int regionWidth, int regionHeight, byte[] data)
        {
            byte[] into = _levels[level + slice * mipLevels];
            int pitch = RowPitch(format, Math.Max(1, width >> level));
            int rowBytes = RowPitch(format, regionWidth);
            bool blocks = BlockBytes(format) > 0;
            int rows = blocks ? (regionHeight + 3) / 4 : regionHeight;
            int column = blocks ? x / 4 * BlockBytes(format) : x * (int)format.GetBitsPerPixel() / 8;
            int start = (blocks ? y / 4 : y) * pitch + column;
            for (int row = 0; row < rows; row++)
            {
                Buffer.BlockCopy(data, row * rowBytes, into, start + row * pitch, rowBytes);
            }
        }

        public ID3D11ShaderResourceView Create() => Immutable(format, width, height, _levels, mipLevels, arraySize);
    }
}
