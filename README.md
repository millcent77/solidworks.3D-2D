# SolidWorks Auto Drawing / SolidWorks 自动工程图

## 中文说明

### 1. 项目解决什么问题

本项目用于把 SolidWorks 三维零件文件自动转换为二维工程图，目标是减少人工反复打开零件、创建工程视图、导出 DWG/PDF 的重复工作。

项目当前处理的核心问题是：

1. 需要把 `.SLDPRT`、`.x_t`、`.x_b` 等 3D 文件转换为可查看、可继续编辑的 2D 工程图。
2. 当前程序必须依赖本机已安装 SolidWorks 才能运行，因为转换流程通过 SolidWorks COM API 完成。
3. 自动尺寸标注需要持续优化，避免重要尺寸漏标、无关尺寸过多、尺寸文字或尺寸线重叠。

程序输出包括：

- `.SLDDRW` 工程图
- `.PDF` 图纸文件
- `.DWG` 二维图文件

当前项目重点是稳定生成基础工程图、生成基础尺寸候选、支持人工确认后输出正式图纸。输出后仍需要工程师检查公差、形位公差、粗糙度、加工要求和关键制造尺寸。

### 2. 主要功能

- 监听输入目录并批量处理零件文件。
- 支持输入格式：
  - `.SLDPRT`
  - `.x_t`
  - `.x_b`
- 对 Parasolid 文件先导入为 SolidWorks 零件，再生成工程图。
- 自动创建标准工程视图：
  - 前视图
  - 上视图
  - 右视图
  - 等轴测视图
- 自动选择 A4/A3 图纸和基础比例。
- 输出：
  - `SLDDRW`
  - `PDF`
  - `DWG`
- 基础尺寸候选与标注能力：
  - 长度
  - 宽度
  - 高度
  - 直径
  - 半径 / R 角
  - 中心距
- 零件分类规则：
  - 冲压件
  - 机加工件
  - 铸件
  - 模具零件
  - 未分类保守模式
- 人工确认候选尺寸：
  - 绿色：建议保留
  - 黄色：需要人工确认
  - 红色：不建议使用
- 审核日志记录：
  - 推荐分类
  - 最终分类
  - 保留尺寸
  - 取消尺寸
  - 正式输出路径

### 3. 安装方法

#### 环境要求

- Windows 11 Pro 64 位
- .NET 8 SDK 或 .NET 8 Desktop Runtime
- Visual Studio 2026 或兼容的 Visual Studio 版本
- SolidWorks 2025
- 本机存在 SolidWorks Interop 组件：
  - `SolidWorks.Interop.sldworks.dll`
  - `SolidWorks.Interop.swconst.dll`

项目默认从以下位置引用 SolidWorks Interop：

```text
C:\Program Files\SolidWorks Corp\SolidWorks\
```

#### 编译

在项目根目录执行：

```powershell
cd D:\AutoDrawing\source\repos\AutoDrawingPro
dotnet build AutoDrawingPro.csproj -c Release -p:Platform=x64
```

编译成功后程序位于：

```text
D:\AutoDrawing\source\repos\AutoDrawingPro\bin\x64\Release\net8.0-windows\AutoDrawingPro.exe
```

### 4. 使用方法

1. 启动 SolidWorks 2025。
2. 启动 `AutoDrawingPro.exe`。
3. 在程序界面确认目录：

```text
监听输入目录: D:\AutoDrawingServer\Input
2D导出目录: D:\AutoDrawingServer\Output
图纸模板目录: D:\AutoDrawingServer\Templates
```

4. 将 `.SLDPRT`、`.x_t` 或 `.x_b` 文件放入监听输入目录。
5. 点击“启动监听”。
6. 程序创建工程图和候选尺寸。
7. 在候选尺寸确认界面选择零件分类和要保留的尺寸。
8. 点击“确认并生成正式图”。
9. 在输出目录查看正式文件和审核日志。

### 5. 输入输出示例

#### 输入示例

```text
D:\AutoDrawingServer\Input\Bracket.SLDPRT
D:\AutoDrawingServer\Input\Housing.x_t
D:\AutoDrawingServer\Input\MoldPlate.x_b
```

#### 输出示例

```text
D:\AutoDrawingServer\Output\SLDDRW\Bracket.slddrw
D:\AutoDrawingServer\Output\PDF\Bracket.pdf
D:\AutoDrawingServer\Output\DWG\Bracket.dwg
D:\AutoDrawingServer\Output\SLDPRT\Housing.sldprt
D:\AutoDrawingServer\Output\Audit\2026-09-16.csv
D:\AutoDrawingServer\Output\Logs\2026-09-16.log
```

> 注意：`.x_t` 和 `.x_b` 文件会先转换为中间 `.SLDPRT`，再用于创建工程图。

---

## English

### 1. What Problem This Project Solves

This project automatically converts SolidWorks 3D part files into 2D engineering drawings, reducing repetitive manual work such as opening parts, creating drawing views, and exporting DWG/PDF files.

The main problems addressed by this project are:

1. Converting `.SLDPRT`, `.x_t`, and `.x_b` 3D files into viewable and editable 2D engineering drawings.
2. Running the conversion through the local SolidWorks COM API, which means SolidWorks must be installed on the computer.
3. Improving automatic dimensioning so that important manufacturing dimensions are not missed, unnecessary dimensions are reduced, and dimension text or lines do not overlap.

The application exports:

- `.SLDDRW` drawing files
- `.PDF` drawing documents
- `.DWG` 2D CAD files

The current focus is stable drawing generation, basic dimension candidates, and engineer review before producing final drawings. The generated drawings still require engineering review for tolerances, GD&T, surface finish, manufacturing notes, and critical production dimensions.

### 2. Main Features

- Watches an input folder and processes part files in batches.
- Supported input formats:
  - `.SLDPRT`
  - `.x_t`
  - `.x_b`
- Imports Parasolid files into SolidWorks parts before drawing generation.
- Creates standard drawing views:
  - Front view
  - Top view
  - Right view
  - Isometric view
- Selects basic A4/A3 sheet size and drawing scale.
- Exports:
  - `SLDDRW`
  - `PDF`
  - `DWG`
- Basic dimension candidate and annotation support:
  - Length
  - Width
  - Height
  - Diameter
  - Radius / R corner
  - Center distance
- Part classification rules:
  - Stamping part
  - Machined part
  - Casting part
  - Mold component
  - Unclassified conservative mode
- Manual dimension review:
  - Green: recommended to keep
  - Yellow: requires engineer confirmation
  - Red: not recommended
- Audit log records:
  - Recommended category
  - Final category
  - Kept dimensions
  - Removed dimensions
  - Final output paths

### 3. Installation

#### Requirements

- Windows 11 Pro 64-bit
- .NET 8 SDK or .NET 8 Desktop Runtime
- Visual Studio 2026 or a compatible Visual Studio version
- SolidWorks 2025
- SolidWorks Interop assemblies installed locally:
  - `SolidWorks.Interop.sldworks.dll`
  - `SolidWorks.Interop.swconst.dll`

The project references SolidWorks Interop assemblies from:

```text
C:\Program Files\SolidWorks Corp\SolidWorks\
```

#### Build

Run the following commands from the project root:

```powershell
cd D:\AutoDrawing\source\repos\AutoDrawingPro
dotnet build AutoDrawingPro.csproj -c Release -p:Platform=x64
```

After a successful build, the executable is located at:

```text
D:\AutoDrawing\source\repos\AutoDrawingPro\bin\x64\Release\net8.0-windows\AutoDrawingPro.exe
```

### 4. Usage

1. Start SolidWorks 2025.
2. Start `AutoDrawingPro.exe`.
3. Confirm the folders in the application:

```text
Input folder: D:\AutoDrawingServer\Input
2D output folder: D:\AutoDrawingServer\Output
Drawing template folder: D:\AutoDrawingServer\Templates
```

4. Put `.SLDPRT`, `.x_t`, or `.x_b` files into the input folder.
5. Click “Start Monitoring”.
6. The application creates drawing views and dimension candidates.
7. In the dimension review window, choose the part category and dimensions to keep.
8. Click “Confirm and Generate Final Drawing”.
9. Check the output folder for final files and audit logs.

### 5. Input and Output Examples

#### Input Examples

```text
D:\AutoDrawingServer\Input\Bracket.SLDPRT
D:\AutoDrawingServer\Input\Housing.x_t
D:\AutoDrawingServer\Input\MoldPlate.x_b
```

#### Output Examples

```text
D:\AutoDrawingServer\Output\SLDDRW\Bracket.slddrw
D:\AutoDrawingServer\Output\PDF\Bracket.pdf
D:\AutoDrawingServer\Output\DWG\Bracket.dwg
D:\AutoDrawingServer\Output\SLDPRT\Housing.sldprt
D:\AutoDrawingServer\Output\Audit\2026-09-16.csv
D:\AutoDrawingServer\Output\Logs\2026-09-16.log
```

> Note: `.x_t` and `.x_b` files are imported and saved as intermediate `.SLDPRT` files before drawing generation.

## Disclaimer / 说明

The generated drawing is a reviewed basic drawing aid, not a complete manufacturing drawing. Engineers must still verify tolerances, GD&T, surface finish, manufacturing notes, and all critical dimensions.

生成结果是基础工程图辅助文件，不代表完整制造图纸。公差、形位公差、粗糙度、加工要求及所有关键尺寸仍必须由工程师确认。
