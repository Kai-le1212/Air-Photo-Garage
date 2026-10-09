<div align="center">

# Air Photo Garage

**飞机照片整理工具 —— 按注册号归档你拍过的每一架飞机**

Windows 桌面应用 · WinUI 3 / .NET 10 · 全程本地，不联网

![platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D4)
![.NET](https://img.shields.io/badge/.NET-10-512BD4)
![license](https://img.shields.io/badge/license-GPL--3.0-blue)

</div>

---

## 这是什么

一个给**航空摄影爱好者**用的本地照片管理工具。导入照片后自动读取 EXIF，
把机型、注册号、拍摄时间、机场等信息整理进一个可检索的数据库。

核心思路：**机型（如 A330-300）是共享的** —— 一次航展可能拍到十几架同型机，
光按机型分组没有意义；**只有注册号唯一标识一架具体的飞机**。
所以这个工具以**注册号为主线**来组织照片。

## 主要功能

- **照片墙** —— 按注册号自动聚组，支持关键词 / 机型 / 注册号 / 机场 / 日期区间筛选
- **三个浏览维度**
  - **机型**：机型 → 注册号 → 照片（先落到具体哪一架飞机）
  - **机场**：机场 → 按天时间轴，显示如「香港国际机场 · HKG」
  - **注册号**：注册号 → 时间轴（按天自动分组 **+** 自定义命名分组，如「2024 珠海航展」）
- **自动读 EXIF** —— 拍摄时间、相机厂牌/型号、镜头、焦距、光圈、快门、ISO、GPS
- **信息可改** —— 拍摄时间、机型、注册号、机场、相机与镜头参数、备注都能手动修正
- **机场三字段联动** —— IATA ↔ ICAO ↔ 机场名，内置机场目录自动补全

![照片墙](docs/screenshots/01-photo-wall.png)

## 安装

从 [Releases](../../releases) 下载 `AirPhotoGarage_X.Y.Z_x64.msix` 和 `devcert.cer`：

```powershell
# 1. 用管理员身份打开 PowerShell，先信任签名证书（仅首次需要）
certutil -addstore TrustedPeople devcert.cer

# 2. 安装
Add-AppxPackage -Path AirPhotoGarage_X.Y.Z_x64.msix
```

> 安装包用**自签名开发证书**签名（省掉付费代码签名证书）。
> 证书只用于标识发布者，不联网、不收集任何信息。

首次启动会让你先选**照片库位置**（默认 `%LocalAppData%\AirPhotoGarage`），之后可随时在设置里改。

## 数据存在哪

全部在你指定的库目录下，与安装包分离 —— **卸载应用不会删你的照片**。

| 内容 | 位置 |
|---|---|
| 数据库 | `<库目录>\library.db` |
| 原图 | `<库目录>\Photos\` |
| 缩略图 | `<库目录>\Thumbnails\` |

> 删除照片只从数据库移除记录，**磁盘上的原文件保留**，不会误删素材。

## 系统要求

- Windows 10 版本 1809（17763）或更高 / Windows 11
- x64
- 安装包已自带 Windows App SDK 运行时，**无需另外安装**

## 从源码构建

```bash
git clone https://github.com/Kai-le1212/Air-Photo-Garage.git
cd Air-Photo-Garage

./run.cmd              # 启动（Debug + 打包模式）
./run.cmd /unpackaged  # 非打包模式
./run.cmd /release     # Release 配置
./run.cmd /build       # 只编译不启动
```

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download)。详细构建与打包命令见 [CHANGELOG](CHANGELOG.md)。

## 已知限制

- **智能应用控制（SAC）**：Windows 11 的 SAC 会拦截「无法验证发布者」的应用。
  安装前执行上面的「信任签名证书」通常即可放行；仍被拦可临时关闭 SAC
  （单向操作，重新开启需重装系统，请自行权衡）
- **单文件 exe 在启用 SAC / WDAC 的机器上无法启动**，请使用 MSIX 安装包

## 许可

[GPL-3.0](LICENSE)

<div align="center">

*开始开发于 2026.10.07*

</div>
