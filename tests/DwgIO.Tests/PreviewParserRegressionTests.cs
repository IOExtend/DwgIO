using System.Buffers.Binary;
using DwgIO.Internal.Container;
using DwgIO.Internal.Parsing;
using DwgIO.Model;
using Xunit;

namespace DwgIO.Tests;

public class PreviewParserRegressionTests
{
    private static byte[] Preview(byte code, uint start, byte[] image)
    {
        var data = new byte[64 + image.Length + 16];
        Convert.FromHexString("1F256D07D43628289D57CA3F9D44102B").CopyTo(data, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16), (uint)data.Length);
        data[20] = 1;
        data[21] = code;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(22), start);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(26), (uint)image.Length);
        image.CopyTo(data, 64);
        Convert.FromHexString("E0DA92F82BC9D7D762A835C062BBEFD4").CopyTo(data, 64 + image.Length);
        return data;
    }

    private static byte[] BitfieldsDib()
    {
        var dib = new byte[56];
        BinaryPrimitives.WriteUInt32LittleEndian(dib, 40);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4), 1);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14), 16);
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(16), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(20), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(40), 0xF800);
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(44), 0x07E0);
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(48), 0x001F);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(52), 0xF800);
        return dib;
    }

    [Fact]
    public void Wmf_DirectoryEntry_PreservesImage()
    {
        byte[] wmf = Convert.FromHexString("0100090000030C0000000000030000000000030000000000");
        var preview = PreviewParser.Parse(Preview(3, 64, wmf));
        Assert.Equal(DwgPreviewImageKind.Wmf, preview.ImageKind);
        Assert.Equal(wmf, preview.ImageBytes);
    }

    [Fact]
    public void BitfieldsDib_OffsetSkipsColorMasks()
    {
        var preview = PreviewParser.Parse(Preview(2, 64, BitfieldsDib()));
        Assert.Equal(DwgPreviewImageKind.Bmp, preview.ImageKind);
        Assert.Equal(66u, BinaryPrimitives.ReadUInt32LittleEndian(preview.ImageBytes.AsSpan(10)));
    }

    [Fact]
    public void AlphaBitfields_FourMasks_AreBeforePixels()
    {
        var dib = new byte[60];
        BinaryPrimitives.WriteUInt32LittleEndian(dib, 40);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4), 1);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14), 32);
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(16), 6);
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(20), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(40), 0x00FF0000);
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(44), 0x0000FF00);
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(48), 0x000000FF);
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(52), 0xFF000000);
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(56), 0xFFFF0000);
        var preview = PreviewParser.Parse(Preview(2, 64, dib));
        Assert.Equal(DwgPreviewImageKind.Bmp, preview.ImageKind);
        Assert.Equal(70u, BinaryPrimitives.ReadUInt32LittleEndian(preview.ImageBytes.AsSpan(10)));
    }

    [Fact]
    public void AbsoluteDib_SpanningTwoPages_PreservesImage()
    {
        var data = Preview(2, 1064, BitfieldsDib());
        SectionPageLayout[] pages = [new(0, 92, 1000, true), new(92, data.Length - 92, 1124, true)];
        var preview = PreviewParser.Parse(data, pages);
        Assert.Equal(DwgPreviewImageKind.Bmp, preview.ImageKind);
        Assert.True(preview.HasImage);
    }
}
