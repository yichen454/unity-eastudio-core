# EAStudio Core (`com.eastudio.core`)

EAStudio 核心基础库与通用工具包，提供 URP 自定义渲染特性、场景预热流程、Timeline 扩展套件以及全套编辑器生产力工具。

---

## 🌟 核心功能特性

### 1. 运行时模块 (`Runtime`)
- **Render Features (自定义渲染特性)**：
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
    ├── RenderFeature/              # URP 渲染特性 (DepthPrePass / Overlay)
    ├── SceneWarmup/                # 场景预热与激活流程
    └── Timeline/                   # Timeline 扩展 (Trigger 等)
```

---

## 👤 作者
- **yichen454** - [GitHub 主页](https://github.com/yichen454)
