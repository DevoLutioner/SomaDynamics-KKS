# MMD DynamicBone Stabilizer（KKS 版）

面向 Koikatsu Sunshine（KKS）CharaStudio 的零调参兼容插件，用于降低人物运动时
头发、衣摆和饰品 DynamicBone 的细碎抖动、阶梯运动及切换动作时的爆甩。

它是 Soma Dynamics KKS 的推荐 MMD 配套插件，但也可以独立使用。Soma Dynamics
负责形体物理，本插件负责头发、衣服、饰品与裙骨碰撞稳定，两者不会重复接管同一对象。

本仓库只构建 KKS 版。Koikatu（KK）版维护在
<https://github.com/DevoLutioner/MmdDynamicBoneStabilizer>。两版共用同一份源码，
仅 CharaAnime 依赖 GUID 不同（KKS 为 `Countd360.CharaAnime.KKS`）。

## 工作原理

- 自动发现工作室角色，以身体根、胯、头部三个稳定锚点检测人物运动；
- 工作室动画、IK/节点移动及 MMD 都会触发稳定，不再要求 MMD 正在播放；
- 等 MMD Director 在 `LateUpdate` 写完角色姿势后，再运行该角色的
  `DynamicBone` 与 `DynamicBone_Ver02`；普通人物运动保留游戏原有解算顺序，避免重复计算；
- 在人物运动稳定期间恢复 `Dynamic Bones Fix 22.1` 屏蔽的跳帧补偿；
- 自动识别 Unity `Cloth` 衣物，降低 MMD 实时姿势的世界速度/加速度尖峰，
  启用连续碰撞，并在播放或跳转时清除旧的变换速度；
- 对裙骨引用的 KKPE 球形碰撞体增加运动稳定：裙骨逐帧求解，球体临时
  缩小安全余量，人物停止运动后恢复作者参数；
- 播放刚开始自动清除旧物理速度；切歌或拖动进度时只观察身体根、胯、头部
  三个稳定锚点，绝不再用头发和衣摆物理骨自身判断跳转；
- 不改卡片参数；静止角色在短暂保持期结束后恢复作者原始物理参数。

## 安装

将 `MmdDynamicBoneStabilizer.dll` 放入：

`BepInEx/plugins/MmdDynamicBoneStabilizer/`

依赖：

- BepInEx 5；
- HarmonyX；
- MMD Director 自带的 CharaAnime 2.8+（KKS 版，GUID `Countd360.CharaAnime.KKS`）。

重启 CharaStudio 后生效。无需打开界面或调整参数。安装目录：
`BepInEx/plugins/MmdDynamicBoneStabilizer/`。

## 构建

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Build-Release.ps1
```

默认游戏根目录为 `H:\KKS\KKS`（本地沙盒），可用 `-GameRoot` 参数或环境变量
`KKS_BUILD_GAME_ROOT` 覆盖。产物输出到
`release\MmdDynamicBoneStabilizer-KKS-v<版本>` 与同名 zip。

## 默认配置

配置文件首次运行后生成在：

`BepInEx/config/codex.koikatu.mmddynamicbonestabilizer.cfg`

日常使用保持默认即可。出现特殊动作在切换时大幅甩动，才需要调低
`Seek reset distance` 或 `Seek reset angle`。

运动检测默认只忽略小于 0.5 毫米或 0.1 度的单帧浮点噪声。首次检测到运动后会
持续保持稳定，避免大量 DynamicBone、裙骨和碰撞体参数反复切换。若确实需要静止后
恢复，可关闭 `Continuous stabilization after motion`，再通过 `Hold frames` 设置延迟；
`Position threshold` 和 `Rotation threshold` 用于调整检测灵敏度。

## 快速确认

在 `BepInEx/LogOutput.log` 中搜索：

`Character motion stabilization active` 或 `MMD DynamicBone stabilization active`

日志会列出当前角色检测到的普通 DynamicBone、Ver02、裙骨链和球形碰撞体数量。

## 兼容性说明

- 可以和 `KK_Fix_DynamicBones.dll` 共存；本插件只为当前正在运动或播放 MMD 的角色
  补回跳帧处理，不移除它的其他修复。
- 可以和 Dynamic Bone Editor、KKPE、Cloth Colliders Support 共存。
- Soma Dynamics、FPC、Breast Physics Controller 不是本插件的处理目标。若同时使用
  Soma Dynamics，请按其教程停用 BPC，避免胸臀参数被两个形体控制器重复覆盖。

## 下载与诊断

- 项目主页：<https://github.com/DevoLutioner/MmdDynamicBoneStabilizer>
- 发布页面：<https://github.com/DevoLutioner/MmdDynamicBoneStabilizer/releases>
- 完整组合教程：<https://github.com/DevoLutioner/SomaDynamics/blob/main/docs/USER_GUIDE.zh-CN.md>

报告问题时请附上 `BepInEx/LogOutput.log` 中从插件加载到
`MMD DynamicBone stabilization active` 的相关行，并说明异常对象属于头发、DynamicBone
衣物、Unity Cloth，还是 KKPE 裙骨碰撞体。
