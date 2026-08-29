using GasNet;

namespace GasNet.Editor.Tests;

/// <summary>编辑器测试用的属性集与代码片段类型：供 CatalogDocument 的数据往返和
/// EditorProfile 的类型发现测试反射加载。</summary>
public class EditorTestAttributeSet : AttributeSet
{
    public GameplayAttributeData Health;
    public GameplayAttributeData Mana;
}

/// <summary>EditorProfile 发现测试专用的属性集（名字带 Discovery 前缀便于断言）。</summary>
public class DiscoveryAttributeSet : AttributeSet
{
    public GameplayAttributeData Mana;
}

public sealed class DiscoveryExecution : GameplayEffectExecutionCalculation
{
    public override void Execute(GameplayEffectExecutionParams executionParams)
    {
    }
}

public sealed class DiscoveryAbility : GameplayAbility
{
    protected override void ActivateAbility()
    {
    }
}
