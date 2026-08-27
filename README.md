# 幻杀工具箱

幻杀工具箱是一个基于 WinUI 3 / .NET 8 的 Windows 桌面制作工具箱，用于整理和制作卡牌、三国杀类武将、手牌和后续虚幻同步数据。

当前版本：`1.0.0`

## 当前功能

- 角色卡管理：创建角色、设置卡面、编辑英文代号 / 中文名 / 介绍 / 血量 / Stage（形态阶段）/ Tag / 携带技能组 / 携带牌。
- 手牌管理：创建手牌、设置卡面、编辑名称 / 介绍 / 花色 / 扑克数字 / 卡牌类型 / 函数组 / 使用次数 / 装备类型 / 数值 / 共鸣牌表达式。
- 标准卡片列表：角色和手牌均使用标准宽版卡牌比例，列表中以高密度小卡展示。
- 图片处理：导入卡面时进入裁剪弹窗，按角色卡面和手牌卡面的目标分辨率处理，并统一保存为 PNG。
- 右键菜单：支持重命名、复制、备份、打开文件夹、导出、删除等对象级操作。
- 整体设置：支持整体项目位置迁移、夜间模式、辅助显示、日志相关开关。
- 备份和回滚基础：删除、重命名、导出等重要操作会优先创建可恢复备份。
- F1 / F2：提供快捷键说明和整体项目信息辅助入口。
- 脚本入口：`Scripts\工具箱脚本菜单.ps1` 提供打包、发布正式版和发布 Beta 版的菜单选择。

## 技术栈

- Windows App SDK / WinUI 3
- .NET 8
- C# 12
- MVVM 分层：`Models`、`ViewModels`、`Services`、`Views`
- 普通 exe 发布方式：非 MSIX，`WindowsPackageType=None`

## 目录结构

```text
FantasyTools/
  Assets/          默认图标、默认卡面和应用资源
  Converters/      XAML 绑定转换器
  Models/          数据模型
  Services/        设置、日志、工作区、弹窗、迁移等底层服务
  Styles/          工具箱通用样式
  ViewModels/      页面和对象的状态与命令
  Views/           弹窗内容工厂等 UI 片段
  Scripts/
    工具箱脚本菜单.ps1  交互式脚本入口
    打包工具箱.ps1      打包核心脚本
    发布新版本.ps1      发布核心脚本
    热更新覆盖.ps1      程序热更新覆盖脚本
```

## 开发环境

建议环境：

- Windows 10 1809 或更高版本
- Visual Studio 2022
- .NET 8 SDK
- Windows App SDK 2.2 对应依赖

命令行构建：

```powershell
dotnet build .\FantasyTools.csproj --configuration Release --runtime win-x64 -p:Platform=x64
```

## 本地运行

构建后运行：

```powershell
.\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\幻杀工具箱.exe
```

工具箱默认会在 D 盘创建整体项目目录：

```text
D:\幻杀工具箱项目
```

用户制作数据、日志、备份等运行期文件默认放在整体项目目录中，不应提交到源码仓库。

## 打包发布

推荐使用 `Scripts` 文件夹内的菜单脚本：

```powershell
.\Scripts\工具箱脚本菜单.ps1
```

默认输出：

```text
D:\DabaoV\幻杀工具箱\
  幻杀工具箱.lnk
  幻杀工具箱.exe
  幻杀工具箱.pri
  App.xbf
  MainWindow.xbf
  Assets\
  Scripts\
  ...
```

常用参数：

```powershell
.\Scripts\打包工具箱.ps1 -Configuration Release -Runtime win-x64
.\Scripts\打包工具箱.ps1 -Clean
.\Scripts\打包工具箱.ps1 -OutputRoot "D:\DabaoV"
.\Scripts\打包工具箱.ps1 -Version 1.0.1
.\Scripts\发布新版本.ps1 -Version 1.0.1 -Runtime win-x64
.\Scripts\发布新版本.ps1 -Version 1.0.1-beta.1 -Runtime win-x64 -Prerelease
.\Scripts\工具箱脚本菜单.ps1 -Action ReleaseStable -Version 1.0.1
.\Scripts\工具箱脚本菜单.ps1 -Action ReleaseBeta -Version 1.0.1-beta.1
```

当 `打包工具箱.ps1` 或发布脚本传入 `-Version` 时，新版本号必须大于 `FantasyTools.csproj` 当前版本；脚本会同步更新 `Version`、`AssemblyVersion`、`FileVersion` 和 `InformationalVersion`。不传 `-Version` 时仅按当前版本重新打包。

发布 GitHub Release 前可编辑 `Scripts\新版本介绍.txt`，发布脚本会把该文件内容作为 Release 介绍。

## 交付包说明

当前阶段已验证的交付包：

```text
D:\DabaoV\幻杀工具箱
D:\DabaoV\ReleaseAssets\FantasyTools-v1.0.1-win-x64.zip
```

交付给别人时优先发送压缩包。解压后建议从外层的 `幻杀工具箱.lnk` 启动。

## 仓库规则

可以提交：

- 源码文件：`.cs`、`.xaml`、`.csproj`、`.sln`
- 默认资源：`Assets`
- 脚本入口：`Scripts\工具箱脚本菜单.ps1`
- 脚本核心：`Scripts\打包工具箱.ps1`、`Scripts\发布新版本.ps1`、`Scripts\热更新覆盖.ps1`
- 文档：`README.md`

不要提交：

- `bin/`
- `obj/`
- `.vs/`
- `.idea/`
- `publish/`
- `artifacts/`
- 用户制作数据目录
- 临时日志、备份和导出包

## 版本

当前版本为 `1.0.0`，版本号以 `FantasyTools.csproj` 为准。更新版本时需要同步检查顶栏、关于页、manifest 和打包输出目录名。
