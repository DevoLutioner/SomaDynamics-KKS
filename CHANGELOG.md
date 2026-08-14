# 更新日志（CHANGELOG）

## 2026-08-14：Soma Dynamics KKS v1.0.3.5

- 汇总自 v1.0.2.7 以来的预设、默认开关、Timeline/自由 H、安全性和性能修复。
- 内置“高”档精确采用用户 `MyPreset1.xml`，保留当前启用状态与求解模式。
- 最终修复 PushUp 协调：按 BPC 的左右胸提交顺序补回 `ReSetupDynamicBoneBust(0)`，并在
  PushUp 写完 4–13 号胸型参数后只刷新胸部基准，不再牵动手臂、腹部和大腿。

## 2026-08-14：Soma Dynamics KKS v1.0.3.4

- 修正 PushUp 协调：不再重采手臂/腹部，也不使用 `setPtn` 撤回胸型；仅原位回写胸链
  的物理字段。

## 2026-08-14：Soma Dynamics KKS v1.0.3.3

- 内置高档精确采用用户 `MyPreset1.xml`。
- PushUp 重算后重采 Soma 骨骼基准并清除旧速度，修复拖动胸托滑条造成的肢体变形；
  胸部原生物理参数在重算完成后延迟回写。

## 2026-08-14：Soma Dynamics KKS v1.0.3.2 性能热修复

- 自由 H 反射兜底检测改为仅在场景加载/切换后扫描一次并缓存，修复 1.0.3.1-KKS
  在普通游戏与 Studio 每帧全场景扫描导致的性能回归。

## 2026-08-13：KKS 独立仓库首次发布

- Soma Dynamics KKS v1.0.2.7：与 KK 版 v1.0.2.7 功能一致（Timeline 安全弹簧开关等），
  源码与 KK 版共用，仅程序集依赖切换为 KKSAPI 与 KKS_ExtensibleSaveFormat。
- MMD DynamicBone Stabilizer KKS v1.2.2：与 KK 版 v1.2.2 功能一致（异常链强制重置
  校验与隔离），CharaAnime 依赖切换为 `Countd360.CharaAnime.KKS`。
- 两个插件在 KKS 本地沙盒 `H:\KKS\KKS`（主游戏 + CharaStudio）验证加载、初始化与
  补丁安装全部正常。
