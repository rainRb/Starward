# 绝区零国服 Steam 接入（实验）

此功能为独立 Fork 的实验实现，默认关闭，仅支持 `nap_cn`，使用正式 AppID `4162040`。尚未完成真实国服客户端、Steam Overlay、Steam 游玩时长与账号兼容性验收。编译和模拟进程测试通过不代表游戏实测通过。

## 来源与实现边界

- 官方 Starward：<https://github.com/Scighost/Starward>，基线提交 `df8ae07b5ad4c9c6cb70d288488ce8073ce01478`，MIT。当前主程序使用 .NET 10、WinUI 3 / Windows App SDK、MVVM、SQLite；原生 C++ 外层启动器负责版本目录与更新。
- Better Endfield：<https://github.com/Dr-hydra/Better-Endfield>，研究时 main 为 `285a9e55c46537a66065209e88c41740cb9ca81e`，根目录 LICENSE 为 GNU AGPL v3。Windows 控制器是 .NET 9 / WinUI 3，另有 C++ 原生模块、加载器与 Android 实现。
- 参考其 [Steam 接入文档](https://github.com/Dr-hydra/Better-Endfield/blob/main/docs/host/STEAM_CN_LAUNCH.md) 中的元数据、独立清单、原生路径和进程生命周期思路。本实现独立编写，未复制其 AGPL 源码，未引入注入器、XInput 代理、父进程替换或进程内模块。
- Steam 官方页面：<https://store.steampowered.com/app/4162040/>。2026-10-20 查询的入口为 `HYP.exe`、安装目录 `Zenless Zone Zero`、public BuildID `25595825`。这些只是当时快照，程序从每次获取／导入的元数据生成清单，不硬编码版本号。
- 获取按钮使用第三方公开 app-info 镜像 `https://api.steamcmd.net/v1/info/4162040`。它不是 Valve 官方 API；导入也接受同结构 JSON。缺少 Windows 入口、public BuildID 或 depot manifest 时拒绝配置，不填写猜测值。

## 设计

```text
Starward 的国服启动按钮 → steam.exe -applaunch 4162040
Steam 正式库条目 → 用户设置的 Launch Options
  → 稳定路径下的 Starward.Steam.exe
    → 现有国服 ZenlessZoneZero.exe
    → 等待该子进程退出，返回其退出码
```

辅助程序在 Steam 上下文中运行，保留继承的 Steam 环境，忽略 `%command%` 后面的国际服原始命令，不把国际服参数、票据或启动程序转发给国服。不通过 Steam API 伪造登录、许可、成就或游玩状态；环境变量检查只是防止误启动，不是许可证验证。

只修改用户显式选择的 Steam 游戏库中的独立占位目录和 `appmanifest_4162040.acf`。国服安装目录仅作为启动目标，不向其中写入任何文件。国服的下载、修复、更新和正常启动流程继续使用 Starward 的原有实现；只有国服且开关打开时才走 Steam 路径。

设置页新增 `Steam · CN`，提供元数据获取／导入、库选择、应用／刷新、移除、复制命令和默认关闭的启动开关，含英文与简体中文文案。其他语言回退英文。

## 使用

1. 备份自己的 Starward 设置，在单独目录解压实验构建。不要覆盖现有工作版本。先在 Starward 中定位可正常运行的官方国服 `ZenlessZoneZero.exe`。
2. 用自己的 Steam 账号通过正常商店流程入库绝区零。此功能不授予许可，不处理锁区，也不绕过商店限制。
3. 关闭 Starward 的自定义启动程序、CMD 启动和登录票据功能。普通启动参数、无边框和 DX12 参数由接入配置保存；不保存一次性登录票据。
4. 完全退出 Steam，包括托盘后台；关闭游戏和接入辅助程序。在国服的游戏设置中打开 `Steam · CN`。
5. 选择已登记的 Steam 库，获取或导入最新元数据，核对显示的目录／EXE／BuildID，然后点击“应用／刷新配置”。库内不能已有该 AppID 的国际服清单或游戏目录，其他库也不能已有同一 AppID 安装。程序不会接管或清理这些文件。
6. 点击“复制命令”，在 Steam → 绝区零 → 属性 → 启动选项中手动粘贴。修改前自行保存原启动选项。命令形如：

   ```text
   "...\Starward\SteamCN\4162040\helper\Starward.Steam.exe" --config "...\Starward\SteamCN\4162040\launch.json" -- %command%
   ```

7. 先从 Steam 正式库条目启动；需要时再打开“Starward 启动按钮使用 Steam”。该开关只改变 Starward 的国服启动按钮，不代替 Steam 中的启动选项。开始、退出时观察辅助程序是否随游戏结束。
8. 如果 Windows 提示需要管理员权限，完全退出 Steam 后通过正常 UAC 手动以管理员启动 Steam，再试一次。程序不会自动提权、改兼容性注册表或调整反作弊。若官方客户端拒绝该启动方式，应停止使用接入。

## 升级、回退与文件保护

稳定状态目录为 `%LOCALAPPDATA%\Starward\SteamCN\4162040`：`launch.json` 为国服路径与参数，`install.json` 为清单归属及中断恢复记录，`helper` 为独立自包含辅助程序，`backups` 保存变更前的托管 ACF。这里不依赖 Starward 的 `app-*` 版本目录，升级或退出 Starward 后仍可由 Steam 启动。更新辅助程序时保留 `.bak`。

移动国服目录、改变参数或更新实验版后，在 Steam 退出时重新应用配置。Starward 启动按钮会更新路径与参数；直接从 Steam 启动时使用上次保存值。元数据只按用户操作刷新，不运行后台定时更新；安装目录或入口改变时需先移除旧配置。刷新保留 Steam 的 `LastPlayed`、`LastOwner` 和未知字段。

Steam 可能在更新或验证时下载国际服内容，`AutoUpdateBehavior=1` 不能保证阻止下载。占位目录绝不能指向国服目录。发现非空占位 EXE、额外文件、目录链接、其他库已有安装或归属标记变化时，程序拒绝更新／移除并保留文件；不提供“强制清理”。此时应先人工检查目录和清单，不能把未知内容当缓存删除。

移除时，先完全退出 Steam 和游戏，点击“移除接入配置”，再手动清空本功能添加的 Steam 启动选项，或恢复自己保存的旧值。只移除归属匹配的清单、空占位文件及空父目录，不删除国服资源，不递归删除目录。备份、配置和辅助程序保留供检查；功能关闭后 Starward 恢复原有启动路径。若首次设置被中断，重新应用或移除可利用已写入的归属记录恢复。

## 验证状态与风险

已完成：核心库编译、完整 WinUI 主程序 x64 Debug 构建、自包含单文件辅助程序发布；隔离检查包含真实公开元数据快照解析、64 位 manifest、路径与 VDF 校验、已有安装保护、清单刷新字段保留、备份移除、中断恢复、目录 junction 拒绝、并发拒绝、参数及 Steam 原始命令隔离、原生工作目录与环境继承、等待进程退出及退出码透传。

未验证：真实游戏是否正常进入并登录、Steam 是否显示正式 AppID 正在游玩、服务端游玩时长增加、Shift+Tab Overlay、F12 截图、Steam Input、游戏与 Steam 更新后的持续可用性、游戏进程自重启后的跟踪、非 x64 平台与安装器打包。Steam 按钮是否走用户设定命令依赖其客户端行为，不能由离线测试证明。首次实测应先检查国服登录界面／服务器，再登录账号。

辅助程序仅等待它启动的进程，不读取受保护内存，也不追踪同名无关进程。若游戏自行脱离或重启进程，应观察实际 Steam 生命周期，不能把辅助程序仍存在直接当作有效游玩时长证据。

没有绕过反作弊、DLL 注入、修改受保护文件、虚报时长或自动解锁许可的实现；这也不能证明官方认可此启动方式或保证账号零风险。真实测试必须遵守游戏和 Steam 的适用条款。Valve 的 [Overlay 文档](https://partner.steamgames.com/doc/features/overlay) 说明正常 Steam 启动可提供覆盖层，但不构成本国服桥接方式的实测结论。

## 构建与复现

使用仓库 `global.json` 指定的 .NET 10 SDK。主程序还需要 Windows SDK 和 WinUI 构建环境；原生外层启动器需要其现有 C++ 工具链。

```powershell
dotnet build tests/SteamCN.FakeGame -c Release
dotnet run --project tests/SteamCN -c Release -- C:/temp/steam-cn-fixtures tests/SteamCN.FakeGame/bin/Release/net10.0/ZenlessZoneZero.exe
dotnet publish src/Starward.Steam -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
dotnet build src/Starward -c Debug -p:Platform=x64 -r win-x64
```

测试目录应选择一个新的独立目录；夹具会保留以便检查。所有模拟 EXE 都由 `SteamCN.FakeGame` 编译，与真实游戏无关。可在测试命令末尾附加已发布辅助程序路径和公开元数据 JSON 路径，额外检查实际发布二进制与快照解析；本次共 60 项通过。CI 默认执行不依赖在线元数据的 58 项。

本机只有 Windows SDK `10.0.22621.0` 的完整元数据，默认基线构建缺少 `10.0.26100.0/Platform.xml`。通过局部构建参数 `-p:CsWinRTWindowsMetadata=10.0.22621.0` 使用已有 SDK 完成 WinUI 编译，没有安装或修改 SDK。还补齐了基线 `FileCache.cs` 已使用但未声明的 MIT 依赖 `ZiggyCreatures.FusionCache 2.9.0`。编译仍包含上游可空性、不可达代码与裁剪警告。实验发布关闭裁剪、ReadyToRun 和自动更新，不修改仓库正常发布配置。
