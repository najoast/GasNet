# 重新构建 GasNet（netstandard2.1）并刷新 Assets/Plugins 下的全部 DLL。
# Assets/Plugins 里已提交预构建产物，只有改了 src/ 里的核心代码后才需要跑本脚本。
#
# 用法: powershell -ExecutionPolicy Bypass -File setup.ps1
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$plugins  = Join-Path $PSScriptRoot "Assets\Plugins"
$csproj   = Join-Path $repoRoot "src\GasNet.Data\GasNet.Data.csproj"

# publish 会传递构建 GasNet 并带上 System.Text.Json 依赖闭包（ns2.1 目标）
dotnet publish $csproj -c Release -f netstandard2.1 -o $plugins
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ""
Write-Host "Refreshed Assets/Plugins:"
Get-ChildItem $plugins -Filter *.dll | ForEach-Object { Write-Host ("  " + $_.Name) }
