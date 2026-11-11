namespace DwgIO.Model;

/// <summary>A DWG handle: 4-bit code + 4-bit byte counter + up to 8 bytes of payload.</summary>
/// <param name="Code">Reference type code (upper 4 bits of the first byte).</param>
/// <param name="Counter">Number of payload bytes that follow.</param>
/// <param name="Value">Handle value, big-endian across the payload bytes.</param>
public sealed record DwgHandle(byte Code, byte Counter, ulong Value)
{
    /// <summary>The handle value in hex, as AutoCAD displays handles.</summary>
    public override string ToString() => $"{Value:X}";
}

/// <summary>
/// Inventory of one record in the <c>AcDb:AcDbObjects</c> stream:
/// framing and identity only; the record body is decoded separately.
/// </summary>
public sealed record DwgObjectInfo
{
    /// <summary>Offset of the record start (its size prefix) within the decompressed stream.</summary>
    public required long StreamOffset { get; init; }

    /// <summary>Record payload size in bytes, as stored in the size prefix; excludes the record's own framing and CRC.</summary>
    public required uint SizeBytes { get; init; }

    /// <summary>Object type code (e.g. LINE, LWPOLYLINE).</summary>
    public required ushort TypeCode { get; init; }

    /// <summary>Absolute object handle as read from the record (the value of <see cref="StreamHandle"/>).</summary>
    public required ulong Handle { get; init; }

    /// <summary>The record's own handle field, with its code and byte counter. It can differ
    /// from the handle map's entry for this offset; the walker does not cross-check them.</summary>
    public required DwgHandle StreamHandle { get; init; }

    /// <summary>Absolute stream offset where the bit-level payload begins (right after the size prefix).</summary>
    public required int BitsStart { get; init; }

    /// <summary>Length of the bit-level payload from <see cref="BitsStart"/> to the record CRC.</summary>
    public required int BitsLength { get; init; }
}
