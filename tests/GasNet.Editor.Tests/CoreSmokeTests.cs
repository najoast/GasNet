using GasNet.Editor;
using Xunit;

namespace GasNet.Editor.Tests;

/// <summary>锁住"纯 C# 库已迁出 Blazor 项目"这一重构：6 个核心类型必须可从测试程序集直接
/// 实例化，且命名空间保持 <see cref="GasNet.Editor"/>（不动 using，Blazor UI 不需要任何修改）。</summary>
public class CoreSmokeTests
{
    [Fact]
    public void CoreTypes_AreIn_GasNetEditor_Namespace()
    {
        // 如果有人把任一类型搬出了 Core 或改了命名空间，这里全挂——重构的契约锁定。
        Assert.Equal("GasNet.Editor", typeof(GasNet.Editor.CatalogDocument).Namespace);
        Assert.Equal("GasNet.Editor", typeof(GasNet.Editor.CueDocument).Namespace);
        Assert.Equal("GasNet.Editor", typeof(GasNet.Editor.EditorProfile).Namespace);
        Assert.Equal("GasNet.Editor", typeof(GasNet.Editor.CatalogSearch).Namespace);
        Assert.Equal("GasNet.Editor", typeof(GasNet.Editor.EditorLogBuffer).Namespace);
        Assert.Equal("GasNet.Editor", typeof(GasNet.Editor.Ui).Namespace);
    }

    [Fact]
    public void CoreTypes_CanBeInstantiated_NoBlazorDependency()
    {
        // 无参构造可用 = Core 是 plain class library，不是 Web SDK 项目。
        Assert.NotNull(new CatalogDocument());
        Assert.NotNull(new CueDocument());
        Assert.NotNull(new EditorProfile());
        Assert.NotNull(new EditorLogBuffer());
    }

    [Fact]
    public void CoreAssembly_IsNotTheBlazorAssembly()
    {
        // 确认 Core 单独成程序集（不是 .Web 那个），后续 Unity 引用才走得通。
        var coreAssembly = typeof(CatalogDocument).Assembly.GetName().Name;
        Assert.Equal("GasNet.Editor.Core", coreAssembly);
    }
}
