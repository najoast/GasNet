using GasNet;
using GasNet.Editor;
using GasNet.Editor.Components;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

// 核心库日志进编辑器的日志面板（同时保留控制台输出）。
var logBuffer = new EditorLogBuffer();
GasNetLog.OnWarn = msg => { logBuffer.Add(msg); Console.Error.WriteLine("[GasNet][Warn] " + msg); };
GasNetLog.OnError = msg => { logBuffer.Add(msg); Console.Error.WriteLine("[GasNet][Error] " + msg); };

builder.Services.AddSingleton(logBuffer);
builder.Services.AddSingleton<EditorProfile>();
builder.Services.AddSingleton<CatalogDocument>();

var app = builder.Build();
app.UseStaticFiles();
app.UseAntiforgery();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
