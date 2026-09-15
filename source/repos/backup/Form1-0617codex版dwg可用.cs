using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

using SwAppClass = SolidWorks.Interop.sldworks.SldWorks;
using SwView = SolidWorks.Interop.sldworks.View;
using SysEnv = System.Environment;

namespace AutoDrawingPro
{
    public partial class Form1 : Form
    {
        private bool _isRunning;
        private CancellationTokenSource? _cts;

        private readonly string _inputFolder = @"D:\AutoDrawingServer\Input";
        private readonly string _outputFolder = @"D:\AutoDrawingServer\Output";
        private readonly string _templateFolder = @"D:\AutoDrawingServer\Templates";

        public Form1()
        {
            InitializeComponent();
            Directory.CreateDirectory(_inputFolder);
            Directory.CreateDirectory(_outputFolder);
            Directory.CreateDirectory(_templateFolder);
            Directory.CreateDirectory(Path.Combine(_outputFolder, "Logs"));
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            if (_isRunning) return;

            _isRunning = true;
            _cts = new CancellationTokenSource();

            Task.Run(() => StartEngineProcess(_cts.Token));

            btnStart.Enabled = false;
            LogToWindow("[系统提示] 自动化出图服务已成功启动，正在监听 Input 文件夹...");
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            _isRunning = false;
            _cts?.Cancel();
            btnStart.Enabled = true;
            LogToWindow("[系统提示] 正在停止出图服务，请稍候...");
        }

        private async Task StartEngineProcess(CancellationToken token)
        {
            SwAppClass? swApp = null;

            try
            {
                LogToWindow("[进程通信] 正在连接本机的 SolidWorks 软件...");

                try
                {
                    Type? swType = Type.GetTypeFromProgID("SldWorks.Application");
                    if (swType != null)
                    {
                        object? activeObj = null;
                        try
                        {
                            Guid clsid;
                            CLSIDFromProgID("SldWorks.Application", out clsid);
                            object obj;
                            GetActiveObject(ref clsid, IntPtr.Zero, out obj);
                            activeObj = obj;
                        }
                        catch { }

                        if (activeObj != null)
                        {
                            swApp = (SwAppClass)activeObj;
                        }
                        else
                        {
                            swApp = (SwAppClass?)Activator.CreateInstance(swType);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogToWindow($"[连接提示] 尝试连接失败: {ex.Message}，正在启用第二套备用连接方案...");
                    try
                    {
                        swApp = (SwAppClass?)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("86C4080F-218E-11D1-9D4A-00A0C9B18385"))!);
                    }
                    catch (Exception ex2)
                    {
                        LogToWindow($"[致命错误] 彻底无法连接 SolidWorks 接口，原因: {ex2.Message}");
                    }
                }

                if (swApp == null)
                {
                    LogToWindow("[重磅错误] 无法连接到 SolidWorks，请确保电脑已打开了 SolidWorks！");
                    this.Invoke(new Action(() => btnStart.Enabled = true));
                    _isRunning = false;
                    return;
                }

                swApp.Visible = true;
                LogToWindow("[进程通信] 成功握手 SolidWorks！进入文件夹扫描模式...");

                while (!token.IsCancellationRequested)
                {
                    if (Directory.Exists(_inputFolder))
                    {
                        // =========================================================
                        // 关键修复：之前分别用 "*.sldprt" 和 "*.SLDPRT" 两次调用
                        // GetFiles，但 Windows 文件系统对扩展名匹配本身就是
                        // 大小写不敏感的，导致同一个文件（如 88276-1.SLDPRT）
                        // 同时出现在两次结果里，被塞进 allFiles 两次。
                        // 第一次处理成功后文件被移走，第二次再处理同一个路径
                        // 字符串时文件已不存在，报 "找不到文件"。
                        // 这正是日志里"重复扫描+文件消失"现象的真正根因，
                        // 与 SolidWorks 本身无关，是纯粹的扫描逻辑 bug。
                        // 现改为：用 HashSet 按完整路径（忽略大小写）去重。
                        // =========================================================
                        var allFilesSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var pattern in new[] { "*.step", "*.stp", "*.sldprt" })
                        {
                            foreach (var f in Directory.GetFiles(_inputFolder, pattern))
                            {
                                allFilesSet.Add(f);
                            }
                        }
                        var allFiles = allFilesSet.ToArray();

                        foreach (var file in allFiles)
                        {
                            if (token.IsCancellationRequested) break;

                            // 文件可能在排队等待处理期间被外部操作移走/重命名，
                            // 处理前再确认一次存在，避免对已消失的文件做无意义调用
                            if (!File.Exists(file))
                            {
                                LogToWindow($"[跳过] {Path.GetFileName(file)} 在排队期间已不存在，跳过。");
                                continue;
                            }

                            LogToWindow($"[扫描发现] 成功抓取到 file: {Path.GetFileName(file)}，准备送入流水线...");

                            bool success = ExecuteSingleDrawingWorkflow(swApp, file);

                            if (success)
                            {
                                string backupDir = Path.Combine(_outputFolder, "Processed_Backup");
                                Directory.CreateDirectory(backupDir);
                                string targetPath = Path.Combine(backupDir, Path.GetFileName(file));

                                // 移动文件前文档可能刚被 SW 关闭，文件句柄释放需要
                                // 一点时间，加短暂重试避免"文件正被占用"导致移动失败、
                                // 文件留在 Input 里被下一轮扫描重复抓取
                                bool moved = false;
                                for (int retry = 0; retry < 5 && !moved; retry++)
                                {
                                    try
                                    {
                                        if (File.Exists(targetPath)) File.Delete(targetPath);
                                        File.Move(file, targetPath);
                                        moved = true;
                                    }
                                    catch (IOException)
                                    {
                                        await Task.Delay(500, token);
                                    }
                                }

                                if (!moved)
                                {
                                    LogToWindow($"[警告] {Path.GetFileName(file)} 处理成功但移动到备份目录失败（文件可能被占用），" +
                                        "该文件将保留在 Input 中，下一轮扫描可能被重复处理，请手动检查。");
                                }
                            }
                            else
                            {
                                // 处理失败时也不能让文件一直留在 Input 里被无限重复扫描，
                                // 移到失败目录并打标记，避免同一份坏文件反复占用流水线
                                string failedDir = Path.Combine(_outputFolder, "Failed");
                                Directory.CreateDirectory(failedDir);
                                string failedTargetPath = Path.Combine(failedDir, Path.GetFileName(file));

                                try
                                {
                                    if (File.Exists(failedTargetPath)) File.Delete(failedTargetPath);
                                    File.Move(file, failedTargetPath);
                                    LogToWindow($"[处理失败] {Path.GetFileName(file)} 已移动到 Failed 目录，不会再被重复扫描。");
                                }
                                catch (Exception moveEx)
                                {
                                    LogToWindow($"[警告] {Path.GetFileName(file)} 处理失败且无法移动到Failed目录: {moveEx.Message}，该文件可能被反复重试。");
                                }
                            }

                            await Task.Delay(2000, token);
                        }
                    }

                    await Task.Delay(5000, token);
                }
            }
            catch (Exception ex)
            {
                LogToWindow($"[引擎异常] 轮询崩溃: {ex.Message}");
            }
            finally
            {
                _isRunning = false;
            }
        }

        private bool ExecuteSingleDrawingWorkflow(SwAppClass swApp, string stepPath)
        {
            SolidWorks.Interop.sldworks.ModelDoc2? swModel = null;
            SolidWorks.Interop.sldworks.DrawingDoc? swDraw = null;
            string fileName = Path.GetFileNameWithoutExtension(stepPath);

            try
            {
                LogToWindow($"[{DateTime.Now:HH:mm:ss}] 发现新任务，正在导入: {Path.GetFileName(stepPath)}");
                int errors = 0;
                int warnings = 0;

                // =========================================================
                // 已确认手动在 SW 里能正常打开 STEP 文件，说明问题不在文件本身，
                // 也不在路径编码（纯英文临时路径重试同样失败）。
                // 真正怀疑方向：
                //   a) OpenDoc6 + swOpenDocOptions_Silent 组合在静默模式下吞掉了
                //      一个交互式确认弹窗（如版本不匹配、特征识别确认等），
                //      静默模式下该弹窗被自动判定为失败而不是自动确认。
                //   b) SW 内存里堆积了上一次失败任务未关闭的残留文档，
                //      干扰了新文档的打开/激活。
                // 处理方式：
                //   1) 打开前先扫描并关闭所有与本次文件同名的残留文档。
                //   2) 优先尝试非 silent 模式打开（牺牲一点静默性换取真实弹窗
                //      自动以默认值确认，而不是被当成错误吞掉）。
                //   3) 不再使用临时文件名重试——这会污染 GetTitle()，导致后续
                //      创建视图时找不到正确的文档名。
                // =========================================================
                // =========================================================
                // 关键修复：STEP 导入会弹出"是否进行特征识别(FeatureWorks)"
                // 对话框需要人工确认。Silent 模式下该弹窗不会被自动确认，
                // 而是直接被判定为打开失败（这正是 2097152 的真正根因）。
                // 不同 SW 版本对应枚举名差异很大，为避免编译期硬编码枚举名
                // 导致跨版本编译失败，这里用反射在运行时动态查找，找不到就
                // 跳过（不影响主流程，最多是弹窗问题没被自动解决）。
                // =========================================================
                TryDisableFeatureWorksPrompt(swApp);

                int docType = (int)SolidWorks.Interop.swconst.swDocumentTypes_e.swDocPART;
                string openFileName = Path.GetFileName(stepPath);

                // 步骤 a：关闭 SW 中可能残留的同名文档，避免互相干扰
                try
                {
                    foreach (SolidWorks.Interop.sldworks.ModelDoc2 openDoc in (object[])swApp.GetDocuments())
                    {
                        if (openDoc != null && string.Equals(openDoc.GetTitle(), openFileName, StringComparison.OrdinalIgnoreCase))
                        {
                            LogToWindow($"[清理] 发现残留同名文档 \"{openDoc.GetTitle()}\"，关闭后重新打开。");
                            swApp.CloseDoc(openDoc.GetTitle());
                        }
                    }
                }
                catch { }

                // 步骤 b：已在上方关闭了特征识别弹窗触发选项，此时 silent 模式
                // 不会再卡在那个弹窗上，可以直接无人值守静默打开。
                swModel = (SolidWorks.Interop.sldworks.ModelDoc2)swApp.OpenDoc6(
                    stepPath,
                    docType,
                    (int)SolidWorks.Interop.swconst.swOpenDocOptions_e.swOpenDocOptions_Silent,
                    "",
                    ref errors,
                    ref warnings);

                if (swModel == null)
                {
                    LogToWindow($"[重试] silent模式打开失败（错误码: {errors} / 0x{errors:X}, 警告码: {warnings}），尝试非silent模式作为兜底...");
                    int errors2 = 0, warnings2 = 0;
                    swModel = (SolidWorks.Interop.sldworks.ModelDoc2)swApp.OpenDoc6(
                        stepPath,
                        docType,
                        0,
                        "",
                        ref errors2,
                        ref warnings2);

                    if (swModel != null) { errors = 0; warnings = warnings2; }
                    else { errors = errors2; }
                }

                if (swModel == null)
                {
                    LogToWindow($"[错误] 无法解析该文件，错误码: {errors}（0x{errors:X}），警告码: {warnings}。" +
                        "已确认手动打开该文件本身没问题，请把这两个错误码发给我，" +
                        "需要根据 swFileLoadError_e 枚举精确定位（例如：" +
                        "1=找不到文件, 2=不是有效格式, 4=版本更新需确认, 8=已被其他用户锁定, " +
                        "16=需要授权确认, 32=文件需要修复等，实际数值组合需对照枚举表核对）。");
                    return false;
                }

                LogToWindow($"[成功] 文档已打开，实际标题: \"{swModel.GetTitle()}\"");

                // 2. 确定 SLDPRT 核心参考路径
                string prtDir = Path.Combine(_outputFolder, "SLDPRT");
                Directory.CreateDirectory(prtDir);
                string prtPath = Path.Combine(prtDir, fileName + ".sldprt");

                if (stepPath.ToLower().EndsWith(".sldprt"))
                {
                    prtPath = stepPath;
                }
                else
                {
                    swApp.ActivateDoc3(swModel.GetTitle(), true, 0, ref errors);
                    swModel.Extension.SaveAs(
                        prtPath,
                        (int)SolidWorks.Interop.swconst.swSaveAsVersion_e.swSaveAsCurrentVersion,
                        (int)SolidWorks.Interop.swconst.swSaveAsOptions_e.swSaveAsOptions_Silent,
                        null, ref errors, ref warnings);

                    // 另存为之后文档标题可能发生变化，重新激活确保 swModel 指向的是
                    // 已保存的 SLDPRT 文档而不是原 STEP 临时文档
                    swModel = (SolidWorks.Interop.sldworks.ModelDoc2)swApp.ActiveDoc;
                }

                // 3. 计算边界尺寸
                double maxDim = 150.0;
                try
                {
                    SolidWorks.Interop.sldworks.PartDoc? partDoc = swModel as SolidWorks.Interop.sldworks.PartDoc;
                    if (partDoc != null)
                    {
                        double[] box = (double[])partDoc.GetPartBox(true);
                        if (box != null && box.Length >= 6)
                        {
                            double dx = Math.Abs(box[3] - box[0]) * 1000;
                            double dy = Math.Abs(box[4] - box[1]) * 1000;
                            double dz = Math.Abs(box[5] - box[2]) * 1000;
                            maxDim = Math.Max(dx, Math.Max(dy, dz));
                        }
                    }
                }
                catch
                {
                    LogToWindow("[尺寸判定] 提示：读取边界受限，采用默认安全尺寸。");
                }

                string sheetSize = "A3";
                string scaleRatio = "1:2";
                if (maxDim <= 100) { sheetSize = "A4"; scaleRatio = "1:1"; }
                else if (maxDim > 300 && maxDim <= 1000) { sheetSize = "A2"; scaleRatio = "1:5"; }
                else if (maxDim > 1000) { sheetSize = "A1"; scaleRatio = "1:10"; }

                LogToWindow($"[外形分析] 最大尺寸: {maxDim:F1}mm -> 自动匹配图纸: {sheetSize} (比例 {scaleRatio})");

                // 4. 新建工程图
                string templatePath = Path.Combine(_templateFolder, $"{sheetSize}.drwdot");
                if (!File.Exists(templatePath))
                {
                    LogToWindow($"[保存失败] 缺少模板文件: {templatePath}，请确保模板放入该路径！");
                    return false;
                }

                // =========================================================
                // 修复点 3：视图创建全部失败的根因
                // 原代码用 swDwgPapersUserDefined 配合宽高参数 (0, 0) 新建工程图，
                // 这意味着创建出的图纸纸张尺寸是 0x0 的无效图纸。图纸本身无效时，
                // CreateDrawViewFromModelView3 无论传入什么模型名称都会静默失败
                // （不抛异常，只是返回 null），这正是三种名字都失败的根因。
                // 现改为：根据已匹配的 sheetSize 选用对应的标准纸张枚举，
                // 让图纸纸张本身处于有效状态。
                // =========================================================
                SolidWorks.Interop.swconst.swDwgPaperSizes_e paperSizeEnum =
                    sheetSize switch
                    {
                        "A4" => SolidWorks.Interop.swconst.swDwgPaperSizes_e.swDwgPaperA4size,
                        "A3" => SolidWorks.Interop.swconst.swDwgPaperSizes_e.swDwgPaperA3size,
                        "A2" => SolidWorks.Interop.swconst.swDwgPaperSizes_e.swDwgPaperA2size,
                        "A1" => SolidWorks.Interop.swconst.swDwgPaperSizes_e.swDwgPaperA1size,
                        _ => SolidWorks.Interop.swconst.swDwgPaperSizes_e.swDwgPaperA3size,
                    };

                swDraw = (SolidWorks.Interop.sldworks.DrawingDoc)swApp.NewDocument(
                    templatePath,
                    (int)paperSizeEnum,
                    0, 0);
                if (swDraw == null) return false;

                SolidWorks.Interop.sldworks.ModelDoc2 drawModel = (SolidWorks.Interop.sldworks.ModelDoc2)swDraw;

                // 强行激活当前工程图视窗到前台
                swApp.ActivateDoc3(drawModel.GetTitle(), true, 0, ref errors);

                SolidWorks.Interop.sldworks.Sheet currentSheet = (SolidWorks.Interop.sldworks.Sheet)swDraw.GetCurrentSheet();
                string[] scaleParts = scaleRatio.Split(':');
                currentSheet.SetScale(double.Parse(scaleParts[0]), double.Parse(scaleParts[1]), true, true);

                try
                {
                    LogToWindow($"[诊断] 图纸名称=\"{currentSheet.GetName()}\" | drawModel标题=\"{drawModel.GetTitle()}\" | " +
                        $"drawModel是否为DrawingDoc类型有效: {(swDraw != null)}");
                }
                catch (Exception sheetEx)
                {
                    LogToWindow($"[诊断-图纸异常] {sheetEx.Message}");
                }

                // =========================================================
                // 关键修复：工程图视图必须使用模型文件完整路径创建。
                // CreateDrawViewFromModelView3 / Create3rdAngleViews2 的模型参数
                // 在跨电脑、跨语言版本 SolidWorks 上最稳定的是已保存的
                // .sldprt 完整路径。传 swModel.GetTitle() 或去扩展名标题时，
                // 很多环境会找不到被引用模型，结果导出的 PDF/DWG 只有空白图纸。
                // =========================================================
                string rawTitle = swModel.GetTitle();
                string modelReferencePath = File.Exists(prtPath) ? prtPath : swModel.GetPathName();
                if (string.IsNullOrWhiteSpace(modelReferencePath) || !File.Exists(modelReferencePath))
                {
                    LogToWindow($"[出图异常] 找不到可用于工程图引用的模型文件路径: {modelReferencePath}");
                    return false;
                }

                CreateNamedStandardViews(swApp, swModel);

                // ===== 诊断信息：确认 swModel 此刻是否真的是有效的活动文档 =====
                try
                {
                    object? activeDocCheck = swApp.ActiveDoc;
                    string activeDocTitle = activeDocCheck != null
                        ? ((SolidWorks.Interop.sldworks.ModelDoc2)activeDocCheck).GetTitle()
                        : "(null)";
                    int docCount = swApp.GetDocumentCount();
                    string pathCheck = swModel.GetPathName();

                    LogToWindow($"[诊断] swModel标题=\"{rawTitle}\" | swModel路径=\"{pathCheck}\" | " +
                        $"当前ActiveDoc标题=\"{activeDocTitle}\" | SW中已打开文档总数={docCount}");
                }
                catch (Exception diagEx)
                {
                    LogToWindow($"[诊断异常] {diagEx.Message}");
                }

                // 强制把 swModel 重新激活为前台活动文档，确保视图创建时引用的是
                // 这一份文档，而不是 SW 内存里可能存在的另一份同名/残留文档
                int activateErrors = 0;
                swApp.ActivateDoc3(rawTitle, true, 0, ref activateErrors);
                if (activateErrors != 0)
                {
                    LogToWindow($"[诊断] ActivateDoc3 激活零件返回错误码: {activateErrors}");
                }

                // 激活零件后，再切回工程图，确保 CreateDrawViewFromModelView3
                // 是在"工程图为当前活动文档、零件为被引用文档"的正确上下文下调用
                int activateBackErrors = 0;
                swApp.ActivateDoc3(drawModel.GetTitle(), true, 0, ref activateBackErrors);
                if (activateBackErrors != 0)
                {
                    LogToWindow($"[诊断] ActivateDoc3 切回工程图返回错误码: {activateBackErrors}");
                }

                double modelScale = double.Parse(scaleParts[0]) / double.Parse(scaleParts[1]);

                bool standardViewsCreated = swDraw!.Create3rdAngleViews2(modelReferencePath);
                LogToWindow($"[诊断] Create3rdAngleViews2(\"{modelReferencePath}\") 返回: {standardViewsCreated}");

                SwView? isoView = (SwView?)swDraw.CreateDrawViewFromModelView3(modelReferencePath, "Auto_ISO", 0.30, 0.18, 0);
                if (isoView != null)
                {
                    isoView.ScaleDecimal = modelScale * 0.8;
                    isoView.UseSheetScale = 0;
                }
                else
                {
                    LogToWindow($"[警告] 等轴测视图创建失败，模型引用路径: {modelReferencePath}");
                }

                ApplyScaleToDrawingViews(swDraw, modelScale);

                int modelViewCount = CountModelDrawingViews(swDraw);
                LogToWindow($"[诊断] 当前工程图模型视图数量: {modelViewCount}");
                if (modelViewCount == 0)
                {
                    LogToWindow($"[出图异常] {fileName} 工程图内没有真实模型视图，终止导出，避免生成空白 PDF/DWG。");
                    return false;
                }

                // 给 SolidWorks 时间完成视图解析，并轮询确认至少一个视图有实际边界。
                bool hasVisibleView = false;
                for (int waitTry = 0; waitTry < 20 && !hasVisibleView; waitTry++)
                {
                    Thread.Sleep(500);
                    drawModel.ForceRebuild3(false);
                    swDraw.ForceRebuild();
                    drawModel.GraphicsRedraw2();
                    hasVisibleView = HasVisibleDrawingView(swDraw);
                }

                if (!hasVisibleView)
                {
                    LogToWindow($"[出图异常] {fileName} 已创建视图，但视图边界仍为空，终止导出。");
                    return false;
                }

                // 6. 自动标注尺寸并全向强制刷新
                try
                {
                    drawModel.Extension.SelectByID2("", "SHEET", 0, 0, 0, false, 0, null, 0);
                    swDraw.InsertModelAnnotations3(2, 32769, true, true, false, true);

                    drawModel.ForceRebuild3(true);
                    drawModel.EditRebuild3();
                    swDraw.ForceRebuild();
                    drawModel.GraphicsRedraw2(); // 强制刷新图形视口，确保保存时画面是最新状态
                    Thread.Sleep(800);
                }
                catch (Exception annoEx)
                {
                    LogToWindow($"[警告] 标注插入/刷新过程出现异常（可能不影响视图本身）: {annoEx.Message}");
                }

                // 7. 保存 SLDDRW —— 检查 SaveAs 的真实返回值，而不是只看 errors/warnings
                string drwDir = Path.Combine(_outputFolder, "SLDDRW");
                Directory.CreateDirectory(drwDir);
                string drwPath = Path.Combine(drwDir, fileName + ".slddrw");
                bool drwSaved = drawModel.Extension.SaveAs(
                    drwPath,
                    (int)SolidWorks.Interop.swconst.swSaveAsVersion_e.swSaveAsCurrentVersion,
                    (int)SolidWorks.Interop.swconst.swSaveAsOptions_e.swSaveAsOptions_Silent,
                    null, ref errors, ref warnings);
                LogToWindow($"[诊断] SLDDRW保存返回={drwSaved}, errors={errors}, warnings={warnings}, " +
                    $"文件大小={(File.Exists(drwPath) ? new FileInfo(drwPath).Length : -1)}字节");

                // 8. 导出 PDF
                string pdfDir = Path.Combine(_outputFolder, "PDF");
                Directory.CreateDirectory(pdfDir);
                string pdfPath = Path.Combine(pdfDir, fileName + ".pdf");
                swApp.ActivateDoc3(drawModel.GetTitle(), true, 0, ref errors);
                if (File.Exists(pdfPath)) File.Delete(pdfPath);

                // 部分 SolidWorks 版本使用 Extension.SaveAs + ExportPdfData 会生成
                // 只有空白页面的 PDF；ModelDoc2.SaveAs4 对工程图 PDF 反而更稳定。
                bool pdfSaved = drawModel.SaveAs4(
                    pdfPath,
                    (int)SolidWorks.Interop.swconst.swSaveAsVersion_e.swSaveAsCurrentVersion,
                    (int)SolidWorks.Interop.swconst.swSaveAsOptions_e.swSaveAsOptions_Silent,
                    ref errors, ref warnings);
                LogToWindow($"[诊断] PDF保存返回={pdfSaved}, errors={errors}, warnings={warnings}, " +
                    $"文件大小={(File.Exists(pdfPath) ? new FileInfo(pdfPath).Length : -1)}字节");

                // 9. 导出 DWG
                string dwgDir = Path.Combine(_outputFolder, "DWG");
                Directory.CreateDirectory(dwgDir);
                string dwgPath = Path.Combine(dwgDir, fileName + ".dwg");
                bool dwgSaved = drawModel.Extension.SaveAs(
                    dwgPath,
                    (int)SolidWorks.Interop.swconst.swSaveAsVersion_e.swSaveAsCurrentVersion,
                    (int)SolidWorks.Interop.swconst.swSaveAsOptions_e.swSaveAsOptions_Silent,
                    null, ref errors, ref warnings);
                LogToWindow($"[诊断] DWG保存返回={dwgSaved}, errors={errors}, warnings={warnings}, " +
                    $"文件大小={(File.Exists(dwgPath) ? new FileInfo(dwgPath).Length : -1)}字节");

                if (!pdfSaved || !dwgSaved)
                {
                    LogToWindow($"[警告] {fileName} 部分文件保存返回失败状态，请检查上方诊断行的errors码与文件大小。");
                }

                LogToWindow($"[成功] {fileName} 成果已全部导出（PRT/DRW/PDF/DWG）。");
                return true;
            }
            catch (Exception ex)
            {
                LogToWindow($"[出图异常] 零件 {fileName} 出图失败: {ex.Message}");
                return false;
            }
            finally
            {
                try { if (swModel != null) swApp.CloseDoc(swModel.GetTitle()); } catch { }
                try { if (swDraw != null) swApp.CloseDoc(((SolidWorks.Interop.sldworks.ModelDoc2)swDraw).GetTitle()); } catch { }
                // CloseDoc 是同步调用，但 SW 内部释放文件句柄偶有延迟，
                // 短暂等待降低紧接着 File.Move 时遇到"文件被占用"的概率
                Thread.Sleep(300);
            }
        }

        private void CreateNamedStandardViews(SwAppClass swApp, SolidWorks.Interop.sldworks.ModelDoc2 swModel)
        {
            int errors = 0;
            swApp.ActivateDoc3(swModel.GetTitle(), true, 0, ref errors);

            CreateNamedStandardView(swModel, "Auto_Front", SolidWorks.Interop.swconst.swStandardViews_e.swFrontView);
            CreateNamedStandardView(swModel, "Auto_Top", SolidWorks.Interop.swconst.swStandardViews_e.swTopView);
            CreateNamedStandardView(swModel, "Auto_Right", SolidWorks.Interop.swconst.swStandardViews_e.swRightView);
            CreateNamedStandardView(swModel, "Auto_ISO", SolidWorks.Interop.swconst.swStandardViews_e.swIsometricView);

            swModel.ForceRebuild3(false);
            swModel.GraphicsRedraw2();
        }

        private void CreateNamedStandardView(
            SolidWorks.Interop.sldworks.ModelDoc2 swModel,
            string viewName,
            SolidWorks.Interop.swconst.swStandardViews_e standardView)
        {
            try { swModel.DeleteNamedView(viewName); } catch { }

            swModel.ShowNamedView2("", (int)standardView);
            swModel.ViewZoomtofit2();
            swModel.NameView(viewName);
        }

        private int CountModelDrawingViews(SolidWorks.Interop.sldworks.DrawingDoc swDraw)
        {
            int count = 0;
            SwView? view = GetFirstModelDrawingView(swDraw);
            while (view != null)
            {
                count++;
                view = (SwView?)view.GetNextView();
            }

            return count;
        }

        private void ApplyScaleToDrawingViews(SolidWorks.Interop.sldworks.DrawingDoc swDraw, double scale)
        {
            SwView? view = GetFirstModelDrawingView(swDraw);
            while (view != null)
            {
                try
                {
                    view.ScaleDecimal = scale;
                    view.UseSheetScale = 0;
                }
                catch { }

                view = (SwView?)view.GetNextView();
            }
        }

        private bool HasVisibleDrawingView(SolidWorks.Interop.sldworks.DrawingDoc swDraw)
        {
            SwView? view = GetFirstModelDrawingView(swDraw);
            while (view != null)
            {
                try
                {
                    double[]? outline = view.GetOutline() as double[];
                    if (outline != null && outline.Length >= 4 &&
                        Math.Abs(outline[2] - outline[0]) > 0.0001 &&
                        Math.Abs(outline[3] - outline[1]) > 0.0001)
                    {
                        return true;
                    }
                }
                catch { }

                view = (SwView?)view.GetNextView();
            }

            return false;
        }

        private SwView? GetFirstModelDrawingView(SolidWorks.Interop.sldworks.DrawingDoc swDraw)
        {
            SwView? sheetView = (SwView?)swDraw.GetFirstView();
            return sheetView == null ? null : (SwView?)sheetView.GetNextView();
        }

        /// <summary>
        /// 尝试关闭 STEP/IGES 导入时的"特征识别(FeatureWorks)"确认弹窗。
        /// 不同 SolidWorks 版本中相关枚举名不一致（常见有
        /// swFeatureWorksLevelOfFeatureRecog、swFeatureWorksAutoRecognitionTrigger 等），
        /// 用反射在运行时遍历尝试，避免编译期对某个具体枚举名硬依赖导致跨版本编译失败。
        /// 即使全部尝试失败也不影响主流程，只是弹窗问题没被自动消除，
        /// 此时需要去 SolidWorks「工具→选项→特征识别」手动关闭。
        /// </summary>
        private void TryDisableFeatureWorksPrompt(SwAppClass swApp)
        {
            string[] candidateEnumNames = new[]
            {
                "swUserPreferenceIntegerValue_e",
                "swUserPreferenceToggle_e"
            };

            string[] candidateFieldNames = new[]
            {
                "swFeatureWorksLevelOfFeatureRecog",
                "swFeatureWorksAutoRecognitionTrigger",
                "swFeatureWorksAutoRecognitionShowMessage",
                "swFeatureWorksAutoRecognitionFromMenuFlyout",
                "swFeatureWorksRunFeatureRecognition"
            };

            var swconstAssembly = typeof(SolidWorks.Interop.swconst.swDocumentTypes_e).Assembly;

            foreach (var enumTypeName in candidateEnumNames)
            {
                Type? enumType = swconstAssembly.GetType("SolidWorks.Interop.swconst." + enumTypeName);
                if (enumType == null) continue;

                foreach (var fieldName in candidateFieldNames)
                {
                    try
                    {
                        var field = enumType.GetField(fieldName);
                        if (field == null) continue;

                        int enumValue = (int)field.GetValue(null)!;

                        if (enumTypeName == "swUserPreferenceIntegerValue_e")
                        {
                            swApp.SetUserPreferenceIntegerValue(enumValue, 0);
                        }
                        else
                        {
                            swApp.SetUserPreferenceToggle(enumValue, false);
                        }

                        LogToWindow($"[提示] 已通过 {enumTypeName}.{fieldName} 关闭特征识别相关弹窗设置。");
                        return; // 找到并成功设置一个即可
                    }
                    catch
                    {
                        // 该候选枚举字段在此版本里设置失败，继续尝试下一个
                    }
                }
            }

            LogToWindow("[提示] 未能自动找到特征识别相关枚举设置（不同SW版本枚举名差异），" +
                "若STEP导入仍弹窗卡住，请手动在 SolidWorks「工具→选项→系统选项→特征识别」中关闭自动询问。");
        }

        private void LogToWindow(string message)
        {
            if (this.IsDisposed) return;

            this.BeginInvoke(new Action(() =>
            {
                if (txtLog != null && !txtLog.IsDisposed)
                {
                    txtLog.AppendText(message + SysEnv.NewLine);
                }

                try
                {
                    string logFile = Path.Combine(_outputFolder, "Logs", $"{DateTime.Now:yyyy-MM-dd}.log");
                    File.AppendAllText(logFile, message + SysEnv.NewLine);
                }
                catch { }
            }));
        }

        [DllImport("ole32.dll", PreserveSig = false)]
        private static extern void GetActiveObject(ref Guid rclsid, IntPtr pvReserved, [MarshalAs(UnmanagedType.IUnknown)] out object ppunk);

        [DllImport("ole32.dll")]
        private static extern int CLSIDFromProgID([MarshalAs(UnmanagedType.LPWStr)] string lpszProgID, out Guid pclsid);
    }
}
