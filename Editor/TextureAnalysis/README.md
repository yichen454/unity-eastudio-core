# 场景与Build纹理检查分析工具 (Scene & Build Texture Analyzer)

## 功能概述
用于针对 Unity 项目中**当前激活场景 (Current Scene)** 或 **Build Settings 列表场景 (Build Scenes)** 进行全面的纹理依赖与使用情况分析，帮助项目定位内存浪费、格式不规范以及运行时潜在性能问题。

## 核心特性
1. **多维度检查指标**:
   - **分辨率 (Resolution)**: 检查纹理尺寸，高亮标记高分辨率 (≥2048) 纹理。
   - **POT (Power of Two)**: 检查并标记非 2 的整数次幂 (NPOT) 纹理，规避部分平台补边与压缩失效问题。
   - **Mipmap 分析**:
     - 识别 3D 场景大纹理未开 Mipmap 的噪点走样与缓存未命中问题。
     - 识别 UI / Sprite 误开 Mipmap 导致的约 33% 冗余内存浪费。
   - **显存与磁盘占用**:
     - 基于 `Profiler.GetRuntimeMemorySizeLong` 获取真实 GPU/CPU 运行时显存占用。
     - 统计磁盘文件大小并按降序排序，快速找到内存大头。
   - **Read/Write 检测**:
     - 检测开启了 `Read/Write Enabled` 的纹理（在 CPU 和 GPU 各驻留一份，导致双倍内存浪费）。
   - **未压缩格式检测**:
     - 识别 RGBA32/RGB24 等非压缩格式，提醒切换硬件压缩 (ASTC/ETC2/BC7/DXT)。
   - **同名重复资源预警**:
     - 检测项目中不同路径但同名的纹理，防范重复打包。

2. **便捷操作与一键修复**:
   - 提供**一键关闭 Read/Write**、**一键开启/关闭 Mipmap**、**一键限制 Max Size (1024/512)** 等快捷修复操作。
   - 快速在 Project 窗口定位纹理资源，或定位引用该纹理的场景文件。

## 打开方式
Unity 编辑器顶部菜单：
`Tools -> EAStudio -> Texture -> Scene Texture Analyzer`
