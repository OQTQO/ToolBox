# Phone Audio Relay

Phone Audio Relay 是 ToolBox 的 Product 02 插件。它把 Windows 作为蓝牙 A2DP 接收端：Android 手机通过蓝牙发送媒体音频，Windows 将手机流播放到当前系统音频输出；电脑原有应用声音继续走正常的 Windows 混音，插件不会采集、替换或静音电脑声音。

底层使用 Microsoft 的 [`Windows.Media.Audio.AudioPlaybackConnection`](https://learn.microsoft.com/windows/apps/develop/media-playback/enable-remote-audio-playback) API。该 API 从 Windows 10 version 2004（build 19041）开始提供。

## 使用条件

- Windows 10 version 2004（build 19041）或更高版本；
- 电脑有可用的蓝牙适配器和驱动；
- Android 手机已经在 Windows“设置 → 蓝牙和设备”中完成配对；
- ToolBox Host 在接收期间保持运行；
- 手机使用“媒体音频”输出，而不是通话音频。

## 构建和安装

```powershell
dotnet build ToolBox.sln --configuration Release
powershell -ExecutionPolicy Bypass -File .\tools\New-AudioRelayPackage.ps1 `
  -Configuration Release `
  -Version 0.1.1 `
  -OutputDirectory .\artifacts
dotnet run --project .\src\ToolBox.Host\ToolBox.Host.csproj
```

在 Host 的 `Phone Audio Relay` 卡片中：

1. 点击 `Install .tpk`，选择 `artifacts\PhoneAudioRelay-0.1.1.tpk`；
2. 点击 `Enable relay`；
3. 点击 `Refresh paired phones`，从列表选择手机；
4. 点击 `Start receiving`，然后在 Android 手机上播放媒体；
5. 结束时点击 `Stop receiving`，或直接禁用插件。

如果列表为空，先确认手机仍在 Windows 中配对、蓝牙已开启，再刷新。若手机主动断开，卡片会回到 `Ready`，可再次连接。

## 音频边界

- 手机媒体流和电脑应用声音在 Windows 的正常输出混音中并存；
- 手机音量键仍控制手机发送端音量，Windows 主音量控制最终电脑输出；
- 插件一次只占用一个 A2DP 接收资源；
- 当前版本不处理电话/HFP、手机麦克风、电脑录音、逐应用音量或后台自启动；
- 蓝牙编解码、延迟和音质由手机、适配器、驱动与 Windows 协商决定。

## 包内容

生成的 `.tpk` 包含 Manifest、`AudioRelay.dll`、`AudioRelay.deps.json`、Microsoft Windows SDK 的 WinRT 运行时投影依赖和 SHA-256 文件清单，不携带私有 `ToolBox.PluginSdk.dll`。安装器仍会执行路径、大小、压缩比、Manifest/API/平台和哈希校验。
