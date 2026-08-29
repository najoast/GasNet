namespace GasNet;

public sealed partial class AbilitySystemComponent
{
    /// <summary>Disables all cue routing (UE: <c>bSuppressGameplayCues</c>, doc §4.8.6).</summary>
    public bool SuppressGameplayCues { get; set; }

    public event Action<GameplayTag, GameplayCueParameters>? OnGameplayCueAdded;
    public event Action<GameplayTag, GameplayCueParameters>? OnGameplayCueRemoved;
    public event Action<GameplayTag, GameplayCueParameters>? OnGameplayCueExecuted;

    public void ExecuteGameplayCue(GameplayTag cueTag, GameplayCueParameters? parameters = null) =>
        RouteCue(cueTag, GameplayCueEvent.Executed, parameters);

    /// <summary>Adds a cue (fires OnActive then WhileActive — Duration/Infinite GE behavior, doc §4.8.8).</summary>
    public void AddGameplayCue(GameplayTag cueTag, GameplayCueParameters? parameters = null)
    {
        RouteCue(cueTag, GameplayCueEvent.OnActive, parameters);
        RouteCue(cueTag, GameplayCueEvent.WhileActive, parameters);
    }

    public void RemoveGameplayCue(GameplayTag cueTag, GameplayCueParameters? parameters = null) =>
        RouteCue(cueTag, GameplayCueEvent.Removed, parameters);

    public void RemoveAllGameplayCues() =>
        AbilitySystemGlobals.Get().GameplayCueManager.RemoveAllActorCues(AvatarActor ?? OwnerActor);

    private void RouteCue(GameplayTag cueTag, GameplayCueEvent evt, GameplayCueParameters? parameters)
    {
        if (SuppressGameplayCues || !cueTag.IsValid)
            return;
        parameters ??= new GameplayCueParameters { Target = AvatarActor ?? OwnerActor };

        switch (evt)
        {
            case GameplayCueEvent.OnActive: OnGameplayCueAdded?.Invoke(cueTag, parameters); break;
            case GameplayCueEvent.Removed: OnGameplayCueRemoved?.Invoke(cueTag, parameters); break;
            case GameplayCueEvent.Executed: OnGameplayCueExecuted?.Invoke(cueTag, parameters); break;
        }

        AbilitySystemGlobals.Get().GameplayCueManager.HandleGameplayCue(AvatarActor ?? OwnerActor, cueTag, evt, parameters);
    }

    /// <summary>Fires every cue tag carried by a spec with the given event.</summary>
    internal void FireCueTagsFromSpec(GameplayEffectSpec spec, GameplayCueEvent evt)
    {
        var cueTags = spec.GetAllCueTags();
        if (cueTags.IsEmpty)
            return;
        var parameters = GameplayCueParameters.FromSpec(spec, this);
        foreach (var tag in cueTags.Tags)
            RouteCue(tag, evt, parameters);
    }
}
