using DwgIO.Internal.Container;
using DwgIO.Internal.Parsing;
using DwgIO.Model;

namespace DwgIO;

/// <summary>
/// An opened Autodesk DWG file parsed without AutoCAD installed.
/// Exposes version info, the named-section catalog and per-section decompressed reads.
/// </summary>
public sealed class DwgDocument
{
    private readonly byte[] _fileBytes;
    private readonly R2004Container? _container;
    private readonly bool _verifyChecksums;
    private readonly List<string> _diagnostics = [];
    private byte[]? _objectStreamBytes;

    private DwgDocument(string path, byte[] fileBytes, DwgFileHeader fileHeader, R2004Container? container, DwgReaderOptions options)
    {
        Path = path;
        _fileBytes = fileBytes;
        FileHeader = fileHeader;
        _container = container;
        _verifyChecksums = options.VerifyChecksums;
        Version = fileHeader.Version;
        Sections = container is null
            ? []
            : [.. container.SectionMap.Sections.Select(ToSectionInfo)];
        SummaryInfo = container is null || !options.ReadSummaryInfo ? null : ReadOptionalSummaryInfo();
        Diagnostics = _diagnostics;
        if (container is not null)
        {
            if (options.ReadMetadata)
            {
                AppInfo = TryReadMetadata("AcDb:AppInfo", b => AppInfoParser.Parse(b, fileHeader.Codepage));
                FileDepList = TryReadMetadata("AcDb:FileDepList", b => FileDepListParser.Parse(b, fileHeader.Codepage));
                Preview = ReadOptionalPreview();
            }

            if (options.ReadObjects)
            {
                HandleMap = ReadOptionalHandleMap();
                Objects = ReadOptionalObjects();
                Entities = !options.ReadEntities ? [] : ReadOptionalEntities();
            }
        }
    }

    /// <summary>Absolute or relative path the document was opened from.</summary>
    public string Path { get; }

    /// <summary>Size of the file in bytes.</summary>
    public long FileSize => _fileBytes.Length;

    /// <summary>The DWG release identified from the file's version magic.</summary>
    public DwgVersionInfo Version { get; }

    /// <summary>The parsed 128-byte shared file prefix (version, maintenance version, codepage).</summary>
    public DwgFileHeader FileHeader { get; }

    /// <summary>Named sections of the container, in section-map order (R2004-family files).</summary>
    public IReadOnlyList<DwgSectionInfo> Sections { get; }

    /// <summary>The decrypted file header (R2004-family files only).</summary>
    public DwgContainerHeader? ContainerHeader => _container?.Header;

    /// <summary>Number of pages in the global page map (R2004-family files only).</summary>
    public int? PageCount => _container?.PageMap.Count;

    /// <summary>Drawing metadata from <c>AcDb:SummaryInfo</c>, when the section exists (R2004-family files).</summary>
    public DwgSummaryInfo? SummaryInfo { get; }

    /// <summary>Producer information from <c>AcDb:AppInfo</c>, when present.</summary>
    public DwgAppInfo? AppInfo { get; }

    /// <summary>External file dependencies from <c>AcDb:FileDepList</c>, when present.</summary>
    public DwgFileDepList? FileDepList { get; }

    /// <summary>Embedded thumbnail from <c>AcDb:Preview</c>, when present.</summary>
    public DwgPreview? Preview { get; }

    /// <summary>Handle→offset index from <c>AcDb:Handles</c> (R2004-family files, ReadObjects on).</summary>
    public DwgHandleMap? HandleMap { get; }

    /// <summary>Object inventory from <c>AcDb:AcDbObjects</c> (framing + identity, bodies decode later).</summary>
    public IReadOnlyList<DwgObjectInfo> Objects { get; } = [];

    /// <summary>Decoded entities with geometry (LINE/CIRCLE/ARC/POINT) — requires ReadObjects.</summary>
    public IReadOnlyList<DwgEntity> Entities { get; } = [];

    /// <summary>Best-effort parse failures collected while opening (section name → message).</summary>
    public IReadOnlyList<string> Diagnostics { get; } = [];

    /// <summary>Creates an in-memory document over a file header and a fixed entity list (tests).</summary>
    internal DwgDocument(string path, DwgFileHeader fileHeader, IReadOnlyList<DwgEntity> entities, DwgPreview? preview = null)
    {
        Path = path;
        _fileBytes = [];
        FileHeader = fileHeader;
        Version = fileHeader.Version;
        Sections = [];
        Diagnostics = _diagnostics;
        Entities = entities;
        Preview = preview;
    }

    /// <summary>Creates a document over a container built in the tests; reads follow <paramref name="options"/>.</summary>
    internal DwgDocument(string path, R2004Container container, DwgFileHeader fileHeader, DwgReaderOptions options)
        : this(path, [], fileHeader, container, options)
    {
    }

    /// <summary>Records a non-fatal problem found after the document was opened (e.g. by an exporter).</summary>
    internal void ReportDiagnostic(string message) => _diagnostics.Add(message);

    /// <summary>Opens and parses a DWG file.</summary>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="DwgUnsupportedVersionException">The file's container is not implemented yet
    /// (R13-R2000, R2007).</exception>
    /// <exception cref="DwgException">The file is corrupt or truncated.</exception>
    public static DwgDocument Open(string path, DwgReaderOptions? options = null)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"DWG file not found: {path}", path);
        }

        options ??= DwgReaderOptions.Default;
        var fileBytes = File.ReadAllBytes(path);
        try
        {
            return Parse(path, fileBytes, options);
        }
        catch (Exception ex) when (ex is not DwgException && IsParseFailure(ex))
        {
            throw Corrupt(ex);
        }
    }

    private static DwgDocument Parse(string path, byte[] fileBytes, DwgReaderOptions options)
    {
        var fileHeader = DwgFileHeader.Parse(fileBytes);

        if (fileHeader.Version.Family != DwgContainerFamily.R2004Family)
        {
            throw new DwgUnsupportedVersionException(
                $"{fileHeader.Version.Release} ({fileHeader.Version.Magic}) is not supported yet: " +
                (fileHeader.Version.Family == DwgContainerFamily.R2007
                    ? "the R2007 container is planned."
                    : "the R13-R2000 container is planned."));
        }

        var container = options.ReadSectionMap ? R2004Container.Parse(fileBytes) : null;
        return new DwgDocument(path, fileBytes, fileHeader, container, options);
    }

    /// <summary>
    /// Reads and decompresses a named section (e.g. <c>"AcDb:Header"</c>, <c>"AcDb:SummaryInfo"</c>).
    /// </summary>
    public byte[] ReadSection(string name)
    {
        var container = _container
            ?? throw new DwgException("The document was opened without its section map (ReadSectionMap = false).");
        var entry = container.SectionMap.FindByName(name)
            ?? throw new DwgException($"Section '{name}' not found. Available: {string.Join(", ", container.SectionMap.Sections.Select(s => s.Name))}");
        try
        {
            return container.ReadSection(_fileBytes, entry, _verifyChecksums, _diagnostics.Add);
        }
        catch (Exception ex) when (ex is not DwgException && IsParseFailure(ex))
        {
            throw Corrupt(ex);
        }
    }

    // Malformed data can surface as these runtime exceptions before a parser check rejects it.
    private static bool IsParseFailure(Exception ex) =>
        ex is DwgException or ArgumentException or OverflowException or IndexOutOfRangeException or InvalidOperationException;

    private static DwgException Corrupt(Exception ex) =>
        new($"The file is corrupt or truncated: {ex.Message}", ex);

    private DwgSummaryInfo? ReadOptionalSummaryInfo()
    {
        if (!TryGetReadableSection("AcDb:SummaryInfo", out var entry))
        {
            return null;
        }

        return TryParseMetadata("AcDb:SummaryInfo", static bytes => SummaryInfoParser.Parse(bytes), entry);
    }

    /// <summary>Looks up a named section the reader is expected to parse: present and not encrypted. An
    /// encrypted section is reported once, instead of the data simply going missing.</summary>
    internal bool TryGetReadableSection(string name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out SectionEntry? entry)
    {
        entry = _container!.SectionMap.FindByName(name);
        if (entry is null)
        {
            return false;
        }

        if (entry.Encryption == 1)
        {
            _diagnostics.Add($"{name}: the section is encrypted (encryption flag 1) and was skipped; decrypting it is not supported");
            return false;
        }

        return true;
    }

    private DwgHandleMap? ReadOptionalHandleMap()
    {
        if (!TryGetReadableSection("AcDb:Handles", out var entry))
        {
            return null;
        }

        try
        {
            return DwgHandleMap.Parse(ReadSectionCore(entry), _diagnostics.Add);
        }
        catch (Exception ex) when (IsParseFailure(ex))
        {
            _diagnostics.Add($"AcDb:Handles: {ex.Message}");
            return null;
        }
    }

    private IReadOnlyList<DwgObjectInfo> ReadOptionalObjects()
    {
        if (!TryGetReadableSection("AcDb:AcDbObjects", out var entry))
        {
            return [];
        }

        try
        {
            _objectStreamBytes = ReadSectionCore(entry);
            // Drive the walk by the handle map: after a gap it resumes at the next map offset, so records
            // beyond the gap are still inventoried instead of silently lost.
            var mapOffsets = HandleMap is null
                ? null
                : HandleMap.Entries.Select(e => e.Offset).Distinct().OrderBy(o => o).ToList();
            var objects = ObjectStreamWalker.Walk(_objectStreamBytes, r2010Plus: FileHeader.Version.Year >= 2010, mapOffsets, _diagnostics.Add);
            if (HandleMap is { Count: > 0 } map && objects.Count < map.Count)
            {
                _diagnostics.Add($"AcDb:AcDbObjects: inventory holds {objects.Count} records; the handle map lists {map.Count} entries ({map.Count - objects.Count} unreached)");
            }

            return objects;
        }
        catch (Exception ex) when (IsParseFailure(ex))
        {
            _diagnostics.Add($"AcDb:AcDbObjects: {ex.Message}");
            return [];
        }
    }

    private List<DwgEntity> ReadOptionalEntities()
    {
        if (_objectStreamBytes is null)
        {
            return [];
        }

        int year = FileHeader.Version.Year;
        var entities = new List<DwgEntity>();
        foreach (var info in Objects)
        {
            if (!EntityDecoder.IsEntityCode(info.TypeCode))
            {
                continue;
            }

            try
            {
                if (EntityDecoder.Decode(info, _objectStreamBytes, year) is { } entity)
                {
                    entities.Add(entity);
                }
            }
            catch (Exception ex) when (IsParseFailure(ex))
            {
                _diagnostics.Add($"entity {info.Handle:X} @{info.StreamOffset} (type 0x{info.TypeCode:X}): {ex.Message}");
            }
        }

        return entities;
    }

    private DwgPreview? ReadOptionalPreview()
    {
        if (!TryGetReadableSection("AcDb:Preview", out var entry))
        {
            return null;
        }

        try
        {
            var pageLayout = new List<SectionPageLayout>();
            var bytes = _container!.ReadSection(_fileBytes, entry, _verifyChecksums, _diagnostics.Add, pageLayout);
            return PreviewParser.Parse(bytes, pageLayout);
        }
        catch (Exception ex) when (IsParseFailure(ex))
        {
            _diagnostics.Add($"AcDb:Preview: {ex.Message}");
            return null;
        }
    }

    private T? TryReadMetadata<T>(string sectionName, Func<byte[], T> parse)
        where T : class
    {
        if (!TryGetReadableSection(sectionName, out var entry))
        {
            return null;
        }

        return TryParseMetadata(sectionName, parse, entry);
    }

    private T? TryParseMetadata<T>(string sectionName, Func<byte[], T> parse, SectionEntry entry)
        where T : class
    {
        try
        {
            return parse(ReadSectionCore(entry));
        }
        catch (Exception ex) when (IsParseFailure(ex))
        {
            // Metadata is best-effort: a malformed section must not fail the whole document.
            _diagnostics.Add($"{sectionName}: {ex.Message}");
            return null;
        }
    }

    private byte[] ReadSectionCore(SectionEntry entry) =>
        _container!.ReadSection(_fileBytes, entry, _verifyChecksums, _diagnostics.Add);

    private static DwgSectionInfo ToSectionInfo(SectionEntry entry) => new()
    {
        Name = entry.Name,
        Size = (long)entry.Size,
        PageCount = (int)entry.PageCount,
        MaxPageSize = entry.MaxPageSize,
        Compression = (int)entry.Compression,
        Encryption = (int)entry.Encryption,
        SectionId = entry.SectionId,
    };
}
