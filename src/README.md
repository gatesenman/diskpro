# DiskPro C盘清理大师

专业级 Windows 10 / 11 C 盘空间清理工具，原生 WPF（.NET 8）界面，全中文，支持深色 / 浅色主题。

> 原则：**先预览、后确认、可回收、有日志**。所有删除都会先列出清单与大小，需要你确认；默认移入回收站；系统关键目录受硬性保护。

---

## 下载与运行

| 文件 | 说明 |
|------|------|
| `release/DiskPro.exe` | 单文件绿色版，自带 .NET 运行时，双击即可运行 |
| `release/DiskPro-win-x64.zip` | 同上，压缩包形式（含本 README） |

- 系统要求：Windows 10 1809+ / Windows 11，x64。
- 程序清单声明 `requireAdministrator`，启动时会弹出 UAC 提示，请选择“是”。
- 首次启动单文件程序需要解压运行时到临时目录，可能需要几秒。
- 程序未做代码签名，SmartScreen 可能提示“未知发布者”，点击“更多信息 → 仍要运行”。

## 功能模块

| 模块 | 说明 |
|------|------|
| **总览仪表盘** | C 盘容量 / 已用 / 可用；“智能扫描”汇总各类可回收空间（垃圾、休眠文件、Windows.old），点击条目跳转到对应模块 |
| **系统垃圾清理** | 用户 / Windows Temp、Windows Update 下载缓存、传递优化缓存、缩略图 / 图标缓存、错误报告 & 崩溃转储、旧日志（>7 天）、回收站、浏览器缓存（Chrome / Edge / Firefox **仅缓存**，不碰 Cookie / 密码 / 历史）、npm / pip / NuGet / Yarn 缓存，可选微信 / QQ 缓存。按项勾选，显示大小与风险等级 |
| **大文件扫描** | 按顶层目录多线程并行遍历，Top-N + 最小大小 + 类型过滤（视频、压缩包、镜像、安装包、文档、日志），可定位、删除 |
| **空间分析** | 文件夹大小树（可调深度，懒加载展开）+ 矩形树图（Squarified Treemap），点击方块下钻、返回上一级 |
| **重复文件查找** | 大小分组 → 首 64KB 部分哈希 → 完整 SHA-256 三级比对；按组显示；一键“保留最新 / 保留最旧”；跳过系统目录；不允许删除整组所有副本 |
| **软件卸载** | 读取 HKLM / HKCU 的 Uninstall 键（含 WOW6432Node），显示大小、发布者、安装日期，支持搜索排序；调用程序自带卸载器；卸载后扫描 Program Files / AppData / ProgramData 残留文件夹并提示注册表残留位置 |
| **系统深度优化** | DISM `/StartComponentCleanup`；休眠开关（`powercfg -h off/on`，显示 hiberfil.sys 大小）；虚拟内存信息；Windows.old 清理（调用系统磁盘清理）；第三方旧驱动包（`pnputil /enum-drivers` / `/delete-driver`）；还原点占用（`vssadmin list shadowstorage`） |
| **文件夹搬家** | 桌面 / 文档 / 下载 / 图片 / 视频 / 音乐迁移到其他盘：系统已知文件夹重定向（`SHSetKnownFolderPath`）或目录联接（Junction）；记录保存在 `%LOCALAPPDATA%\DiskPro\moves.json`，可一键回滚 |
| **启动项管理** | 列出 HKCU / HKLM Run、RunOnce 与启动文件夹中的项目；禁用方式与任务管理器一致（`StartupApproved`），启动文件夹项改名为 `.disabled`，均可恢复 |
| **设置** | 仅预览（Dry-run）模式、默认回收站、可选 IM 缓存、深色主题；查看受保护路径列表 |
| **操作日志** | 所有扫描、删除、跳过原因、优化命令均写入 `%LOCALAPPDATA%\DiskPro\logs\diskpro-YYYYMMDD.log`，界面中可查看 |

## 安全机制

1. **硬性保护白名单**（`SafetyGuard`）：`Windows\System32`、`SysWOW64`、`WinSxS`、`servicing`、`Boot`、`Fonts`、`Installer`、`Recovery`、`System Volume Information`、`pagefile.sys`、`hiberfil.sys`、`Program Files\WindowsApps` 等目录树内任何文件都不可删除；磁盘根目录、`Windows`、`Program Files`、`ProgramData`、`Users`、用户目录、桌面、文档等根目录本身不可删除。WinSxS 只能通过 DISM 官方命令清理。
2. **范围约束**：每次删除都携带“允许范围”，超出所选扫描范围的路径一律拒绝。路径会先规范化（处理 `..`），并区分 `System32` 与 `System32Backup` 这类前缀相似目录。
3. **不跟随链接**：遇到 Junction / 符号链接只移除链接本身，绝不删除其指向的内容。
4. **占用检测**：被其他程序占用的文件会跳过，并在结果中给出原因。
5. **确认 + 预览**：删除前弹出清单（路径、大小、说明），可选“移至回收站”或“永久删除”。开启“仅预览”模式后，所有操作只写日志。
6. **回收站优先**：默认使用 `SHFileOperation(FOF_ALLOWUNDO)` 移入回收站。

## 使用建议

1. 首次使用建议先在 **设置** 中开启“仅预览模式”，运行一遍熟悉流程，再关闭。
2. 在 **总览仪表盘** 点击“智能扫描”，查看可回收空间分布。
3. 进入 **系统垃圾清理**，勾选项目 → “清理所选” → 核对清单 → 确认。
4. 对于大文件、重复文件，务必逐项确认是否仍需要。
5. DISM 组件清理、关闭休眠等操作属于系统级变更，请阅读提示后再执行。

## 从源码构建

```bash
# Linux / macOS / Windows 均可交叉编译
dotnet test tests/DiskPro.Tests          # 运行单元测试
dotnet publish src/DiskPro.App/DiskPro.App.csproj -c Release -r win-x64 \
  --self-contained true -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true \
  -p:EnableWindowsTargeting=true -o publish/win-x64
```

或直接运行 `./build.sh`。

### 项目结构

```
src/DiskPro.Core     核心库（net8.0，无 UI 依赖）：扫描器、去重、安全规则、删除器、卸载、优化、搬家、启动项
src/DiskPro.App      WPF 界面（MVVM：ViewModels / Views / Themes / Services）
tests/DiskPro.Tests  xUnit 单元测试（可在 Linux 上运行）
```

## 已知限制

- 界面在 Linux 上交叉编译，需在真实 Windows 上做最终验收。
- 浏览器缓存清理前请先关闭浏览器，否则被占用的缓存文件会被跳过。
- Windows.old 清理调用系统“磁盘清理”（cleanmgr），不自行强删。
- 还原点占用以 `vssadmin` 原始输出显示，未做解析。
- 卸载残留只扫描文件夹；注册表残留只给出位置提示，不自动删除。
- 程序未签名。
