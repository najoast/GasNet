using GasNet;

namespace GasNet.Editor;

/// <summary>
/// 目录的反查扫描：一个标签/属性被哪些效果的哪些字段引用。标签匹配用核心库的层级语义
/// （持有 "Stun.Blocked" 的效果匹配查询 "Stun"），与运行时 HasTag 一致。
/// </summary>
public static class CatalogSearch
{
    public sealed record TagOccurrence(string Effect, string Field, GameplayTag Tag);

    public sealed record AttributeOccurrence(string Effect, string Field, string AttributeKey);

    /// <summary>枚举目录里每个标签出现点（含修饰符 sourceTags/targetTags、SetByCaller 的
    /// DataTag 与 attributeBased 的过滤标签）。</summary>
    public static IEnumerable<TagOccurrence> AllTagOccurrences(IEnumerable<CatalogDocument.Entry> effects)
    {
        foreach (var entry in effects)
        {
            var def = entry.Def;
            foreach (var (field, container) in DefTagContainers(def))
                foreach (var tag in container.Tags)
                    yield return new TagOccurrence(entry.Name, field, tag);

            for (var i = 0; i < def.Modifiers.Count; i++)
            {
                var modifier = def.Modifiers[i];
                var prefix = $"modifiers[{i}]";
                foreach (var tag in modifier.SourceTags.Tags)
                    yield return new TagOccurrence(entry.Name, $"{prefix}.sourceTags", tag);
                foreach (var tag in modifier.TargetTags.Tags)
                    yield return new TagOccurrence(entry.Name, $"{prefix}.targetTags", tag);

                switch (modifier.Magnitude)
                {
                    case SetByCallerMagnitude setByCaller when setByCaller.DataTag.IsValid:
                        yield return new TagOccurrence(entry.Name, $"{prefix}.magnitude.tag", setByCaller.DataTag);
                        break;
                    case AttributeBasedMagnitude based:
                        if (based.SourceTagFilter is { IsEmpty: false } sourceFilter)
                            foreach (var tag in sourceFilter.Tags)
                                yield return new TagOccurrence(entry.Name, $"{prefix}.magnitude.sourceTagFilter", tag);
                        if (based.TargetTagFilter is { IsEmpty: false } targetFilter)
                            foreach (var tag in targetFilter.Tags)
                                yield return new TagOccurrence(entry.Name, $"{prefix}.magnitude.targetTagFilter", tag);
                        break;
                }
            }
        }
    }

    /// <summary>引用了 queryTag（自身或其后代标签）的效果与字段；查询不是合法标签名时返回空。</summary>
    public static List<TagOccurrence> FindTagReferences(IEnumerable<CatalogDocument.Entry> effects, string query)
    {
        var result = new List<TagOccurrence>();
        GameplayTag queryTag;
        try
        {
            queryTag = Ui.ParseTag(query);
        }
        catch (ArgumentException)
        {
            return result; // 查询框是自由输入，非法标签名按"无结果"处理而不是崩溃
        }
        if (!queryTag.IsValid)
            return result;
        result.AddRange(AllTagOccurrences(effects).Where(o => o.Tag.MatchesTag(queryTag)));
        return result;
    }

    /// <summary>修饰符与 attributeBased 幅度里引用了某属性（"SetTypeName.FieldName"）的效果与字段。</summary>
    public static List<AttributeOccurrence> FindAttributeReferences(IEnumerable<CatalogDocument.Entry> effects, string query)
    {
        var result = new List<AttributeOccurrence>();
        var queryKey = query.Trim();
        foreach (var entry in effects)
        {
            var def = entry.Def;
            for (var i = 0; i < def.Modifiers.Count; i++)
            {
                var modifier = def.Modifiers[i];
                var key = AttributeKey(modifier.Attribute);
                if (string.Equals(key, queryKey, StringComparison.Ordinal))
                    result.Add(new AttributeOccurrence(entry.Name, $"modifiers[{i}].attribute", key));

                if (modifier.Magnitude is AttributeBasedMagnitude based)
                {
                    var captureKey = AttributeKey(based.Capture.Attribute);
                    if (string.Equals(captureKey, queryKey, StringComparison.Ordinal))
                        result.Add(new AttributeOccurrence(entry.Name, $"modifiers[{i}].magnitude.attribute", captureKey));
                }
            }
        }
        return result;
    }

    private static string AttributeKey(GameplayAttribute attribute) =>
        $"{attribute.AttributeSetType.Name}.{attribute.Name}";

    private static IEnumerable<(string Field, GameplayTagContainer Container)> DefTagContainers(
        GameplayEffectDefinition def)
    {
        if (def.AssetTags.IsNotEmpty) yield return ("assetTags", def.AssetTags);
        if (def.GrantedTags.IsNotEmpty) yield return ("grantedTags", def.GrantedTags);
        if (def.GameplayCueTags.IsNotEmpty) yield return ("cueTags", def.GameplayCueTags);
        if (def.RemoveGameplayEffectsWithTags.IsNotEmpty)
            yield return ("removeGameplayEffectsWithTags", def.RemoveGameplayEffectsWithTags);
        if (def.GrantedApplicationImmunityTags.IsNotEmpty)
            yield return ("grantedApplicationImmunityTags", def.GrantedApplicationImmunityTags);
        if (def.ApplicationTagRequirements.RequiredTags.IsNotEmpty)
            yield return ("applicationTagRequirements.require", def.ApplicationTagRequirements.RequiredTags);
        if (def.ApplicationTagRequirements.IgnoredTags.IsNotEmpty)
            yield return ("applicationTagRequirements.ignore", def.ApplicationTagRequirements.IgnoredTags);
        if (def.TargetTagRequirements.RequiredTags.IsNotEmpty)
            yield return ("targetTagRequirements.require", def.TargetTagRequirements.RequiredTags);
        if (def.TargetTagRequirements.IgnoredTags.IsNotEmpty)
            yield return ("targetTagRequirements.ignore", def.TargetTagRequirements.IgnoredTags);
        if (def.OngoingTagRequirements.RequiredTags.IsNotEmpty)
            yield return ("ongoingTagRequirements.require", def.OngoingTagRequirements.RequiredTags);
        if (def.OngoingTagRequirements.IgnoredTags.IsNotEmpty)
            yield return ("ongoingTagRequirements.ignore", def.OngoingTagRequirements.IgnoredTags);
    }
}
