using System.Collections.Generic;
using GasNet;

namespace GasNet.Data;

/// <summary>
/// Data-driven GameplayCue: a tag (under the <c>GameplayCue.</c> root), a static/actor split, and a
/// flat list of <see cref="CueAction"/> entries to dispatch. Loaded from JSON by
/// <see cref="GasNetDataCueLoader"/>; turned into a registered <c>GameplayCueNotify</c> at runtime by
/// <c>GameplayCueRuntime</c>.
///
/// <para>This type lives in <c>GasNet.Data</c> so the core library stays dependency-free; the
/// runtime sees it only as a generic payload.</para>
/// </summary>
public sealed class CueDefinition
{
    /// <summary>The cue name (the JSON object's key) — used as the lookup key in the catalog.</summary>
    public string Name { get; set; } = "";

    /// <summary>Tag the cue is registered against. Must be valid and under <c>GameplayCue.</c>.</summary>
    public GameplayTag Tag { get; set; }

    /// <summary>Static = one-shot per Execute; Actor = one instance per (target, tag) with add/remove lifecycle.</summary>
    public bool IsStatic { get; set; } = true;

    /// <summary>Effect list. Order is preserved by the writer for diff stability; dispatching is by
    /// <see cref="CueAction.TriggerOn"/>, not by index. Must be non-empty — the loader rejects empty
    /// arrays because a cue with no actions can never fire anything.</summary>
    public List<CueAction> Actions { get; set; } = [];
}
