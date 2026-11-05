# Gargantua 黑洞桌宠

以《星际穿越》黑洞视角为灵感的 Windows 桌宠，使用 C#、Windows Forms 和 Win32 API 绘制。

## 下载运行

下载仓库中的 [Gargantua.exe](https://github.com/tmqdtc/gargantua-desktop-pet/raw/refs/heads/main/Gargantua.exe)，双击即可运行。分享时只需发送 EXE。

当前版本在 Windows 11 多屏环境下测试，运行依赖 .NET Framework 4.x 和 Windows 桌面合成。EXE 尚未数字签名，下载后可能出现 Windows SmartScreen 提示。

## 功能与操作

- 带有气体纹理转动的金色吸积盘。
- 在桌面漂移，对下方实时画面产生局部引力透镜效果。
- 生长到设定尺寸后，将屏幕旋转撕裂为光丝并吸入，随后缩小复位。
- 左键按住黑洞可以拖动，支持跨屏拖放。
- 按 F7 在已连接的屏幕之间循环切换。
- 右键黑洞或托盘图标打开菜单。菜单打开时暂停漂移与生长计时，点击菜单外关闭。
- 设置支持独立开关引力透镜，选择生长吞屏或固定大小模式。
- 可以调整最小尺寸、最大尺寸、循环总时长和漂移速度，并恢复默认。

漂移速度的 100% 为默认速度，0% 停止自动漂移。设置保存在当前 Windows 用户配置中。选择托盘菜单的「退出黑洞」可结束程序。

屏幕画面在内存中处理，用于透镜和吞屏渲染。采样使用放大镜控件的窗口过滤列表排除桌宠自身，桌宠窗口可被桌面捕获录制。

## 源码构建

仓库包含 `Gargantua.cs`、`Gargantua.ico` 和预编译的 `Gargantua.exe`。

退出正在运行的桌宠，在项目目录打开 PowerShell，使用 Windows 自带的 .NET Framework C# 编译器构建。

```powershell
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $csc /nologo /target:winexe /platform:anycpu /optimize+ /reference:System.Drawing.dll,System.Windows.Forms.dll /win32icon:Gargantua.ico /out:Gargantua.exe Gargantua.cs
```

生成的是独立图形程序，启动时无需保留 CMD 或 PowerShell 窗口。
