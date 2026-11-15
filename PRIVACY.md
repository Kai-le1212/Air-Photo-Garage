# Privacy Policy

Air Photo Garage is a fully offline Windows desktop application. It does not collect,
transmit, or share any personal data.

## What the app stores

All data stays on your own computer, under your user profile:

- `%LOCALAPPDATA%\AirPhotoGarage\library.db` — the photo library database (file paths,
  photo metadata read from your own image files, and any information you type in yourself)
- `%LOCALAPPDATA%\AirPhotoGarage\Photos\` — copies of imported photos
- `%LOCALAPPDATA%\AirPhotoGarage\Thumbnails\` — generated thumbnails
- `%LOCALAPPDATA%\AirPhotoGarage\covers.json` and `settings.txt` — your own preferences

## Network access

The application does not send any data anywhere. The only outbound requests are made
**at your explicit request**: when you click "View on Flightradar24", the default browser
is opened with a URL containing the registration number you already stored locally.

## Telemetry and analytics

None. There is no telemetry, no crash reporting, no advertising, and no third-party SDK
that collects usage data.

## Permissions

The app only reads and writes files inside your own user profile and the photo folders
you explicitly choose. No administrator rights are required.

## Contact

Questions about this policy: please open an issue at
<https://github.com/Kai-le1212/Air-Photo-Garage/issues>.

---

# 隐私政策（中文）

Air Photo Garage 是一个**完全离线**的 Windows 桌面应用，不收集、不上传、不共享任何个人数据。

所有数据只保存在你自己的电脑上（`%LOCALAPPDATA%\AirPhotoGarage\`），
包括照片库数据库、导入的照片副本、缩略图与你自己的偏好设置。

应用不会主动联网；只有在**你点击「在 Flightradar24 查看」**时，才会用默认浏览器打开
一个包含本地已有注册号的网址。

没有遥测、没有崩溃上报、没有广告、没有第三方统计 SDK；不需要管理员权限。
