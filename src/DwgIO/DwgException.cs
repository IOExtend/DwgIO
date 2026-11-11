namespace DwgIO;

/// <summary>Thrown when a DWG file's version or container layout is not supported by this reader.</summary>
public sealed class DwgUnsupportedVersionException : DwgException
{
    /// <summary>Creates the exception with a message.</summary>
    public DwgUnsupportedVersionException(string message) : base(message)
    {
    }
}

/// <summary>Thrown when the DWG container cannot be parsed (corrupt file, truncated section, bad layout).</summary>
public class DwgException : Exception
{
    /// <summary>Creates the exception with a message.</summary>
    public DwgException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with a message and the exception that caused it.</summary>
    public DwgException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
