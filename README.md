# SomaDynamics-KKS

Koikatsu Sunshine（KKS）专用插件合集，与 Koikatu（KK）版同名插件共用功能与角色卡
数据格式，各自独立仓库维护：

| 目录 | 插件 | KK 版仓库 |
| --- | --- | --- |
| [`SomaDynamics/`](SomaDynamics/) | Soma Dynamics v1.0.3.5（形体动力学控制器，ThighPhysicsController 的 KKS 版） | [DevoLutioner/SomaDynamics](https://github.com/DevoLutioner/SomaDynamics) |
| [`MmdDynamicBoneStabilizer/`](MmdDynamicBoneStabilizer/) | MMD DynamicBone Stabilizer v1.2.2（MMD 头发/衣摆/饰品稳定器，KKS 版） | [DevoLutioner/MmdDynamicBoneStabilizer](https://github.com/DevoLutioner/MmdDynamicBoneStabilizer) |

## 下载安装

从 [Releases](../../releases) 下载对应 zip：

- `SomaDynamics_KKS_1.0.3.5.zip`（附 `.sha256`）
- `MmdDynamicBoneStabilizer-KKS-v1.2.2.zip`（附 `.sha256`）

将压缩包内的 `BepInEx` 合并到 KKS 游戏根目录（包含 `KoikatsuSunshine.exe` 与
`CharaStudio.exe` 的目录）。依赖常用 KKS 插件库自带的：

- Soma：`KKSAPI.dll`、`KKS_BepisPlugins\KKS_ExtensibleSaveFormat.dll`
- MMD 稳定器：MMD Director 自带的 CharaAnime 2.8+（GUID `Countd360.CharaAnime.KKS`）

## 沙盒验证记录（2026-08-13）

本地沙盒 `H:\KKS\KKS` 实测：

- 主游戏 `KoikatsuSunshine.exe`：Soma 加载、初始化、Harmony 补丁安装正常；
- `CharaStudio.exe`：Soma 场景钩子安装正常；MMD 稳定器加载正常，无缺失依赖错误。

```text
Loading [Soma Dynamics 1.0.3.5]
Soma Dynamics initialized (...)
Native breast and Studio pose-change patches installed.
MMD DynamicBone Stabilizer v1.2.2 loaded (automatic, no tuning required).
```

## 构建

两个插件分别构建，默认游戏根目录 `H:\KKS\KKS`，可用 `-GameRoot` 或环境变量
`KKS_BUILD_GAME_ROOT` 覆盖：

```powershell
# Soma Dynamics KKS
powershell -NoProfile -ExecutionPolicy Bypass -File .\SomaDynamics\tools\Build-ThighPhysicsController.ps1

# MMD DynamicBone Stabilizer KKS
powershell -NoProfile -ExecutionPolicy Bypass -File .\MmdDynamicBoneStabilizer\Build-Release.ps1
```

- Soma 目标框架 net471，引用 `KoikatsuSunshine_Data\Managed` 与 KKSAPI；
- MMD 稳定器目标框架 net48（与 KKS CharaAnime 程序集目标一致），引用
  `CharaStudio_Data\Managed`。

## 更新日志

- Soma：[`SomaDynamics/CHANGELOG.md`](SomaDynamics/CHANGELOG.md)
- MMD 稳定器：[`MmdDynamicBoneStabilizer/CHANGELOG.md`](MmdDynamicBoneStabilizer/CHANGELOG.md)
