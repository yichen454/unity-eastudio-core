# EAStudio Core (`com.eastudio.core`)

EAStudio 核心基础库与通用工具包，提供 URP 自定义渲染特性、场景预热流程、Timeline 扩展套件以及全套编辑器生产力工具。

---

## 🌟 核心功能特性

### 1. 运行时模块 (`Runtime`)
- **Render Features (自定义渲染特性)**：
  - **Sky & Cloud System (天空与程序化云系统)** (`SkyRenderFeature`)：
    - **HDRP 式解耦架构**：仿照 HDRP 的 Volume 驱动模式，通过 `VisualEnvironment` 统筹控制 `HDRISky`、`ProceduralSky`、`MoonSettings` 与 `CloudSettings`。
    - **全景 HDRI 天空 (`HDRISky`)**：支持高动态范围 HDR 浮点解码、精准标准灰阶色彩叠加（#808080）、多 Volume 间权重交叉过渡（Cross-fade Blending）及 XR Multiview / 双目模式完整兼容。
    - **物理程序化大气天空 (`ProceduralSky`)**：基于分析式物理大气散射算法（完整复刻瑞利散射、米氏气溶胶前向散射、臭氧层吸收），呈现真实的晨昏红移与落日晚霞；双高斯软核日冕模型彻底消除硬切边缘；支持落日后平滑过渡至星空 HDRI 全景贴图或物理深空底色。
    - **3D 天文月相与夜空系统 (`MoonSettings`)**：支持 3D 物理广告牌月相阴影投射（根据日月空间夹角实时计算朔、望、弦月相），集成 NASA 高清月面材质贴图采样、微弱地照光（Earthshine）及柔和月冕光晕（Halo）。
    - **多层体积感程序化云 (`CloudSettings`)**：内置 6 张离线生成的 7-octave 周期平铺噪声图（Worley、Billow、Perlin、层积云及高频侵蚀图），支持双层独立海拔与云层形态；使用 Beer-Lambert 吸收定律与多步光线步进（Lightmarching）实现丰富的内部立体阴影与 Henyey-Greenstein 逆光银边；在不透明物体渲染前以低分辨率生成并在天空盒中直接合成，支持导出全局参数供地形阴影与体积光采样。
    - **平行光 Cookie 动态云层阴影 (`CloudShadowCookie.shader`)**：将实时生成的顶视光流贴图绑定至 `Directional Light.cookie`，无需侵入场景材质即可在地面与物体上投射自然流动的云影。
    - **24 小时昼夜双灯控制器 (`TimeOfDay.cs`)**：支持地理纬度、正北罗盘偏角与四季赤纬角轨道推算，自动协同控制 Sun Light 与 Moon Light 的朝向、强度渐变与阴影管线调度，内置天体防重合空间排斥机制。
    - **全中文参数提示**：所有 Volume 参数及控制器提供完善且符合技术美术习惯的中文分栏（`[Header]`）与悬浮提示（`[Tooltip]`）。
  - **Depth PrePass** (`RenderingLayerDepthPrepassFeature` / `CustomDepthContextData`)：支持 Rendering Layer Mask 过滤的自定义深度预通过。
  - **UI / Overlay** (`UIOverlayRenderFeature`)：基于 URP RenderGraph 的场景与 UI 叠加渲染特性。
- **Scene Warmup (场景预热与流程控制)**：
  - `SceneWarmup`：场景分步与异步预热管理。
  - `EnableFlow`：带单帧时间预算（Frame Budget）的对象分帧渐进激活流程，有效避免瞬时卡顿。
  - `SceneReference`：安全的场景资产引用封装（支持 Editor 预览与 Runtime 路径解析）。
  - `SceneWarmupTrack`：集成进 Timeline 的场景预热轨道、剪辑与混合行为。
- **Timeline Trigger (Timeline 触发器)**：
  - `TimelineTrigger`：基于 Timeline 驱动的轻量事件触发系统，附带自动同步 Clip 列表的专用 Inspector 增强。
- **Common & Utilities (基础通用工具)**：
  - `AndroidPermissionUtils`：Android 11+（API 30+）所有文件访问权限检查与申请跳转。
  - `BoneVisualizer`：SkinnedMeshRenderer 骨骼层级可视化组件。
  - `DisableRendererCulling`：强制关闭视锥与渲染剔除组件。

### 2. 编辑器工具集 (`Editor`，菜单路径：`Tools/EAStudio/`)
- **资产 (`Tools/EAStudio/资产/`)**：
  - **场景资产整理与修复 (`AssetFixerWindow`)**：分析并整理场景中模型、材质、贴图依赖，支持自动化贴图规则导出与 Prefab 修复。
  - **场景资源归类整理 (`ResourceOrganizerWindow`)**：拖拽场景对象，按模型维度自动化批量归类与整理关联资源。
- **光照 (`Tools/EAStudio/光照/`)**：
  - **光照贴图使用分析 (`LightmapAnalyzer`)**：一键统计与分析场景网格的 GI 烘焙贡献、Lightmap 0 占用及探针使用情况。
- **地形 (`Tools/EAStudio/地形/`)**：
  - **合并选中地形 (`TerrainMergeTool`)**：多块地形的高度图、Alphamap 权重图与 TerrainLayers 稳定合并。
  - **地形树转实体 GameObject (`TerrainTreeConverterWindow`)**：将 TerrainData 原生树批量实例化为独立 GameObject，并自动配置 Occludee Static。
- **贴图 (`Tools/EAStudio/贴图/`)**：
  - **贴图编辑器 (`TextureEditorWindow`)**：基于 GPU Compute Shader 实现的极速贴图处理工具，支持 **RGBA 通道合并**（MaskMap 生成）与 **程序化多类型噪声图生成**（Perlin / Simplex / Worley / White / FBm / Turbulence）。

---

## 📦 安装方式

### 方式 A：通过 Package Manager Git URL（推荐）
1. 打开 Unity 编辑器菜单：`Window` -> `Package Manager`。
2. 点击左上角 `+` 号 -> 选择 **Add package from git URL...**。
3. 输入仓库地址：
   ```text
   https://github.com/yichen454/unity-eastudio-core.git
   ```
   > 如需锁定特定版本，可在尾部追加 Tag，例如：`https://github.com/yichen454/unity-eastudio-core.git#v0.2.0`

### 方式 B：直接配置 `Packages/manifest.json`
在项目的 `Packages/manifest.json` 的 `dependencies` 中添加：
```json
{
  "dependencies": {
    "com.eastudio.core": "https://github.com/yichen454/unity-eastudio-core.git"
  }
}
```

---

## 📋 环境与依赖要求

- **Unity 版本**：Unity 6 (6000.0+) / Unity 2022.3+
- **Universal Render Pipeline**：`com.unity.render-pipelines.universal` (17.0.0+)
- **Timeline**：`com.unity.timeline` (1.8.0+)

---

## 📂 目录结构

```text
Packages/com.eastudio.core/
├── Editor/                         # 通用编辑器工具集 (EAStudio.Core.Editor)
│   ├── Assets/                     # 资产修复与整理窗口
│   ├── Lighting/                   # 光照贴图使用分析
│   ├── Terrain/                    # 地形合并与地形树转换
│   └── Texture/                    # 贴图编辑器与 Compute Shaders
└── Runtime/                        # 运行时核心模块 (EAStudio.Core.Runtime)
    ├── Common/                     # 通用系统/平台/渲染辅助
    ├── RenderFeature/              # URP 渲染特性 (DepthPrePass / Overlay / Sky)
    │   ├── DepthPrePass/           # 深度预处理特性
    │   ├── Overlay/                # UI 叠加渲染特性
    │   └── Sky/                    # 天空盒与云层系统 (Environment, Pass, Shaders, Volume)
    ├── SceneWarmup/                # 场景预热与激活流程
    └── Timeline/                   # Timeline 扩展 (Trigger 等)
```

---

## 👤 作者
- **yichen454** - [GitHub 主页](https://github.com/yichen454)
