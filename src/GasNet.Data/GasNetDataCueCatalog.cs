using System;
using System.Collections.Generic;

namespace GasNet.Data;

/// <summary>The result of loading a cue catalog: named <see cref="CueDefinition"/>s. Mirrors
/// <see cref="GasNetDataCatalog"/> for cues.</summary>
public sealed class GasNetDataCueCatalog
{
    private readonly Dictionary<string, CueDefinition> _cues;

    internal GasNetDataCueCatalog(Dictionary<string, CueDefinition> cues) => _cues = cues;

    public IReadOnlyDictionary<string, CueDefinition> Cues => _cues;

    /// <summary>Fetches a cue by name; unknown names throw with the list of loaded names.</summary>
    public CueDefinition Get(string name) =>
        _cues.TryGetValue(name, out var def)
            ? def
            : throw new GasNetDataException(
                $"No cue named '{name}' in the catalog. Loaded cues: [{string.Join(", ", _cues.Keys)}].");

    public bool TryGet(string name, out CueDefinition? def) => _cues.TryGetValue(name, out def);
}
