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

### 智能应用控制（SAC）会拦截本应用

Windows 11 的**智能应用控制**只放行两类应用：能被微软云服务判定为安全的，
或**由「微软信任根计划（Trusted Root Program）」中的 CA 签发证书**签名的。

本项目的自签名证书**不满足这个条件** —— 即使把它装进「受信任的根证书颁发机构」，
MSIX **能安装成功，但启动时仍会被拦**：

> 智能应用控制已阻止可能不安全的应用 —— 无法验证其发布者

而且 **SAC 没有针对单个应用的放行白名单**（微软官方明确说明，找不到"放行按钮"）。
所以只有两条路：

1. **关闭 SAC** —— `Windows 安全中心 → 应用和浏览器控制 → 智能应用控制 → 关闭`。
   较新的 Windows 版本已支持随时重新开启，不再需要重装系统
2. **购买正规代码签名证书**（来自微软信任根计划的 CA），用它给 MSIX 重新签名

> SAC 只对**部分机器**强制启用 —— 全新安装的 Windows 11 可能默认开启，
> 而开发机、企业受管设备通常会**自动关闭**。未启用 SAC 的机器不受影响，
> 上面的「信任证书」步骤装完即可正常运行。

### WDAC / VBS 代码完整性策略

如果机器启用了 WDAC，未签名的原生 DLL（如 `Microsoft.WinUI.dll`）会被拒绝加载。
这种情况下**只有 MSIX 能运行，单文件 exe 无法启动**。

## 许可

[GPL-3.0](LICENSE)

<div align="center">

*开始开发于 2026.10.07*

</div>
