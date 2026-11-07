# BK DayZ Mod Downloader

面向 DayZ 服务器管理员的 Windows 创意工坊模组下载工具。通过图形界面登录 Steam、管理下载队列、查看进度和实时速度，无需安装 Steam 客户端或 DayZ 游戏客户端。

使用 **WPF / .NET 10** 构建，下载引擎基于 [DepotDownloader](https://github.com/SteamRE/DepotDownloader) 3.4.0，面向 DayZ（App ID `221100`）创意工坊。

> 原创界面、通用模块与测试代码采用 MIT；下载引擎及整合分发包保留 GPL-2.0 授权。具体范围见 [许可证说明](LICENSING.md)。

## 功能

- **Steam 登录**：支持 Steam Guard 验证码、手机确认和可选的登录状态保存。
- **下载队列**：通过工坊 ID、工坊链接或清单添加模组，逐项显示状态、进度和实时速度。
- **任务操作**：每行提供开始 / 继续、暂停、取消、删除；支持对勾选任务批量操作。
- **文件复用**：校验并复用已有文件和下载缓存，失败后支持重试。
- **自选目录**：统一保存为 `@模组名`，不附加 `_ID`；一键打开下载目录。
- **简洁界面**：参考 [Geist](https://vercel.com/geist/introduction) 的中性色与细边框，提供沉浸式圆角窗口、亮暗主题和可折叠的黑底绿字日志面板。

## 系统要求

| 项目 | 要求 |
| --- | --- |
| 操作系统 | Windows 11 x64，或 Windows Server 2019 x64 Desktop Experience（带桌面） |
| 运行环境 | [.NET 10 Desktop Runtime x64](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) |
| Steam 账号 | 能够访问目标 DayZ 创意工坊内容的账号 |
| 存储空间 | 程序、运行时及所下载模组所需空间 |

目标机器只需安装 **Desktop Runtime**，无需安装 .NET SDK。Windows Server Core 不支持此 WPF 图形界面。

目前已进行开发机界面与离线引擎通信验证；真实账号登录、完整网络下载流程以及 Windows Server 2019 实机兼容性仍需进一步验证。

## 快速开始

从 [GitHub Releases](https://github.com/binbinku/BKDayzModDownloader/releases/latest) 下载 `BKDayzModDownloader-win-x64.zip`，解压到可写目录。请下载该程序压缩包；GitHub 自动生成的 `Source code` 附件仅包含源码。

1. 保留解压后的**整个文件夹**及其中的 `Engine` 子目录，不要只复制主程序 EXE。如果自行构建，对应目录为 `publish/win-x64`。
2. 安装 .NET 10 Desktop Runtime x64，双击 `BKDayzModDownloader.exe`。也可运行 `Start.bat` 检查运行时。
3. 输入自己的 Steam 账号，按提示完成 Steam Guard 验证或手机确认。
4. 登录后，在右上角“下载设置”中选择保存目录。
5. 点击右下角悬浮 `＋`，输入模组 ID 或工坊链接，选择“加入队列”或“添加并下载”。
6. 使用行内按钮管理单项任务，或勾选多项后使用底部批量操作按钮。

“导入清单”位于队列右上角；右上角“日志”可展开或收起日志面板；右下角“打开下载目录”可直接查看文件。

## 批量导入

支持 `模组名 ID`、纯数字 ID，以及带或不带模组名前缀的 SteamCMD 下载命令清单。例如：

```text
CF 1559212036
Dabs Framework 2545327648
1828439124
```

工坊链接示例：

```text
https://steamcommunity.com/sharedfiles/filedetails/?id=1559212036
```

同一 ID 自动去重，ID 为 `0` 的条目跳过。导入后按需勾选，再点击底部开始按钮。

仓库附带 [ModList.example.txt](ModList.example.txt)。可以在项目根目录另存自己的 `ModList.txt`；构建时优先使用该文件，否则使用示例清单。个人清单已加入 Git 忽略规则。

## 下载与文件管理

### 目录

默认保存到程序旁的 `Downloads`，可以在设置中修改。程序会读取工坊名称，例如将 CF 保存为 `Downloads/@CF`，不会自动部署到服务器 `Mods`，也不会修改 `keys` 或服务器启动配置。

如果新目录不存在，程序会尝试将同 ID 的旧 `@模组名_ID` 目录改名并复用；新旧目录同时存在时不自动合并。目录中的 `.bk-workshop-id` 用于识别模组，避免同名模组混写。

若目标位置位于带 `DayZServer_x64.exe` 的服务器目录下，且检测到 DayZ 服务器进程正在运行，程序会拒绝开始下载。建议先下载到独立目录，停服后自行部署。

### 队列、暂停与重试

- 一个登录会话顺序处理模组；设置中的块并发表示**单个模组的数据块并发数**。
- 暂停停止安排新的文件和数据块，在途请求可能继续完成。暂停中的当前任务会等待继续或取消，然后处理后续任务。
- 取消保留已下载文件和登录会话，随后继续下一项；等待在途请求结束时会显示“取消中”。
- 删除只移除队列任务，不删除磁盘文件；正在下载的任务会先取消。
- 引擎包含网络重连处理。启用失败重试时，下载返回失败后等待 30 秒，最多重试 3 次；也可通过行内按钮手动重试。
- 保留 `.DepotDownloader` 清单与暂存数据可帮助续传。不同失败阶段的恢复效果可能不同，不能保证所有在途数据都得到保留。

### 进度与速度

进度按已处理的未压缩文件数据计算，包含校验通过的本地数据，因此校验缓存时进度可能跳变。速度按压缩传输数据采样，不包含网络协议开销；读取元数据、校验文件或等待验证时可能显示 `0 B/s`。远端未提供总长度的下载会显示不确定进度。

## 登录与本地数据

密码不写入命令行、配置或日志，仅通过界面与引擎之间的标准输入管道传递。勾选“记住登录状态”后才保存令牌，使用 Windows DPAPI 按当前 Windows 用户加密，不保存明文密码。

配置与日志位于：

```text
%LOCALAPPDATA%\BKTools\BKDayzModDownloader
```

登录失效时，可在登录页清除已保存的登录状态后重试。日志超过 4 MiB 时轮换，保留当前和上一份；下载队列运行状态不持久化。

## 从源码构建

开发需要 Windows、[.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)；发布脚本还需要 PowerShell 7。上游源码已随仓库提供，无需额外初始化子模块。

```powershell
git clone https://github.com/binbinku/BKDayzModDownloader.git
cd BKDayzModDownloader

# 还原依赖并构建
dotnet build src/BKDayzModDownloader/BKDayzModDownloader.csproj -c Release

# 运行离线验证
dotnet run --project tests/BKDownloader.Tests/BKDownloader.Tests.csproj -c Release -- src/BKDownloader.Engine/bin/Release/net10.0-windows/BKDownloader.Engine.exe

# 生成 win-x64 发布目录与压缩包
pwsh -File .\publish.ps1
```

输出目录为 `publish/win-x64`，压缩包为 `publish/BKDayzModDownloader-win-x64.zip`。发布采用依赖运行时的方式，不捆绑 .NET Runtime；包内包含许可证、第三方声明及对应源码。

NuGet 依赖使用固定版本，还原到项目内 `.packages`。发布时请检查随包的 `ModList.txt` 是否为准备公开的清单。下载目录、构建缓存、登录状态和日志不应提交到仓库。

开发辅助入口：

- `BKDayzModDownloader.exe --preview`：离线预览下载界面，显示明确标记的示例数据，不连接 Steam、不保存配置或日志。
- `BKDownloader.Engine.exe --self-test`：离线通信验证，不连接 Steam。

## 项目结构

```text
src/
  BKDayzModDownloader/    WPF 界面、队列管理与引擎通信
  BKDownloader.Core/     清单解析、配置、协议与目录检查
  BKDownloader.Engine/   下载引擎入口、身份验证与会话适配
tests/
  BKDownloader.Tests/    离线验证程序
vendor/
  DepotDownloader/       固定版本的上游源码及本地修改
```

DepotDownloader 固定到提交 `c553ef4d60c00a4f5fd16c9fe017f569001589ff`。适配内容与依赖许可证见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。

## 参与贡献

欢迎提交 [Issue](https://github.com/binbinku/BKDayzModDownloader/issues) 或 Pull Request。报告问题时请提供操作系统、程序版本、复现步骤和脱敏日志；请勿公开密码、登录令牌或验证码。

修改下载逻辑后，请运行离线验证，并说明是否完成真实下载测试；修改界面时，建议附上亮色和暗色主题截图。提交的原创代码沿用所属目录的许可证。

## 许可证

本项目保留上游授权，并对原创部分提供 MIT 授权：

- **MIT**：原创 WPF 界面、Core 通用模块、测试代码，以及项目原创文档和构建脚本，见 [LICENSE-MIT](LICENSE-MIT)。
- **GPL-2.0**：DepotDownloader 源码、下载引擎适配代码，以及当前包含该引擎的整合分发包，见 [LICENSE](LICENSE)。
- 第三方依赖继续适用各自许可证，见 [第三方声明](THIRD-PARTY-NOTICES.md)。

MIT 授权不覆盖或替换 DepotDownloader 的 GPL 授权，不能将当前完整程序标注为“仅 MIT”。完整范围与再分发说明见 [LICENSING.md](LICENSING.md)。

感谢 [SteamRE](https://github.com/SteamRE) 及相关开源项目。本项目为非官方工具，与 Valve、Steam 或 Bohemia Interactive 无隶属关系。
