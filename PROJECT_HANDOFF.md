# ToolBox 项目转移交接文档

> 最后核对：2026-08-28（Asia/Shanghai）
>
> 仓库：<https://github.com/OQTQO/ToolBox>
>
> 当前稳定版本：[`v0.1.1`](https://github.com/OQTQO/ToolBox/releases/tag/v0.1.1)
>
> 本文是项目转移的首要入口；详细阶段记录见 [`PROJECT_CONTEXT.md`](PROJECT_CONTEXT.md)，失败复盘见 [`PROJECT_RETROSPECTIVES.md`](PROJECT_RETROSPECTIVES.md)。

## 1. 一页结论

ToolBox 是一个 Windows 10/11、.NET 8、WPF 的模块化工具平台个人学习项目。0.1 系列已经完成并封版，当前发布版包含：

- 雾银 B 方案 WPF Host、Module T 图标、中英双语、系统托盘和结构化日志；
- 安装、打开显示、功能运行互相独立的插件三层状态；
- Plugin API v1、InProcess/OutOfProcess 生命周期、资源 Lease、服务 Broker 和故障隔离；
- 安全 `.tpk` 安装、版本并存、原子激活、升级回退和 SHA-256 完整性校验；
- Keyboard & Mouse Test 官方插件；
- Phone Audio Relay 官方插件，可让已配对 Android 手机通过蓝牙 A2DP 在电脑正常输出中播放媒体音频，同时保留电脑自身声音；
- 本地、CI、Tag Release 共用的确定性发布验证入口。

`v0.1.1` 已公开发布，构建为 0 警告/0 错误，自动化测试 `86/86` 通过，最终窗口、插件管理和 Android 手机音频物理验收由原项目所有者确认通过。

最重要的边界：发布版 Host 目前只注册两个内置产品。Plugin API v1 已具备稳定运行时契约，但尚未提供通用第三方插件发现、动态 UI 注册、外部开发套件或插件模板。因此现在不能对外宣称“任意第三方 `.tpk` 安装后都会自动出现在 Host 中”。

## 2. 仓库与发布状态

| 项目 | 当前状态 |
| --- | --- |
| GitHub 仓库 | `OQTQO/ToolBox`，Public |
| 默认分支 | `main` |
| 交接前 `main` | `aa251d7ac1fe9c58e95483afbb629eeaa7c3c4a1` |
| v0.1.1 Tag 提交 | `ae01b303e04dcd07ab38dd850f7d05fad20b4f0b` |
| v0.1.1 Release | 公开、非 Draft、非 Prerelease |
| v0.1.0 | 保留，不要移动或覆盖 |
| CI 平台 | GitHub Actions `windows-latest` |
| SDK | 项目目标为 .NET 8；交接时本机为 `8.0.424` |
| 最新测试基线 | `86 passed / 0 failed / 0 skipped` |

公开下载：

- [ToolBox-Host-v0.1.1-win-x64.exe](https://github.com/OQTQO/ToolBox/releases/download/v0.1.1/ToolBox-Host-v0.1.1-win-x64.exe)
- [KeyboardMouse-0.1.1.tpk](https://github.com/OQTQO/ToolBox/releases/download/v0.1.1/KeyboardMouse-0.1.1.tpk)
- [PhoneAudioRelay-0.1.1.tpk](https://github.com/OQTQO/ToolBox/releases/download/v0.1.1/PhoneAudioRelay-0.1.1.tpk)
- [SHA256SUMS-v0.1.1.txt](https://github.com/OQTQO/ToolBox/releases/download/v0.1.1/SHA256SUMS-v0.1.1.txt)

已独立下载线上资产并核对清单：

```text
43fe18c6fde7186868b293dbd85153b74cc2d1dcb8ae7b827b1a23d8f4eaa04e  ToolBox-Host-v0.1.1-win-x64.exe
53eaf9f8aec1493f427f679226a25eddc111b6a6bef812683f0a1da624f3b430  KeyboardMouse-0.1.1.tpk
d1aa707f8efe8c3cc0c5cb34f6b7d4e88a0d9f281968ee683410929251c5b166  PhoneAudioRelay-0.1.1.tpk
```

关键 GitHub 证据：

- [发布 PR #6](https://github.com/OQTQO/ToolBox/pull/6)
- [发布合并后 main CI](https://github.com/OQTQO/ToolBox/actions/runs/33149935466)
- [v0.1.1 Tag Release 工作流](https://github.com/OQTQO/ToolBox/actions/runs/33150052667)
- [发布文档收尾 main CI](https://github.com/OQTQO/ToolBox/actions/runs/33150465301)

## 3. 产品目标与非目标

### 3.1 当前目标

- 为 Windows 提供稳定、小型、可扩展的本地工具 Host；
- 让插件的安装、显示和运行状态可独立管理；
- 在失败时显示真实生命周期状态，不把失败伪装成“已停用”；
- 通过统一脚本生成可复现的 Host 和官方插件发布资产；
- 保持个人学习版本地运行，不依赖服务器。

### 3.2 0.1 系列明确不包含

- 通用第三方插件动态发现和动态工作区注册；
- 公共 WPF/UI 贡献契约或跨框架 UI 插件协议；
- 插件市场、在线更新、账号、服务器或遥测；
- 官方数字签名链、发布者真实性认证；
- 权限强制、安全沙箱或恶意代码隔离承诺；
- 全局键盘钩子、Raw Input、宏、输入注入；
- 电话/HFP、手机麦克风、电脑录音或逐应用音量控制。

## 4. 解决方案结构

```text
ToolBox.sln
├─ src/
│  ├─ ToolBox.PluginSdk/       稳定 Plugin API v1 与 Experimental 产品桥接契约
│  ├─ ToolBox.Core/            生命周期、隔离、资源、服务、日志、包安装器
│  ├─ ToolBox.PluginWorker/    OutOfProcess Worker、Named Pipe 协议和进程隔离
│  └─ ToolBox.Host/            WPF Shell、设置、托盘、工作区和关闭/重启编排
├─ spikes/
│  ├─ KeyboardTest/            已发布的 Keyboard & Mouse 产品运行时
│  └─ AudioRelay/              已发布的 Phone Audio Relay 产品运行时
├─ tests/
│  ├─ ToolBox.Core.Tests/      Core、Installer、Plugin API 和兼容性测试
│  ├─ ToolBox.Host.Tests/      Host 设置、生命周期、工作区和 UI 状态测试
│  ├─ AudioRelay.Tests/        音频平台与生命周期测试
│  └─ Fixtures/                Crash/Hang/Unload/Worker/旧 SDK/恶意包等夹具
├─ tools/                       发布验证、TPK 打包和图标生成脚本
├─ .github/workflows/           CI 与 Tag Release 适配器
└─ *.md                         范围、策略、上下文、复盘和交接文档
```

`spikes` 名称是历史遗留；其中两个项目已经作为 0.1.1 正式官方插件发布。后续若进入新版本，可在不破坏包身份和测试路径的前提下评估迁移到 `plugins/` 或 `products/`，不要只为改名制造大范围路径漂移。

### 4.1 依赖方向

```text
ToolBox.Host ──────> ToolBox.Core ──────> ToolBox.PluginSdk
      │                    │
      └────> PluginWorker ─┘

KeyboardTest ─────────────> ToolBox.PluginSdk
AudioRelay ───────────────> ToolBox.PluginSdk + Windows WinRT projection
```

长期插件边界只能是 `ToolBox.PluginSdk`。不要让 Host/Core 类型进入稳定插件公开契约，也不要让插件私带 `ToolBox.PluginSdk.dll`。

## 5. 关键架构与行为

### 5.1 Host 启动与关闭

入口位于 `src/ToolBox.Host/App.xaml.cs`：

1. 加载 Host 设置和本地化；
2. 创建 Session/Launch Attempt、诊断和结构化日志；
3. 创建 `PluginPackageInstaller`；
4. 从已提交的激活包创建内置工作区注册；
5. 创建主窗口和托盘服务；
6. 依次推进到 `Healthy`。

关闭/重启由 `HostLifetimeState` 和 `HostShutdownCoordinator` 编排。退出意图幂等，单个清理步骤失败不会跳过后续步骤。当前顺序为：诊断进入 Stopping、记录关闭开始、停止插件 ViewModel、释放托盘、诊断进入 Stopped、记录关闭完成、释放日志、释放安装器，最后在重启意图下启动替代进程。

必须维持的规则：Host 只有在插件实际生命周期已结束后才能声称插件 Disabled；停止或卸载失败必须保持 `Faulted`、`DisableFailed` 或 `RestartRequired` 等可见状态。

### 5.2 插件三层状态

- `IsInstalled`：插件包是否存在已提交的激活版本；
- `IsOpened`：是否在左侧导航显示，由 Host 设置持久化；
- `IsRuntimeEnabled`：本次进程内功能是否已运行，不跨重启自动恢复。

行为约定：

- 新安装成功：默认打开并显示，但不自动运行功能；
- 设置页关闭：先停止运行时；成功后隐藏入口，失败则不隐藏；
- 插件页停用：只停止功能，导航入口仍保留；
- 重新打开：恢复入口，等待用户主动启用；
- 卸载：移除入口和打开状态，但保留插件用户数据；
- 正常关闭窗口：默认进入托盘；设置可改为直接退出。

### 5.3 工作区注册

`BuiltInPluginWorkspaceCatalog` 创建 `PluginWorkspaceRegistration` 集合，`MainWindowViewModel` 从集合投影导航和设置卡片，WPF 通过 DataTemplate 显示独立插件页面。

新增一个“内置产品”现在需要：

1. 实现插件运行时和 Manifest；
2. 实现 Host 侧页面 ViewModel/View；
3. 在 `BuiltInPluginWorkspaceCatalog` 注册；
4. 增加本地化、包脚本、测试和 Release 资产规则。

这仍然是编译期注册，不是第三方动态注册。

### 5.4 InProcess 与 OutOfProcess

- InProcess 使用可回收 `AssemblyLoadContext`；共享 PluginSdk，停止后进行真实卸载检查；
- OutOfProcess 使用 `ToolBox.PluginWorker`、Named Pipe 握手和 Windows Job Object；Worker 及子进程在清理时受控终止；
- 一个 Shutdown Deadline 贯穿 Host、Worker 请求和清理过程；
- Crash、Hang、ALC 泄漏、协议不匹配和子进程清理都有测试夹具。

两个 0.1.1 官方插件当前都以 InProcess 发布。OutOfProcess 能力是平台基础设施，不代表两个官方插件已经切换到该模式。

### 5.5 资源与服务

- `ResourceManager` 提供 Shared/Exclusive Lease 冲突仲裁；
- `ServiceBroker` 提供懒启动、Lease 复用、引用计数和空闲停止；
- Lease 归属于 `PluginLifetimeScope`，停止插件时必须跟随 Scope 清理；
- 键鼠插件使用独占资源 `keyboard.test.surface`；
- 音频插件使用独占资源 `audio.bluetooth.a2dp-sink`。

## 6. 包格式、目录和本地数据

### 6.1 `.tpk`

`.tpk` 是 ZIP 容器。安装器执行路径穿越、绝对路径、大小、压缩比、重复/大小写冲突、Manifest、API major、平台、运行时结构和 SHA-256 校验。

核心规则：

- `manifest.json.version` 必须等于 `package.json.pluginVersion`；
- 包不得携带私有 `ToolBox.PluginSdk.dll`；
- 同一 Plugin ID 的版本并存；
- 只有 `state.json.phase = committed` 的激活版本可被 Host 使用；
- 安装失败保留旧激活版本；
- Config/State 可按策略快照，Cache/UserData 不自动复制；
- 卸载运行包不会删除插件用户数据。

SHA-256 只证明文件完整性，不证明发布者身份。不要把当前校验描述成数字签名或官方认证。

### 6.2 运行目录

```text
<Host EXE 所在目录>\Plugins\               已安装包、版本目录、state.json
%LocalAppData%\ToolBox\Plugins\            插件 Config/State/Cache/UserData 根目录
%LocalAppData%\ToolBox\ui-settings.json     语言、关闭行为、插件打开状态
%LocalAppData%\ToolBox\Logs\               结构化 JSONL 日志
```

Host 为 self-contained 单文件发布，但运行时会在 EXE 同级 `Plugins` 目录写安装状态。若把 EXE 放入普通用户不可写目录，插件安装会失败；产品化安装器需要另行设计可写的数据/安装布局。

## 7. 两个官方插件

### 7.1 Keyboard & Mouse Test

- Plugin ID：`com.toolbox.keyboard-test`
- Assembly：`KeyboardTest.dll`
- 模式：InProcess
- 功能：Host 局部区域按键/鼠标观察、计数和两项设置；
- 不包含：全局钩子、Raw Input、宏、输入注入；
- 详细范围：[`PRODUCT_KEYBOARD_MOUSE_SCOPE.md`](PRODUCT_KEYBOARD_MOUSE_SCOPE.md)。

### 7.2 Phone Audio Relay

- Plugin ID：`com.toolbox.audio-relay`
- Assembly：`AudioRelay.dll`
- 模式：InProcess
- Windows 最低 API：Windows 10 version 2004 / build 19041；
- 底层：`Windows.Media.Audio.AudioPlaybackConnection`；
- 路由：`ANDROID PHONE → BLUETOOTH A2DP → WINDOWS MIX`；
- 电脑原有应用声音继续由 Windows 正常混音，不被采集、替换或静音；
- 手机必须已在 Windows 中配对并启用媒体音频。

正常情况下支持热启停。若 WinRT/驱动资源无法安全释放，插件进入 `RestartRequired`，暂停普通音频操作并提供一次安全重启。不要通过隐藏插件、伪装 Disabled 或盲目重复加载来绕过该边界。

该功能强依赖手机、蓝牙适配器、驱动和 Windows 状态。自动化测试只能覆盖接口、假传输、平台探测和生命周期；真实配对、声音、延迟、音量及重连必须保留人工硬件回归。

详细说明：[`PHONE_AUDIO_RELAY.md`](PHONE_AUDIO_RELAY.md)。

## 8. Plugin API v1 与第三方开发现实

稳定 API 清单和兼容规则见 [`PLUGIN_API_V1.md`](PLUGIN_API_V1.md)。核心约束：

- `PluginContract.PluginApiMajor = 1`；
- 不删除、重命名或改变 v1 公共类型、接口成员、参数、返回类型、枚举值和 Manifest 字段语义；
- 不向已有 v1 接口追加必需成员；新能力使用新接口或新 API major；
- `ToolBox.PluginSdk.Experimental` 不属于稳定承诺；
- 旧版 `ToolBox.PluginSdk 0.0.1` 编译的 Fixture 必须继续能由当前共享 SDK 加载。

第三方开发当前可以参考 SDK 和包格式编译插件运行时，但还不能得到完整的“安装即显示”体验，原因是：

- Host 不会扫描未知 Plugin ID 并创建工作区；
- Host 页面仍由 Host 项目拥有；
- 没有公共 UI、命令或配置 Schema；
- 没有外部模板、独立 SDK 包、兼容矩阵和发布者签名流程。

若下一阶段目标是让别人开发插件，建议先完成“第三方插件启用”里程碑，再正式发布开发文档：

1. 定义通用插件发现和注册模型，不再依赖 `BuiltInPluginWorkspaceCatalog` 的固定 ID；
2. 优先设计 Host-owned 通用页面描述/命令模型，避免直接暴露 WPF 控件；
3. 明确第三方插件可用的设置、状态、资源和日志能力；
4. 建立独立的 Sample Plugin、脚手架、Manifest Schema 和打包 CLI/脚本；
5. 增加未知插件安装、导航、启停、升级、回退、卸载、崩溃和兼容性端到端测试；
6. 再决定是否发布 NuGet SDK、签名链和第三方分发政策。

在这些工作完成前，只能提供“API/包格式预览文档”，不能承诺可用的第三方开发者体验。

## 9. 开发环境与常用命令

要求：

- Windows 10/11 x64；
- .NET 8 SDK；
- PowerShell 5.1 或 PowerShell 7；
- Git；
- 音频物理测试需要蓝牙适配器和 Android 手机。

首次接手：

```powershell
git clone https://github.com/OQTQO/ToolBox.git
Set-Location ToolBox
git status --short --branch
dotnet --info
dotnet restore ToolBox.sln
dotnet build ToolBox.sln --configuration Release
dotnet test ToolBox.sln --configuration Release
dotnet run --project .\src\ToolBox.Host\ToolBox.Host.csproj
```

正式发布前只使用统一入口：

```powershell
.\tools\Invoke-ReleaseValidation.ps1 `
  -Version 0.1.1 `
  -Configuration Release `
  -OutputDirectory .\artifacts\release-validation
```

该脚本会：

1. 检查六个生产项目和两个 Manifest 的版本一致性；
2. 清理并执行 warnings-as-errors Release 构建；
3. 运行完整测试；
4. 发布 self-contained `win-x64` Host；
5. 生成两个确定性 `.tpk`；
6. 检查准确资产集合、包条目、身份、版本和 payload 哈希；
7. 生成并反向校验 Release SHA-256 清单。

单独打包：

```powershell
.\tools\New-KeyboardMousePackage.ps1 -Configuration Release -Version 0.1.1 -OutputDirectory .\artifacts
.\tools\New-AudioRelayPackage.ps1 -Configuration Release -Version 0.1.1 -OutputDirectory .\artifacts
```

`artifacts/`、`bin/`、`obj/` 都被 Git 忽略，不要提交构建输出。

## 10. 测试地图

当前 `86` 项测试分布：

- `ToolBox.Core.Tests`：55；
- `AudioRelay.Tests`：5；
- `ToolBox.Host.Tests`：26。

覆盖重点：

- Plugin API 公共面和旧 SDK 二进制兼容；
- InProcess/OutOfProcess 正常、崩溃、超时、泄漏和协议错误；
- Job Object 子进程清理；
- Resource/Service Lease；
- 安全 ZIP、攻击包、包身份、哈希和原子安装；
- 键鼠安装、输入、设置、冲突、升级、回退和卸载；
- 音频平台探测、假传输生命周期和 ALC 卸载；
- Host 设置迁移、插件三层状态、工作区投影、退出/重启和清理顺序；
- 发布资产和 checksum 的端到端验证。

不能由 CI 代替的人工验收：

- 窗口四角、最大化/还原图标、DPI、字体、滚动条和中英文布局；
- 托盘恢复、直接退出和重启体验；
- 真实 `.tpk` 安装/更新/打开/启停/卸载；
- Android 配对、设备发现、播放、手机音量、电脑自身声音、断开和重连。

## 11. 标准开发流程

每个里程碑必须遵循：

```text
明确范围和非目标
→ 从 main 创建 codex/<topic> 分支
→ 最小实现
→ Release build + 完整测试
→ 必要的 UI/硬件物理验收
→ 更新 CHANGELOG/PROJECT_CONTEXT
→ 写 PROJECT_RETROSPECTIVES 复盘
→ PR CI
→ 合并
→ main CI
→ 如需发布，再创建不可变 Tag
```

Definition of Done：

- 实现与用户确认范围一致；
- Release 构建 0 warning / 0 error；
- 完整测试通过；
- 平台相关功能有明确人工验收结论；
- 文档不夸大安全、兼容或第三方能力；
- `PROJECT_CONTEXT.md` 更新已验证状态和下一步；
- `PROJECT_RETROSPECTIVES.md` 记录失败、根因、遗漏原因、修复、证据和剩余风险；
- PR CI 和合并后 main CI 均通过。

## 12. 发布流程

不要直接在本地随意拼装 Release。推荐顺序：

1. 从最新 `main` 创建 release 分支；
2. 同步 Host/Core/Worker/PluginSdk、两个产品项目和两个 Manifest 的 SemVer；
3. 更新 README、CHANGELOG、包策略和产品文档中的版本示例；
4. 运行统一验证，最好连续运行两次并比较四项资产哈希；
5. 提交 PR，等待 PR CI；
6. 合并后等待 main CI；
7. 在准确的已验证合并提交上创建 annotated Tag，例如 `v0.2.0`；
8. 推送 Tag，让 `.github/workflows/release.yml` 创建/更新同名 Release；
9. 下载线上全部资产，独立核对数量、文件名、Host ProductVersion、TPK Manifest/package metadata、包条目和 SHA-256；
10. 更新项目上下文和发布复盘，但不要移动已经公开的旧 Tag。

CI 和 Release 工作流必须保持为 `Invoke-ReleaseValidation.ps1` 的薄适配器，不能复制另一套构建/打包逻辑。

## 13. 已知风险与技术债

按优先级排列：

1. **第三方插件能力缺口**：运行时平台比 Host 产品注册能力更通用；这是下一阶段最明确的架构/产品缺口。
2. **真实性与安全边界**：当前只有完整性校验，没有签名、权限或沙箱；陌生来源 `.tpk` 可能执行任意本地代码。
3. **Host 插件目录位置**：安装目录在 EXE 同级，可写权限取决于部署位置；正式安装器需要重新规划。
4. **WinRT/驱动生命周期**：音频热停用不是所有机器都能保证成功；`RestartRequired` 是设计边界，不是可吞掉的错误。
5. **Experimental 契约**：两个产品依赖的桥接契约不属于 Plugin API v1；不要让第三方无意依赖它们。
6. **Windows-only CI**：当前没有多 SDK/多 Windows 构建矩阵，也没有真实蓝牙硬件自动化。
7. **历史命名与文档体量**：正式插件仍位于 `spikes/`；`PROJECT_CONTEXT.md` 和复盘文档很长，后续可按版本归档，但不得丢失决策证据。
8. **发布工作流幂等性**：Release 步骤支持已有 Release 和覆盖资产，但公开 Tag 应视为不可变，不应靠强推修正版本。

## 14. 关键失败经验

完整记录见 [`PROJECT_RETROSPECTIVES.md`](PROJECT_RETROSPECTIVES.md)。接手者至少要记住：

- “本地有文件”不等于“Git checkout 有文件”；发布前要用干净检出验证；
- 同名旧 SDK Fixture 会受到 MSBuild 全局属性传播影响，历史版本测试不能随意统一版本；
- 只检查产物存在不够，必须检查包内容、身份、版本和 checksum；
- CI 与 Release 不应维护两套逻辑；
- 发布脚本必须从冷的 Windows PowerShell 5.1 进程运行，不能依赖已加载程序集或新 PowerShell API；
- NuGet audit 失败应先判断源服务与本地缓存，不要直接关闭安全检查；
- 生命周期状态必须描述真实资源状态；停止失败时不能隐藏入口或显示 Disabled；
- WPF 与 WinForms 同时启用时，要警惕 `Brush`、`KeyEventArgs` 等类型歧义；
- 测试的 Dispatcher 不能依赖不存在的真实消息循环；
- 性能/期限断言必须只包围它声称测量的操作，不能把 Worker 启动时间混入关闭期限；
- 软件验证、UI 验收和硬件验收是三类证据，不能互相替代；
- 每完成一个大节点必须回顾失败原因并写入项目记忆。

## 15. 推荐下一阶段

不要立即修改 Plugin API v1。建议先立项“第三方插件启用 v0.2 设计阶段”，交付物为：

1. 第三方插件用户故事、威胁模型和明确非目标；
2. 动态发现/注册模型及 Host-owned 通用 UI 方案；
3. Sample Plugin 和最小开发者工具链；
4. 安装、显示、启停、升级、卸载和故障隔离端到端验收；
5. 开发文档、Manifest Schema、兼容矩阵；
6. 是否引入签名、权限、沙箱和安装器的单独决策。

只有在上述路径真实跑通后，才适合给外部开发者发布“正式插件开发文档”。

## 16. GitHub 所有权转移清单

代码转移和 GitHub 仓库所有权转移是两件事。执行 GitHub Transfer 前：

- 确认接收方账号或 Organization 名称；
- 确认接收方有创建仓库权限并接受转移；
- 记录当前默认分支、Tags、Releases、Actions 和仓库可见性；
- 不把个人 PAT、浏览器会话或本机凭据写入仓库；
- 检查 Actions 仍允许 `GITHUB_TOKEN` 写 Release assets；
- 转移后更新本地 `origin`，检查 README/Release 链接和徽章；
- 验证 `main`、`v0.1.0`、`v0.1.1`、两个公开 Release 及资产仍存在；
- 在新所有者下运行一次无发布的 PR CI；
- 如要发布新版本，使用新 Tag，不移动旧 Tag。

转移命令示例仅在 GitHub 转移完成并获得准确新地址后执行：

```powershell
git remote set-url origin https://github.com/<new-owner>/ToolBox.git
git remote -v
git fetch origin --tags
git status --short --branch
```

不要在不知道准确接收方时猜测或提前修改 remote。

## 17. 接手者第一小时

1. 阅读本文、`PROJECT_CONTEXT.md`、`PROJECT_RETROSPECTIVES.md`；
2. 查看 `git status --short --branch`、`git log -5 --oneline --decorate` 和 `git remote -v`；
3. 从 GitHub 下载 v0.1.1，核对 SHA-256；
4. 运行 restore、Release build 和完整测试；
5. 运行一次 `Invoke-ReleaseValidation.ps1`，但不要推 Tag；
6. 启动 Host，安装两个官方 `.tpk`，检查中英文、托盘和插件三层状态；
7. 有 Android/蓝牙条件时执行音频物理回归；
8. 为下一目标写单独计划，不从历史 `spikes` 名称推断当前产品状态。

## 18. 文档索引

- [`README.md`](README.md)：项目入口和常用命令；
- [`CHANGELOG.md`](CHANGELOG.md)：版本变更；
- [`PROJECT_CONTEXT.md`](PROJECT_CONTEXT.md)：完整阶段状态和架构检查点；
- [`PROJECT_RETROSPECTIVES.md`](PROJECT_RETROSPECTIVES.md)：失败原因、修复和经验；
- [`PLUGIN_API_V1.md`](PLUGIN_API_V1.md)：稳定 Plugin API v1；
- [`PACKAGE_RELEASE_POLICY.md`](PACKAGE_RELEASE_POLICY.md)：TPK、完整性和发布边界；
- [`PRODUCT_KEYBOARD_MOUSE_SCOPE.md`](PRODUCT_KEYBOARD_MOUSE_SCOPE.md)：键鼠产品范围；
- [`PHONE_AUDIO_RELAY.md`](PHONE_AUDIO_RELAY.md)：音频产品原理、条件和边界；
- [`.github/workflows/ci.yml`](.github/workflows/ci.yml)：PR/main CI；
- [`.github/workflows/release.yml`](.github/workflows/release.yml)：Tag Release；
- [`tools/Invoke-ReleaseValidation.ps1`](tools/Invoke-ReleaseValidation.ps1)：唯一发布验证入口。

## 19. 最终交接结论

ToolBox 0.1 系列已经达到“可构建、可测试、可发布、两个官方插件可物理使用”的稳定检查点。接手者不需要继续修复 v0.1.1，也不应重发或移动现有 Tag。下一阶段应从新需求立项，其中第三方插件启用是最自然但也最需要先定契约和安全边界的方向。

任何新阶段结束时，必须同时更新项目上下文和复盘；没有验证证据、物理验收边界和书面教训，就不能把节点标记为完成。
