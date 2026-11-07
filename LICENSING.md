# 许可证范围 / Licensing

本仓库包含原创代码和基于 GPL-2.0 上游源码构建的下载引擎。不同部分的授权范围如下；不能把整个仓库或整合程序统一标为 MIT。

## 原创部分：MIT

Copyright (c) 2026 binbinku。以下原创内容依照 [LICENSE-MIT](LICENSE-MIT) 授权：

| 范围 | 内容 |
| --- | --- |
| `src/BKDayzModDownloader/` | WPF 界面、队列管理与引擎通信代码 |
| `src/BKDownloader.Core/` | 通用模型、配置、清单解析和目录处理 |
| `tests/BKDownloader.Tests/` | 原创离线验证代码 |
| 项目根目录原创文件 | README、本文、示例清单、构建与启动脚本、项目配置 |

上述三个代码目录内各附有 MIT `LICENSE`。MIT 允许使用、修改、再分发及商业使用，但须保留版权和许可声明。该授权仅适用于项目原创内容，不授予第三方代码、依赖、商标或模组内容的额外权利。各许可证文本及第三方声明所引用的内容保留其原有授权。

## 下载引擎与整合分发：GPL-2.0

以下内容继续依照根目录 [LICENSE](LICENSE) 中的 GNU General Public License version 2 授权：

- `vendor/DepotDownloader/`：SteamRE Team 及贡献者的上游源码，以及本项目对其所作修改；保留原版权和许可声明。
- `src/BKDownloader.Engine/`：与上述源码共同编译的下载引擎入口及适配代码。
- 当前包含 GPL 下载引擎的 BK DayZ Mod Downloader 整合分发包。

MIT 原创部分可以在其许可范围内独立复用；将其纳入当前 GPL 引擎及整合程序时，MIT 不替换 GPL 要求。下载引擎使用独立进程并不改变其内部编译使用 DepotDownloader 源码这一事实。

再分发当前整合程序时，须遵守 GPL-2.0，包括保留许可证与版权声明，并按其要求提供完整对应源码。项目的 `publish.ps1` 会将对应源码打包到 `Source/BKDayzModDownloader-source.zip`，同时附带构建脚本、许可证和第三方声明。修改程序后，应随发布版本重新生成对应源码包。

## 第三方依赖

运行时库与上游组件适用各自许可证。参见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)、上游源码中的声明，以及发布包 `Licenses` 目录内的 NuGet 许可资料。MIT 授权不覆盖这些组件。

## English summary

Original code in `src/BKDayzModDownloader/`, `src/BKDownloader.Core/`, and `tests/BKDownloader.Tests/`, together with original root-level documentation, scripts and configuration, is licensed under the MIT License in `LICENSE-MIT`.

`vendor/DepotDownloader/`, its modifications, and `src/BKDownloader.Engine/` remain under GPL-2.0. The current combined application is distributed under GPL-2.0, with the original MIT grants preserved for the identified components. This is not an MIT-only application. Third-party components retain their own licenses. Binary distributions include the corresponding source archive, license texts and third-party notices.
