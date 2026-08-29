using System;
using System.Collections.Generic;
using System.Linq;

namespace GasNet.Data;

/// <summary>The result of loading a data catalog: named GameplayEffect definitions.</summary>
public sealed class GasNetDataCatalog
{
    private readonly Dictionary<string, GameplayEffectDefinition> _effects;

    internal GasNetDataCatalog(Dictionary<string, GameplayEffectDefinition> effects) => _effects = effects;

    public IReadOnlyDictionary<string, GameplayEffectDefinition> Effects => _effects;

    /// <summary>Fetches an effect by name; unknown names throw with the list of loaded names.</summary>
    public GameplayEffectDefinition Get(string name) =>
        _effects.TryGetValue(name, out var def)
            ? def
            : throw new GasNetDataException(
                $"No effect named '{name}' in the catalog. Loaded effects: [{string.Join(", ", _effects.Keys)}].");

    public bool TryGet(string name, out GameplayEffectDefinition? def) => _effects.TryGetValue(name, out def);
}
