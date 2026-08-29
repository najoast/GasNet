using System;

namespace GasNet.Data;

/// <summary>Thrown when a data catalog cannot be loaded or resolved. The message includes the
/// effect name / JSON path where the problem was found.</summary>
public class GasNetDataException : Exception
{
    public GasNetDataException(string message) : base(message) { }
    public GasNetDataException(string message, Exception inner) : base(message, inner) { }
}
