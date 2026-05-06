# OCX_Visualization（Unity 结构 OCX/3DOCX 可视化与编辑）

本项目用于在 Unity 中导入并可视化 OCX/3DOCX（以及兼容的 XML）结构数据，支持 S0/S1/S2 三种 Schema 的板材/加劲材建模与交互编辑，并可将编辑结果回写导出为与源文件相同后缀的文件（覆盖或另存为）。

当前主场景：`Assets/Scenes/3DOCX.unity`


## 目录

- [运行环境](#运行环境)
- [功能总览](#功能总览)
- [快速开始（Editor）](#快速开始editor)
- [操作流程（面向使用者）](#操作流程面向使用者)
- [导入与解析（S0/S1/S2）](#导入与解析s0s1s2)
- [绘制与几何实现](#绘制与几何实现)
- [数据编辑与导出](#数据编辑与导出)
- [撤销（Ctrl+Z）](#撤销ctrlz)
- [部署与发布（Windows）](#部署与发布windows)
- [目录结构](#目录结构)
- [常见问题](#常见问题)
- [开发者入口（代码位置）](#开发者入口代码位置)

---

## 运行环境

- Unity：`6000.3.4f1`（见 [ProjectVersion.txt](file:///f:/Uinty%203D/chuanbokeshihua/ProjectSettings/ProjectVersion.txt)）
- 渲染：URP（`com.unity.render-pipelines.universal`）
- 输入系统：Input System（`com.unity.inputsystem`）
- 平台：Windows（多文件选择使用 Win32 `Comdlg32.GetOpenFileName`，见 [LocalFileBrowser.cs](file:///f:/Uinty%203D/chuanbokeshihua/Assets/Scripts/LocalFileBrowser.cs)）

---

## 功能总览

### 1) 导入

- 支持一次性选择并导入多个 OCX/3DOCX/XML 文件
- 导入过程带遮罩层进度条与导入成功/失败汇总提示
- 单文件/多文件导入后自动对齐到 CAD 网格坐标系，并自动聚焦视角

相关实现：
- UI 入口与进度： [FileBrowserUI.cs](file:///f:/Uinty%203D/chuanbokeshihua/Assets/Scripts/FileBrowserUI.cs#L631-L805)
- Win32 多选对话框： [LocalFileBrowser.cs](file:///f:/Uinty%203D/chuanbokeshihua/Assets/Scripts/LocalFileBrowser.cs#L36-L128)
- 解析与建模入口： [OcxSystemManager.cs](file:///f:/Uinty%203D/chuanbokeshihua/Assets/Scripts/OcxSystemManager.cs#L139-L197)

### 2) 建模/绘制

- 板材（Plate/Panel）：根据边界多边形生成网格（并支持厚度挤出）
- 加劲材（Stiffener）：根据截面信息与长度方向生成实体（Mesh + Collider）
- S2 开孔（Opening）：对板材进行孔洞裁剪与三角化，保证孔洞对测量/点选可识别
- 板厚方向：S2 Plate 优先使用 `<Inclination><bDirection .../>` 作为厚度方向（板厚沿该方向挤出）
- 贴合策略：Stiffener 会自动贴合到所在 Plate 表面（投影到板面并沿板厚法向偏移半厚），避免悬空
- 板件描边：每个 Plate 会自动生成白色细描边（仅边线，不绘制面），包含顶/底外轮廓与竖向边线，体现板厚立体效果
- 板缝线：S2 Plate 若存在 `<SplitBy><Seam><TraceLine>`，会解析并绘制黑色细线（支持 Line3D 与 CompositeCurve3D/CircumArc3D）

### 3) 交互

- 选择高亮：选中对象材质高亮（红色），并保持恢复
- S2 开孔选中提示：选中开孔会绘制红色实线圈
- 变换：移动/旋转/缩放，支持轴向锁定（X/Y/Z/自由）
- 切面浏览：三维盒剖切（只显示剖切长方体内的结构），右下角小窗口可拖拽盒子 XYZ 边界与位移
- 板件列表：按文件分组、可搜索与定位；与场景点选同步定位
- 详情面板：可编辑板厚/型材/端切/重量；材质显示为解析后的名称（只读）
- 显示质量：自动开启较高的抗锯齿/纹理过滤，并动态优化相机 near/far 提升深度精度与清晰度
- 右上角坐标轴：随相机视角同步；按 OCX 语义映射（垂直轴为 Z）
- 全局显示倍率：可通过 `modelVisualScale` 把所有构件等比例放大，方便肉眼区分板厚差异（尺寸/测量仍按真实 mm 显示）
- 描边/板缝线宽可调：`plateOutlineWidth`（白色描边）与 `seamLineWidth`（黑色板缝线）均为 public，可在 Inspector 直接调节

### 4) 测量

- 测距：依次点击 2 个点
- 测角：依次点击 3 个点（第二点为角点）
- 测圆弧：依次点击 3 个点（起点-中间点-终点）
- 测量取点规则：
  - Plate/Panel：点必须落在板面内（支持边界/边上），且落在开孔内会被剔除
  - Stiffener：允许直接使用射线命中的点进行测量

相关实现：
- 测量逻辑： [MeasurementTool.cs](file:///f:/Uinty%203D/chuanbokeshihua/Assets/Scripts/MeasurementTool.cs)
- 测量模式下转发鼠标点击（避免测量时误选中板件）： [FileBrowserUI.cs](file:///f:/Uinty%203D/chuanbokeshihua/Assets/Scripts/FileBrowserUI.cs#L981-L999)

### 5) 撤销与导出

- Ctrl+Z 撤销：对 Transform + 关键字段（材质/厚度/截面/端切/重量）做快照回滚
- 导出：支持覆盖源文件或另存为 `_edited`，并保持与源文件相同的后缀格式
- 退出：退出前弹窗选择「不保存退出 / 覆盖保存退出 / 另存为退出」

相关实现：
- 撤销： [OcxSystemManager.cs](file:///f:/Uinty%203D/chuanbokeshihua/Assets/Scripts/OcxSystemManager.cs#L279-L376)
- 导出： [OcxSystemManager.cs](file:///f:/Uinty%203D/chuanbokeshihua/Assets/Scripts/OcxSystemManager.cs#L2965-L3053)
- 退出弹窗： [FileBrowserUI.cs](file:///f:/Uinty%203D/chuanbokeshihua/Assets/Scripts/FileBrowserUI.cs#L205-L324)

---

## 快速开始（Editor）

1. 使用 Unity Hub 打开工程根目录 `chuanbokeshihua/`
2. 打开场景：`Assets/Scenes/3DOCX.unity`
3. 点击 Play
4. 点击顶部工具栏「导入」按钮，选择一个或多个 `.3docx/.ocx/.xml` 文件

示例数据：
- `Assets/StreamingAssets/` 下包含若干测试 3DOCX 文件，可用于快速验证导入链路

---

## 操作流程（面向使用者）

### 1) 导入

- 点击顶部工具栏「导入」
- 在弹出的 Windows 文件对话框中可多选
- 导入过程中会显示遮罩层与进度条
- 导入结束后：
  - 右侧信息区会输出成功/失败统计与失败原因（若有）
  - 相机会自动聚焦到最近导入的一组模型

### 2) 选择与聚焦

- 鼠标点选构件（Plate/Panel/Stiffener/Opening）
- 选中后会高亮显示
- 若选中的是 Opening：
  - 孔边界会绘制红色实线闭合圈，提示当前选中的孔

相关实现：
- 高亮与开孔描边： [SelectionHighlighter.cs](file:///f:/Uinty%203D/chuanbokeshihua/Assets/Scripts/SelectionHighlighter.cs)

### 3) 板件列表与定位

- 左侧「板件列表」按导入文件分组组织
- 支持搜索：
  - 输入关键词后回车或点“定位”，优先定位 Plate，其次定位其他构件
-  - 点列表项会选中
- 场景中点选构件后，列表会自动展开到对应节点并高亮定位（必要时会自动清空搜索以显示该构件）

相关实现：
- 列表与搜索： [PartListUI.cs](file:///f:/Uinty%203D/chuanbokeshihua/Assets/Scripts/PartListUI.cs)

### 4) 测量

- 点击工具栏「测距 / 测角 / 测圆弧」进入对应测量模式
- 进入测量模式后：
  - 鼠标左键点击会被测量系统接管（不会再触发选择模式）
  - 按提示依次点选所需数量的点
  - 结果会在右侧信息区展示，并在场景中绘制辅助线/文字

### 5) 变换（移动/旋转/缩放）

- 点击工具栏「移动 / 旋转 / 缩放」进入对应模式
- 轴向按钮：
  - 自由轴向：不锁轴
  - X/Y/Z：锁定到对应轴
- 微调缩放：
  - “放大一点 / 缩小一点”用于对选中对象做倍率微调

### 6) 切面浏览

- 点击工具栏「切面」进入/退出切面模式
- 进入切面模式后：
  - 只展示剖切长方体内部的三维结构
  - 右下角小窗口拖拽长方体：
    - 拖拽盒子或中心块：整体位移
    - 拖拽 X/Y/Z 的 Min/Max 小块：单独拉伸对应方向边界
  - 主视图相机操作：
    - 右键拖拽：围绕剖切盒中心 360° 旋转视角
    - 滚轮：缩放
    - 滚轮按下拖拽（中键）：平移视角
- 退出方式：再次点击工具栏「切面」

### 7) 编辑详情

- 点击工具栏「详情」打开当前选择对象的详情面板
- 可编辑字段（根据构件类型动态显示）：
  - Plate：材质（只读显示名称）、Thickness、Weight
  - Stiffener：材质（只读显示名称）、SectionRef、EndCutCode、Weight
- 板厚/型材/端切/重量修改后会即时更新显示与后续导出内容（材质当前不在 UI 中修改）

### 8) 撤销 / 保存 / 退出

- 撤销：`Ctrl + Z`
- 保存：点击工具栏「导出」
  - 覆盖/另存为由 `overwriteOriginalOnExport` 控制（退出弹窗也会临时切换该策略）
- 退出：点击工具栏最右侧「退出」
  - 不保存退出
  - 覆盖保存退出
  - 另存为退出（在原文件名后追加 `_edited`，后缀与源文件一致）

快捷键：
- `Ctrl + Z`：撤销
- `N`：显示 UI
- `Esc`：隐藏 UI

---

## 导入与解析（S0/S1/S2）

工程会在导入时自动判断 Schema 并选择对应建模流程（见 [OcxSystemManager.LoadAndBuild](file:///f:/Uinty%203D/chuanbokeshihua/Assets/Scripts/OcxSystemManager.cs#L139-L197)）：

- S0：无 Plate 但有 Panel 时，按 S0 流程建模（面片/轮廓）
- S1：有 Plate 且不满足 S2 特征，按 S1 流程建模
- S2：检测到 `EndCutEnd1/EndcutParameters/OpeningParameter` 等字段，按 S2 流程建模（含开孔与端切等制造信息）

### OCX 文件如何被读取（从 UI 到建模的完整链路）

这部分按“实际代码调用顺序”说明，方便你定位任意环节的输入/输出。

1) UI 触发导入

- 顶部工具栏 `TBImport` 点击后进入 `OnImportLocalFileClicked()`
- 调用 Windows 原生文件选择器，支持多选，返回绝对路径数组

代码入口：
- [FileBrowserUI.OnImportLocalFileClicked](file:///f:/Uinty%203D/chuanbokeshihua/Assets/Scripts/FileBrowserUI.cs#L631-L644)
- [LocalFileBrowser.OpenFiles](file:///f:/Uinty%203D/chuanbokeshihua/Assets/Scripts/LocalFileBrowser.cs#L54-L100)

2) Windows 多选文件对话框（Win32 GetOpenFileName）

- 通过 `Comdlg32.dll` 的 `GetOpenFileName` 打开系统对话框
- 使用 `OFN_ALLOWMULTISELECT` 返回 MultiString（目录\0文件1\0文件2...\0\0）
- 手动解析缓冲区，组装 `dir + filename` 得到完整路径数组

实现位置：
- [LocalFileBrowser.cs](file:///f:/Uinty%203D/chuanbokeshihua/Assets/Scripts/LocalFileBrowser.cs#L36-L128)

3) 导入进度与失败原因汇总

- `ImportFilesCoroutine` 逐个文件导入
- 每次导入前更新遮罩层文案与进度条
- 每个文件调用 `systemManager.TryLoadAndBuild(path, appendMode:true, out err)`
- 收集失败原因并在右侧信息面板汇总输出

实现位置：
- [FileBrowserUI.ImportFilesCoroutine](file:///f:/Uinty%203D/chuanbokeshihua/Assets/Scripts/FileBrowserUI.cs#L646-L693)

4) 读取 XML / 识别 Schema / 进入建模

`TryLoadAndBuild` 内部做了几件关键事：

- 路径校验：空路径/文件不存在直接返回错误
- 编码识别：`DetectEncoding`（用于后续导出保持原始编码习惯）
- XML 加载：`XDocument.Load(..., PreserveWhitespace)`，Namespace 固定为 `http://data.dnvgl.com/Schemas/ocxXMLSchema`
- 建立索引：按 `id` 缓存 Plate/Stiffener 的 `XElement`，用于导出时回写
- Schema 识别：
  - `!hasPlates && hasPanels` => S0（Panel 为主）
  - 否则检测 S2 特征（EndCut/OpeningParameter 等）=> S2
  - 否则 => S1
- 建模结束后执行：
  - 统一对齐到 CAD 网格坐标系
  - 自动聚焦视角
  - 广播 `ModelsChanged`

实现位置：
- [OcxSystemManager.TryLoadAndBuild](file:///f:/Uinty%203D/chuanbokeshihua/Assets/Scripts/OcxSystemManager.cs#L199-L277)
- Schema 选择分支（S0/S1/S2）：[OcxSystemManager.TryLoadAndBuild: 选择 BuildFromS0/BuildFromS1/BuildFromS2](file:///f:/Uinty%203D/chuanbokeshihua/Assets/Scripts/OcxSystemManager.cs#L240-L270)

5) 坐标系转换（OCX → Unity）

OCX 的点坐标在解析时做了统一转换：

- `Point3D(X,Y,Z)` -> `new Vector3(x, z, y)`
- 也就是把 OCX 的 Y/Z 轴交换到 Unity 的 Y(高度)/Z(深度)习惯上

这会影响所有：板边界、开孔边界、加劲材端点、方向向量等。

实现位置：
- [OcxSystemManager.ParsePoint](file:///f:/Uinty%203D/chuanbokeshihua/Assets/Scripts/OcxSystemManager.cs#L2427-L2434)
- 方向向量同样做了 x,z,y： [OcxSystemManager.ParseDirection](file:///f:/Uinty%203D/chuanbokeshihua/Assets/Scripts/OcxSystemManager.S2.cs#L267-L276)

6) “为什么导出能回写到原文件”

导入时会把每个文件的 `XDocument` 与 `SourcePath/SourceEncoding/FileGroup` 记录在内部结构里，并建立 `PlatesById/StiffenersById` 索引；导出时遍历该文件组下的 `PartData`，把修改写回到对应 `XElement`，最后保存到磁盘（覆盖或另存为）。

实现位置：
- 导出回写： [OcxSystemManager.ExportEdited3docx](file:///f:/Uinty%203D/chuanbokeshihua/Assets/Scripts/OcxSystemManager.cs#L2965-L3022)

### S2：开孔/型材信息如何被“提取出来”

S2 的“读取”不仅是把 XML 读进来，还要把板边界、孔洞、型材截面等信息提取成可建模的数据结构。

1) 板外轮廓（OuterContour）

- 优先读取 `OuterContour` 里的 `Point3D` 列表
- 若没有点列表，则尝试读取 `CompositeCurve3D`：
  - 直线段 `Line3D`：用 Start/End 拼 polyline
  - 圆弧段 `CircumArc3D`：用 Start/Intermediate/End 采样成折线（默认 32 段）
- CompositeCurve3D 支持 `refid/localRef` 指向文档中其他节点

实现位置：
- [ExtractS2PlateBoundaryPoints / ExtractCompositeCurvePolyline / ResolveCompositeCurve3D](file:///f:/Uinty%203D/chuanbokeshihua/Assets/Scripts/OcxSystemManager.S2.cs#L424-L558)

2) 板开孔（InnerContour in CutBy）

- 扫描 `CutBy` 下的 `InnerContour`
- 孔边界提取与外轮廓一致：Point3D 或 CompositeCurve3D（含圆弧采样）
- 同时读取 `OpeningParameter`，并把 `openingType/slotProfileName/...` 写入参数字典
- 开孔不仅从 Plate 内部找：
  - 也会从 Panel 维度提取 Opening，再映射到 Plate（避免“孔定义在 Panel 里导致 Plate 丢孔”）

实现位置：
- [ExtractS2PlateOpenings](file:///f:/Uinty%203D/chuanbokeshihua/Assets/Scripts/OcxSystemManager.S2.cs#L475-L536)
- [ExtractPanelOpenings](file:///f:/Uinty%203D/chuanbokeshihua/Assets/Scripts/OcxSystemManager.S2.cs#L560-L622)

3) 型材截面（BarSection）与 SectionRef

- 扫描文档中所有 `BarSection`，提取截面参数，缓存两套索引：
  - `byId`：按 `id`
  - `byGuid`：按 `GUIDRef`（做了去花括号/转大写归一化）
- Stiffener 的 `SectionRef` 同时支持：
  - `localRef`
  - `GUIDRef`（属性名可能在 namespace 或 localname）
- 当前支持的截面类型：
  - FlatBar：Height/Width
  - LBar：Height/Width/WebThickness/FlangeThickness
- 若 XML 内缺失尺寸，会从名称字符串兜底解析（如 `FL200x12`、`L200x100x10x12`）

实现位置：
- 截面解析： [ParseSectionProfile / ResolveSectionProfile](file:///f:/Uinty%203D/chuanbokeshihua/Assets/Scripts/OcxSystemManager.S2.cs#L293-L376)
- 名称兜底解析： [TryParseFlatBarFromName / TryParseLBarFromName](file:///f:/Uinty%203D/chuanbokeshihua/Assets/Scripts/OcxSystemManager.S2.cs#L378-L422)

S2 解析补强点（实现集中在 [OcxSystemManager.S2.cs](file:///f:/Uinty%203D/chuanbokeshihua/Assets/Scripts/OcxSystemManager.S2.cs)）：

- SectionRef 解析兼容 GUIDRef/localRef 两条路径
- 型材规格优先从 XML 结构读取，不足时可从名称字符串兜底解析
- 加劲材贴板定位：读取/推断 COG 与姿态，将加劲材投影附着到板面

---

## 绘制与几何实现

### 核心数据结构（PartData）

所有可交互对象都会挂 `PartData`，其关键字段包括：

- `PartType`：Plate/Panel/Stiffener/Opening
- `Boundary`：外轮廓（世界坐标）
- `OpeningBoundaries`：孔洞轮廓列表（世界坐标，仅 Plate 使用）
- `FaceNormal`：用于投影/测量的板面法向（世界坐标）
- `SourceFilePath/SourceElementType`：用于导出时把修改回写到哪一个源文件/哪一种元素

定义位置：
- [PartData.cs](file:///f:/Uinty%203D/chuanbokeshihua/Assets/Scripts/PartData.cs#L6-L36)

### 板材（Plate/Panel）

板材绘制的核心目标是：把“外轮廓 + 若干孔洞”的二维结构，稳定三角化成 Unity Mesh，并且保证交互链路（选中、测量、导出）可用。

#### Plate 的网格生成流程（BuildPrecisePlateInternal）

1) 创建节点与局部化

- 新建 `GameObject(id)` 并放置到 `CenterOfGravity`（COG）
- 将外轮廓从世界坐标变换到该物体局部坐标
- 去除连续重复点、去除闭合重复末点
- 估算法线 `EstimateNormal`，简化折线（去抖/去噪），统一 CCW 绕序
- 厚度方向：优先使用 S2 Plate 的 `<Inclination><bDirection .../>`（若缺失则回退到估算法线方向）

2) 开孔处理（S2）

每个孔会先转换到局部坐标并简化，然后与外轮廓关系分类：

- Inside：孔完全在板内 => 作为 holeLoop 参与“带洞三角化”
- Intersect：孔与外轮廓相交/超出边界 => 先做裁剪/相减：
  - `SubtractOpeningFromOuterLocal` 会输出：
    - 更新后的外轮廓 `nextOuter`
    - 被裁剪到板内的孔轮廓 `clippedHoleLocal`
  - 并用面积变化做一次防护：避免异常裁剪导致“扣掉太多”或“没扣掉”
- Outside：忽略

3) 带洞三角化（稳定性保障）

- 计算“理论面积”：外轮廓面积 - 所有孔面积
- 若存在孔洞：先用 `MergePolygonWithHolesLocal` 把“外轮廓 + 多孔”桥接成一个简单多边形
- 对合并后的多边形做耳切三角化 `TriangulateLocalPolygon`
- 计算“实际三角形覆盖面积”，与理论面积比值 `ratio`
- 若 `ratio < 0.995`，会用不同的 `epsScale` 和不同孔排序策略进行重试，选出覆盖率更高的候选
- 若仍不足，会自动估计漏面区域重心并放置标记（用于现场定位）

4) Mesh/Collider/轮廓线

- Mesh 输出为双面三角形（正反各一套 triangles）
- Collider：
  - 可选 MeshCollider（`useMeshColliderForPlates=true`）
  - 默认 BoxCollider（最小尺寸由 `minPlateColliderSize` 保底，避免板太薄导致点选困难）
- 默认不绘制板外轮廓线（避免轮廓线/白边干扰）
- 开孔轮廓默认不画（`drawOpeningOutlines=false`），只做真实挖孔；若打开则会绘制孔轮廓线
- 每个孔还会创建一个子对象 `Opening_*`，挂 `PartData(PartType="Opening")` 用于可选中/聚焦/提示圈

实现位置：
- [OcxSystemManager.BuildPrecisePlateInternal（外轮廓、孔处理、三角化、Collider、Opening 子对象）](file:///f:/Uinty%203D/chuanbokeshihua/Assets/Scripts/OcxSystemManager.cs#L2572-L2889)

数据结构：
- 边界与孔洞：`PartData.Boundary`、`PartData.OpeningBoundaries`（见 [PartData.cs](file:///f:/Uinty%203D/chuanbokeshihua/Assets/Scripts/PartData.cs#L8-L35)）

### 加劲材（Stiffener）

加劲材绘制的核心目标是：按 OCX 的 TraceLine 给出长度方向，用 SectionRef 给出截面尺寸，生成可交互的实体。

#### Stiffener 的网格生成流程（BuildPreciseStiffener）

0) 与板材贴合（避免悬空）

- 会尝试找到同 Panel 下的 Plate，并将 Stiffener 的 TraceLine 投影到该 Plate 的板面
- 再沿 Plate 的板厚法向（bDirection）偏移半厚，使其落在实体板表面

1) 建立局部坐标系

- `axisZ = (End - Start).normalized` 作为长度方向
- `axisY` 优先取 S2 的 `WebDirection`（并做正交化与归一化）
- `axisX` 优先取 `FlangeDirection`（不足时用叉积补齐）
- 以 `Position/COG/中点` 推断一个锚点，设置 `transform.position/rotation`

2) 截面多边形与挤出

- FlatBar：用矩形截面（Height/Width）
- LBar：用 6 点折线构造 L 型截面（Height/Width/WebThickness/FlangeThickness）
- 用 `TriangulateLocalPolygon` 三角化截面 cap
- 沿 `axisZ` 把截面挤出成两个截面层，并拼侧面 triangles
- MeshCollider 用于交互与测量

实现位置：
- [OcxSystemManager.BuildPreciseStiffener](file:///f:/Uinty%203D/chuanbokeshihua/Assets/Scripts/OcxSystemManager.cs#L2890-L3067)

### 开孔（Opening）

- S2 开孔会进行真实“挖空”而非仅描线（`drawOpeningOutlines` 默认关闭）
- Opening 作为可选中对象存在：选中后会自动聚焦，并显示红色实线圈

Opening 的“可选中/可聚焦/提示圈”来自两层实现：

1) Opening 子对象（数据层）

- Plate 建模时会为每个孔创建一个子 GameObject
- 子对象挂 `PartData(PartType="Opening")`
- 其 `PartData.Boundary` 存储该孔的世界坐标闭合轮廓

实现位置：
- [OcxSystemManager.BuildPrecisePlateInternal: Opening 子对象创建](file:///f:/Uinty%203D/chuanbokeshihua/Assets/Scripts/OcxSystemManager.cs#L2790-L2850)

2) 选中提示圈 + 自动聚焦（表现层）

- 当选择系统选中 `PartType == "Opening"`：
  - 自动创建/更新一条 `LineRenderer`，用红色实线画圈（世界坐标、面向相机）
  - 调用 `TryAutoFocusCameraOn(opening.transform, ...)` 聚焦到当前孔

实现位置：
- [SelectionHighlighter（Opening 选中提示圈与聚焦）](file:///f:/Uinty%203D/chuanbokeshihua/Assets/Scripts/SelectionHighlighter.cs#L62-L182)

### S0：Panel 的绘制方式（骨架/轮廓为主）

S0 的 Panel 不一定有完整外轮廓点集，因此流程更偏“参考平面 + 限制线”：

- 从 `CoordinateSystem/FrameTables/RefPlane` 读取参考平面（X/Y/Z）位置
- Panel 的 `UnboundedGeometry/GridRef` 决定它在哪个参考平面上
- `LimitedBy` 决定范围：
  - 若有 `CompositeCurve3D`（Line3D + CircumArc3D），会采样成折线并生成边界
  - 否则退化成一个矩形范围（用默认范围兜底）
- 会额外绘制曲线 polyline（用于可视化边界来源）

实现位置：
- [OcxSystemManager.BuildFromS0](file:///f:/Uinty%203D/chuanbokeshihua/Assets/Scripts/OcxSystemManager.cs#L589-L780)

---

## 数据编辑与导出

### 编辑项

在详情面板中编辑的字段会写回到 `PartData`，导出时会回写到原始 `XDocument` 再输出到磁盘。

材质说明：
- 展示：按 `ocx:GUIDRef` 关联到 `<ocx:Material ... name=...>`，显示 `name`（例如 AH32）
- 存储：`PlateMaterial/@localRef`（Plate）与 `MaterialRef/@localRef`（Stiffener）仍会保留用于导出与兼容，但当前不在 UI 中修改材质引用

- Plate：
  - `PlateMaterial/@localRef`（若 `PartData.MaterialRef` 被修改）
  - `Thickness/@numericvalue` 与 `@unit`（若存在或可补写）
- Stiffener：
  - `MaterialRef/@localRef`（若 `PartData.MaterialRef` 被修改）
  - `SectionRef/@localRef`
  - `EndCutEnd1/@name`

导出行为：
- 覆盖：`outPath = loaded.SourcePath`
- 另存为：`name + "_edited" + ext`（ext 取源文件后缀，空则默认 `.3docx`）

实现位置：
- [OcxSystemManager.ExportEdited3docx](file:///f:/Uinty%203D/chuanbokeshihua/Assets/Scripts/OcxSystemManager.cs#L3068-L3125)

---

## 撤销（Ctrl+Z）

撤销采用“快照”思路：

- 通过状态哈希检测变化
- 变化停止一段时间（`undoIdleSeconds`）后将快照压栈
- `Ctrl + Z` 回滚最近快照

覆盖范围（快照内容）：
- 每个 PartData 的 Transform：Position/Rotation/Scale
- 关键字段：MaterialRef/Thickness/SectionRef/EndCutCode/Weight

实现位置：
- [OcxSystemManager.UndoLast](file:///f:/Uinty%203D/chuanbokeshihua/Assets/Scripts/OcxSystemManager.cs#L306-L342)

---

## 部署与发布（Windows）

### 1) 打包构建

1. `File -> Build Settings...`
2. 确保 `Assets/Scenes/3DOCX.unity` 在 Scenes In Build 列表中（建议置顶）
3. 选择平台：Windows（x86_64）
4. `Build` 输出到指定目录

说明：
- `Assets/StreamingAssets/` 会被 Unity 自动打包到 `YourApp_Data/StreamingAssets/`
- 本项目工具栏图标依赖 `StreamingAssets/UIIcons/*.png`

### 2) 运行目录检查

发布后请确认：
- `YourApp.exe`
- `YourApp_Data/StreamingAssets/UIIcons/` 存在并含图标文件

### 3) 平台限制

文件对话框为 Win32 API 实现：
- Windows Editor/Windows Player 可用
- 非 Windows 平台需要替换为对应平台的文件选择实现

---

## 目录结构

```text
Assets/
  Scenes/
    3DOCX.unity               主场景
    InspectorPanel.uxml       UI Toolkit 布局（顶部工具栏/左右面板）
  Scripts/
    OcxSystemManager*.cs      导入解析、建模、对齐、导出、撤销、切面等核心逻辑
    FileBrowserUI.cs          顶部工具栏交互、导入进度、退出弹窗、快捷键
    PartListUI.cs             左侧板件列表与搜索定位
    MeasurementTool.cs        测距/测角/测圆弧与取点规则
    SelectionHighlighter.cs   选择高亮、Opening 红色实线圈与聚焦
    AxisWidgetUI.cs           右上角坐标轴 UI（随相机视角同步）
    LocalFileBrowser.cs       Windows 多选文件对话框（Win32）
  StreamingAssets/
    UIIcons/                  工具栏图标
    *.3docx/*.xml             示例数据（可直接导入测试）
```

---

## 常见问题

### 1) 点击“导入”没有反应 / 无法多选

- 仅 Windows 可用（使用 Win32 `Comdlg32`）
- 若是 Windows 仍无法打开，优先检查 Player Settings 的 API 兼容性或被系统策略限制

### 2) 测量点“点不上板”

测量模式下取点规则更严格：
- Plate/Panel：点必须投影落在板面边界内，且不能落入开孔区域
- Stiffener：允许直接取射线命中点

可先用“选择高亮”验证当前板/加劲材是否存在 Collider 与 `PartData`。

### 3) 导出后后缀不对 / 覆盖与另存为怎么切换

- 导出保持与源文件相同后缀：`.3docx/.xml/.ocx`
- 覆盖/另存为由 `overwriteOriginalOnExport` 决定
- 退出按钮会弹窗让你选择覆盖或另存为

### 4) Opening 选中提示不明显

- Opening 选中后会显示红色实线圈（基于 `PartData.Boundary`）
- 若 Boundary 为空或点数不足，会不显示圈（需要检查 Opening 建模是否完整）

### 5) 16mm/12mm 板厚看起来差不多（肉眼不明显）

可以通过全局显示倍率把模型整体放大（不会影响导出数据）：

- 在 Unity Inspector 中选中 `OcxSystemManager`，调整 `modelVisualScale`（默认 8）
- 注意：尺寸面板与测量结果会自动按比例还原为真实 mm 显示

### 6) 想要更明显的白色描边/黑色板缝线

线条宽度可在 Unity Inspector 中调整（挂在 `OcxSystemManager` 上）：

- 白色板件描边：`plateOutlineWidth`
- 黑色板缝线：`seamLineWidth`

---

## 开发者入口（代码位置）

- 核心系统： [OcxSystemManager.cs](file:///f:/Uinty%203D/chuanbokeshihua/Assets/Scripts/OcxSystemManager.cs)
- S1/S2 分支： [OcxSystemManager.S1.cs](file:///f:/Uinty%203D/chuanbokeshihua/Assets/Scripts/OcxSystemManager.S1.cs)、[OcxSystemManager.S2.cs](file:///f:/Uinty%203D/chuanbokeshihua/Assets/Scripts/OcxSystemManager.S2.cs)
- UI 工具栏/导入/退出： [FileBrowserUI.cs](file:///f:/Uinty%203D/chuanbokeshihua/Assets/Scripts/FileBrowserUI.cs)
- 测量： [MeasurementTool.cs](file:///f:/Uinty%203D/chuanbokeshihua/Assets/Scripts/MeasurementTool.cs)
- 选择高亮与开孔提示： [SelectionHighlighter.cs](file:///f:/Uinty%203D/chuanbokeshihua/Assets/Scripts/SelectionHighlighter.cs)
- 板件列表： [PartListUI.cs](file:///f:/Uinty%203D/chuanbokeshihua/Assets/Scripts/PartListUI.cs)
- UI 布局： [InspectorPanel.uxml](file:///f:/Uinty%203D/chuanbokeshihua/Assets/Scenes/InspectorPanel.uxml)
