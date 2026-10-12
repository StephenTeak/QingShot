# 轻截图

独立 Windows 截图工具，使用微信 Windows 版常用的 **Alt+A** 全局快捷键。C# / WinForms 实现，没有第三方应用依赖，截图只在本机处理。

## 使用

从 [GitHub Releases](https://github.com/StephenTeak/QingShot/releases/latest) 下载 `QingShot-v1.0.0-win-x64.zip`，解压后双击 `QingShot.exe`；也可直接下载单文件 EXE。无需安装 .NET，适用于 Windows 10/11 x64。

程序常驻系统托盘。按 **Alt+A** 或双击绿色托盘图标开始截图。右键托盘图标可重新注册快捷键、查看帮助或退出。

拖动鼠标框选区域，使用边框上的控制点调整大小；选择“选择”工具可移动选区。支持矩形、椭圆、箭头、画笔、文字和区域马赛克，可选择颜色及粗细。

| 操作 | 快捷键 |
| --- | --- |
| 开始截图（全局） | Alt+A |
| 复制图片并完成 | Enter / Ctrl+C |
| 复制图片并完成（选择工具） | 双击选区 |
| 保存 PNG 并完成 | Ctrl+S |
| 撤销上一条标注 | Ctrl+Z |
| 取消截图 | Esc / 鼠标右键 |
| 确认正在输入的文字 | Enter |
| 文字换行 | Shift+Enter |
| 放弃正在输入的文字 | Esc |

输入文字时，Ctrl+C、Ctrl+Z 保留文本框原有行为。保存对话框取消后可继续编辑，保存失败会保留截图。

**快捷键冲突：** Windows 不允许两个程序同时注册同一全局快捷键。如果微信或其他软件已占用 Alt+A，轻截图会提示失败。先在对方软件中修改/释放该快捷键，再从轻截图托盘菜单选择“重新注册 Alt+A”。程序不会自动换成其他快捷键。

![轻截图编辑界面（合成桌面示例）](docs/editor-preview.png)

## 开发与构建

Windows 10/11 x64，开发需要 .NET 9 SDK。

```powershell
dotnet build -c Release
dotnet run -c Release -- --capture
dotnet run -c Release -- --self-test
Get-Content bin/Release/net9.0-windows/self-test.txt
```

自检覆盖反向框选、边界限制、裁剪像素、标注坐标、马赛克、PNG 保存/覆盖、负坐标屏幕原点、框选/移动/调整/撤销/文字交互处理，以及原生热键注册/冲突/重试/释放。`verification/` 中生成合成桌面的编辑界面和导出预览，不读取用户屏幕。

生成包含运行时的便携单文件 EXE（目标电脑无需安装 .NET）：

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -o dist -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false
```

## 实现范围

- 冻结整个虚拟桌面后框选，支持跨显示器选区；采用 Per-Monitor V2 DPI 感知和物理像素坐标。
- 标注跟随桌面内容坐标，移动选区改变裁剪范围。
- 导出的图片不含遮罩、边框或工具栏；复制后的图片可直接粘贴到聊天或文档中。
- 不包含 OCR、滚动长截图、录屏、窗口自动吸附或开机自启。
- 受系统保护的窗口、DRM 视频和安全桌面可能无法捕获。

源文件：`Program.cs`（托盘和热键）、`CaptureForm.cs`（截图编辑界面）、`Annotation.cs`（标注和裁剪）、`SelfTest.cs`（自检）。

## 当前验证结果

2026-10-13：Release 编译通过（0 警告、0 错误），便携 EXE 自检通过，包括 Win32 Alt+A 注册、快捷键占用检测和释放。已检查合成桌面的工具栏/导出图片渲染。

当前环境的 Windows 界面控制组件无法连接，因此尚未完成真实鼠标键盘操作、剪贴板粘贴和多显示器混合 DPI 的端到端实操验证。交互自检直接调用程序的鼠标/键盘处理方法；剪贴板自检检查图片数据格式。
