using GasNet;
using GasNet.Editor;
using Xunit;

namespace GasNet.Editor.Tests;

public class EditorProfileTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "GasNetEditorTests", Guid.NewGuid().ToString("N"));

    public EditorProfileTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string PathOf(string name) => Path.Combine(_directory, name);

    [Fact]
    public void LoadAssembly_MissingFile_ThrowsFileNotFound()
    {
        var profile = new EditorProfile();
        Assert.Throws<FileNotFoundException>(() => profile.LoadAssembly(PathOf("missing.dll")));
        Assert.False(profile.HasProfile);
        Assert.False(profile.IsStale()); // 未加载时无所谓过期
    }

    [Fact]
    public void LoadAssembly_NotAnAssembly_ThrowsInvalidOperationException()
    {
        var path = PathOf("garbage.dll");
        File.WriteAllText(path, "this is not a managed assembly");

        var profile = new EditorProfile();
        var exception = Assert.Throws<InvalidOperationException>(() => profile.LoadAssembly(path));
        Assert.Contains("无法加载", exception.Message);
        Assert.False(profile.HasProfile);
    }

    [Fact]
    public void LoadAssembly_DiscoversTypes_AndBuildsOptions()
    {
        // 用测试程序集本身当"游戏 DLL"：里面的 EditorTestAttributeSet/Discovery* 就是发现目标
        var profile = new EditorProfile();
        profile.LoadAssembly(typeof(EditorProfileTests).Assembly.Location);

        Assert.True(profile.HasProfile);
        Assert.Contains(profile.Attributes, a => a.Key == "DiscoveryAttributeSet.Mana");
        Assert.Contains(profile.Executions, t => t.Name == "DiscoveryExecution");
        Assert.Contains(profile.Abilities, t => t.Name == "DiscoveryAbility");

        var options = profile.BuildOptions();
        Assert.True(options.AttributeSets.ContainsKey("DiscoveryAttributeSet"));
        Assert.True(options.Types.ContainsKey("DiscoveryExecution"));
        Assert.True(options.Types.ContainsKey("DiscoveryAbility"));

        // 同一文件重新加载：没有重新编译就不算过期
        Assert.False(profile.IsStale());
    }

    [Fact]
    public void IsStale_TrueAfterFileChanges()
    {
        // 用测试程序集的副本：改时间戳既能验证过期检测，也顺带证明内存副本加载不留文件锁
        // （LoadFromAssemblyPath 的话这里会写失败）。
        var copy = PathOf("GameCopy.dll");
        File.Copy(typeof(EditorProfileTests).Assembly.Location, copy, overwrite: true);

        var profile = new EditorProfile();
        profile.LoadAssembly(copy);
        Assert.False(profile.IsStale());

        File.SetLastWriteTimeUtc(copy, DateTime.UtcNow.AddSeconds(2));
        Assert.True(profile.IsStale());
    }
}
