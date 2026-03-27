# Moder.Update.Demo

用于测试 Moder.Update 双进程更新流程的示例控制台应用程序。

## 前置要求

- .NET 10.0 SDK 或更高版本
- Windows 操作系统（该库使用 Win32 `ReplaceFile` API）

## 快速开始

### 步骤 1：构建解决方案

```bash
dotnet build src/Moder.Update.sln
```

### 步骤 2：创建测试更新包

创建从 1.0.0 版本更新到 1.1.0 版本的包：

```bash
dotnet run --project src/Moder.Update.Demo -- \
    --create-package 1.0.0 1.1.0 ./demo-app ./demo-packages
```

这会创建：
- `demo-packages/catalog.json` — 更新目录
- `demo-packages/update-1.0.0-to-1.1.0.zst` — 更新包

### 步骤 3：发布示例程序

```bash
mkdir -p ./test-app
dotnet publish src/Moder.Update.Demo -c Release -o ./test-app
```

### 步骤 4：验证更新检测是否正常工作

```bash
dotnet ./test-app/Moder.Update.Demo.dll --version
# 输出: Current version: 1.0.0

dotnet ./test-app/Moder.Update.Demo.dll --check
# 输出:
# Checking for updates from version 1.0.0...
# Packages directory: /path/to/demo-packages
# Update available! Latest version: 1.1.0
# Update path:
#   1.0.0 -> 1.1.0 (update-1.0.0-to-1.1.0.zst)
```

### 步骤 5：应用更新

```bash
dotnet ./test-app/Moder.Update.Demo.dll --apply
```

这将：
1. 下载更新包
2. 将其应用到应用程序目录
3. 启动更新进程并重启应用

重启后，运行 `--version` 确认更新已应用。

## 命令

| 命令 | 描述 |
|------|------|
| `--version` | 显示当前版本 |
| `--check` | 使用本地目录检查更新 |
| `--apply` | 应用更新包并重启 |
| `--create-package <from> <to> <source> <output>` | 创建测试更新包 |

## 工作原理

1. `--check` 使用 `UpdateChecker` 从本地 `demo-packages/` 目录获取 `catalog.json`
2. `--apply` 下载 `.zst` 包并调用 `UpdateManager.ApplyUpdateAsync()`
3. 应用后，`PrepareRestart()` 启动 `Moder.Update.Updater.exe` 并退出应用
4. 更新进程等待应用退出，替换文件，验证校验和，然后重启

## 包格式

更新包使用 Moder.Update 的二进制格式：
- 4 字节魔术头：`MUP\0`
- Zstd 压缩的 tar 归档，包含：
  - `manifest.json` — 版本信息、文件列表、校验和
  - 要替换的应用程序文件

## 故障排除

**"Updater not found" 警告**：更新进程二进制文件应在 `src/Moder.Update.Updater/bin/Release/net10.0-windows/win-x64/Moder.Update.Updater.exe`。如果缺失，请重新构建。

**"No update available"**：确保 `demo-packages/catalog.json` 存在，且包含匹配 `minSourceVersion` 的条目。

**包创建失败**：确保源目录包含你要打包的文件。
