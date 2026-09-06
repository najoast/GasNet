namespace GasNet.Editor;

/// <summary>Ring buffer of GasNetLog lines shown in the editor's log panel.</summary>
public sealed class EditorLogBuffer
{
    private readonly object _lock = new();
    private readonly List<string> _lines = [];

    public void Add(string line)
    {
        lock (_lock)
        {
            _lines.Add($"[{DateTime.Now:HH:mm:ss}] {line}");
            if (_lines.Count > 60)
                _lines.RemoveAt(0);
        }
    }

    public List<string> Snapshot()
    {
        lock (_lock)
            return [.. _lines];
    }
}
