using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        private AppSettings _settings = AppSettings.Load();

        private string InputFolder => _settings.InputFolder;
        private string OutputFolder => _settings.OutputFolder;
        private string TemplateFolder => _settings.TemplateFolder;

        public Form1()
        {
            InitializeComponent();
            LoadSettingsToForm();
            EnsureWorkingFolders();
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            if (_isRunning) return;

            SaveSettingsFromForm();
            EnsureWorkingFolders();

            _isRunning = true;
            _cts = new CancellationTokenSource();

            Task.Run(() => StartEngineProcess(_cts.Token));

            btnStart.Enabled = false;
            LogToWindow($"[系统提示] 自动化出图服务已成功启动，正在监听: {InputFolder}");
            LogToWindow($"[系统提示] 图纸输出目录: {OutputFolder}");
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
                    if (Directory.Exists(InputFolder))
                    {
                        var allFilesSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var pattern in new[] { "*.sldprt", "*.x_t", "*.x_b", "*.step", "*.stp" })
                        {
                            foreach (var f in Directory.GetFiles(InputFolder, pattern))
                            {
                                if (IsSupportedTaskFile(f))
                                {
                                    allFilesSet.Add(f);
                                }
                            }
                        }
                        var allFiles = allFilesSet.ToArray();

                        foreach (var file in allFiles)
                        {
                            if (token.IsCancellationRequested) break;

                            if (!File.Exists(file))
                            {
                                LogToWindow($"[跳过] {Path.GetFileName(file)} 在排队期间已不存在，跳过。");
                                continue;
                            }

                            if (!WaitForFileReady(file, token))
                            {
                                LogToWindow($"[跳过] {Path.GetFileName(file)} 文件仍在复制或被占用，稍后重试。");
                                continue;
                            }

                            LogToWindow($"[扫描发现] 成功抓取到 file: {Path.GetFileName(file)}，准备送入流水线...");

                            bool success = ExecuteSingleDrawingWorkflow(swApp, file);

                            if (success)
                            {
                                string backupDir = Path.Combine(OutputFolder, "Processed_Backup");
                                Directory.CreateDirectory(backupDir);
                                string targetPath = Path.Combine(backupDir, Path.GetFileName(file));

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
                                    LogToWindow($"[警告] {Path.GetFileName(file)} 处理成功但移动到备份目录失败，请手动检查。");
                                }
                            }
                            else
                            {
                                string failedDir = Path.Combine(OutputFolder, "Failed");
                                Directory.CreateDirectory(failedDir);
                                string failedTargetPath = Path.Combine(failedDir, Path.GetFileName(file));

                                try
                                {
                                    if (File.Exists(failedTargetPath)) File.Delete(failedTargetPath);
                                    File.Move(file, failedTargetPath);
                                    LogToWindow($"[处理失败] {Path.GetFileName(file)} 已移动到 Failed 目录。");
                                }
                                catch (Exception moveEx)
                                {
                                    LogToWindow($"[警告] {Path.GetFileName(file)} 处理失败且无法移动到Failed目录: {moveEx.Message}");
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

        private bool ExecuteSingleDrawingWorkflow(SwAppClass swApp, string inputPath)
        {
            SolidWorks.Interop.sldworks.ModelDoc2? swModel = null;
            SolidWorks.Interop.sldworks.DrawingDoc? swDraw = null;
            string fileName = Path.GetFileNameWithoutExtension(inputPath);
            Stopwatch stopwatch = Stopwatch.StartNew();

            try
            {
                LogToWindow($"[{DateTime.Now:HH:mm:ss}] 发现新任务，正在处理: {Path.GetFileName(inputPath)}");
                TryDisableFeatureWorksPrompt(swApp);

                ModelPreparationResult preparedModel = PrepareModelForDrawing(swApp, inputPath);
                swModel = preparedModel.Model;

                LogToWindow($"[模型检查] 类型={preparedModel.InputKind} 实体={preparedModel.BodyCount} 曲面={preparedModel.SurfaceCount} 最大尺寸={preparedModel.MaxDimensionMm:F1}mm 引用={preparedModel.ModelReferencePath}");
                if (preparedModel.BodyCount == 0 && preparedModel.SurfaceCount == 0)
                {
                    LogToWindow("[模型检查] 文件没有有效几何体，跳过当前文件。");
                    return false;
                }

                DrawingSheetPlan sheetPlan = SelectDrawingSheetPlan(preparedModel.MaxDimensionMm);
                string templatePath = ResolveDrawingTemplatePath(swApp, sheetPlan.SheetSize);
                if (string.IsNullOrWhiteSpace(templatePath) || !File.Exists(templatePath))
                {
                    LogToWindow($"[模板错误] 找不到可用工程图模板，纸张={sheetPlan.SheetSize}，模板目录={TemplateFolder}");
                    return false;
                }

                LogToWindow($"[出图设置] 模板={templatePath} 纸张={sheetPlan.SheetSize} 比例={sheetPlan.ScaleText} 主视方向=Front");

                swDraw = CreateDrawingDocument(swApp, templatePath, sheetPlan.SheetSize);
                if (swDraw == null)
                {
                    LogToWindow("[出图异常] NewDocument 未返回工程图对象。");
                    return false;
                }

                SolidWorks.Interop.sldworks.ModelDoc2 drawModel = (SolidWorks.Interop.sldworks.ModelDoc2)swDraw;
                int errors = 0;
                swApp.ActivateDoc3(drawModel.GetTitle(), true, (int)SolidWorks.Interop.swconst.swRebuildOnActivation_e.swRebuildActiveDoc, ref errors);

                SolidWorks.Interop.sldworks.Sheet currentSheet = (SolidWorks.Interop.sldworks.Sheet)swDraw.GetCurrentSheet();
                currentSheet.SetScale(sheetPlan.ScaleNumerator, sheetPlan.ScaleDenominator, true, true);

                CreateNamedStandardViews(swApp, swModel);
                swApp.ActivateDoc3(drawModel.GetTitle(), true, (int)SolidWorks.Interop.swconst.swRebuildOnActivation_e.swRebuildActiveDoc, ref errors);

                InsertStandardDrawingViews(swDraw, preparedModel.ModelReferencePath, sheetPlan.SheetSize, sheetPlan.ScaleDecimal);

                if (!WaitForDrawingViewsReady(swApp, swDraw, drawModel, TimeSpan.FromSeconds(12)))
                {
                    LogDrawingViewDiagnostics(swDraw);
                    LogToWindow($"[出图异常] {fileName} 视图未在超时内加载出可见轮廓，终止保存。");
                    return false;
                }

                LogDrawingViewDiagnostics(swDraw);

                PartCategory recommendedCategory = PartClassificationRules.RecommendCategory(
                    inputPath,
                    preparedModel.BodyCount,
                    preparedModel.SurfaceCount);
                PartCategory finalCategory = recommendedCategory;
                List<DimensionCandidate> reviewedCandidates = new();
                BasicDimensionResult dimensionResult = new();

                if (_settings.EnableBasicDimensions)
                {
                    BasicDimensionService dimensionService = new(_settings, LogToWindow);
                    IReadOnlyDictionary<PartCategory, PartDimensionRule> rules = PartClassificationRules.CreateDefaultRules(_settings);
                    PartDimensionRule initialRule = rules[recommendedCategory];
                    reviewedCandidates = dimensionService.BuildDimensionCandidates(swModel, swDraw, preparedModel.InputKind, initialRule);

                    bool confirmed = ReviewDimensionCandidates(
                        reviewedCandidates,
                        rules,
                        recommendedCategory,
                        Path.Combine(OutputFolder, "Drafts"),
                        out finalCategory);

                    if (!confirmed)
                    {
                        LogToWindow("[人工确认] 工程师取消确认，未生成正式 SLDDRW/PDF/DWG。");
                        return false;
                    }

                    dimensionResult = dimensionService.ApplyConfirmedDimensions(swApp, swDraw, drawModel, reviewedCandidates);
                }
                else
                {
                    LogToWindow("[基础尺寸] EnableBasicDimensions=false，跳过阶段二基础尺寸。");
                }

                if (_settings.EnableAutomaticDimensions)
                {
                    LogToWindow("[旧尺寸注入] 检测到 EnableAutomaticDimensions=true，但阶段二禁止批量导入模型项目，已强制跳过旧接口。");
                }
                else
                {
                    LogToWindow("[旧尺寸注入] 批量模型尺寸导入接口已关闭。");
                }

                string drwPath = SaveSlddrw(swApp, drawModel, fileName);
                string pdfPath = SavePdf(swApp, drawModel, fileName);
                string dwgPath = SaveDwg(swApp, drawModel, fileName);
                WriteReviewAuditLog(
                    inputPath,
                    recommendedCategory,
                    finalCategory,
                    reviewedCandidates,
                    dimensionResult,
                    drwPath,
                    pdfPath,
                    dwgPath);

                bool success = ValidateOutputFile("SLDDRW", drwPath, 1024)
                    && ValidatePdfFile(pdfPath)
                    && ValidateOutputFile("DWG", dwgPath, 1024);

                if (!success)
                {
                    LogToWindow($"[流水线失败] {fileName} 已生成部分文件，但输出验证未通过。耗时={stopwatch.Elapsed}");
                    return false;
                }

                LogToWindow($"[流水线成功] {fileName} 已稳定生成 SLDDRW/PDF/DWG，耗时={stopwatch.Elapsed}");
                LogToWindow("[人工检查提示] 工程图已完成基础尺寸审核，但公差、形位公差、粗糙度及制造要求仍需工程师检查。");
                return true;
            }
            catch (Exception ex)
            {
                LogToWindow($"[出图异常] 零件 {fileName} 出图失败: {ex.Message}");
                return false;
            }
            finally
            {
                try { if (swDraw != null) swApp.CloseDoc(((SolidWorks.Interop.sldworks.ModelDoc2)swDraw).GetTitle()); } catch { }
                try { if (swModel != null) swApp.CloseDoc(swModel.GetTitle()); } catch { }
                Thread.Sleep(300);
            }
        }

        private bool ReviewDimensionCandidates(
            List<DimensionCandidate> candidates,
            IReadOnlyDictionary<PartCategory, PartDimensionRule> rules,
            PartCategory recommendedCategory,
            string draftFolder,
            out PartCategory finalCategory)
        {
            PartCategory selectedCategory = recommendedCategory;
            bool confirmed = false;

            void ShowReview()
            {
                using DimensionReviewForm form = new(
                    candidates,
                    rules,
                    recommendedCategory,
                    draftFolder,
                    LogToWindow);

                DialogResult result = form.ShowDialog(this);
                confirmed = result == DialogResult.OK && form.Confirmed;
                selectedCategory = form.SelectedCategory;
            }

            if (InvokeRequired)
            {
                Invoke(new Action(ShowReview));
            }
            else
            {
                ShowReview();
            }

            finalCategory = selectedCategory;
            LogToWindow($"[人工确认] 推荐分类={PartClassificationRules.GetDisplayName(recommendedCategory)}，最终分类={PartClassificationRules.GetDisplayName(finalCategory)}，确认={confirmed}");
            return confirmed;
        }

        private void WriteReviewAuditLog(
            string inputPath,
            PartCategory recommendedCategory,
            PartCategory finalCategory,
            List<DimensionCandidate> candidates,
            BasicDimensionResult dimensionResult,
            string drwPath,
            string pdfPath,
            string dwgPath)
        {
            try
            {
                string auditDir = Path.Combine(OutputFolder, "Audit");
                Directory.CreateDirectory(auditDir);
                string auditPath = Path.Combine(auditDir, $"{DateTime.Now:yyyy-MM-dd}.csv");
                bool writeHeader = !File.Exists(auditPath);

                int green = candidates.Count(c => c.ReviewStatus == DimensionReviewStatus.Green);
                int yellow = candidates.Count(c => c.ReviewStatus == DimensionReviewStatus.Yellow);
                int red = candidates.Count(c => c.ReviewStatus == DimensionReviewStatus.Red);
                string kept = string.Join(" | ", candidates.Where(c => c.IsSelected).Select(c => $"{c.DimensionType}:{c.ValueMm:F2}{c.Unit}@{c.ViewName}"));
                string cancelled = string.Join(" | ", candidates.Where(c => !c.IsSelected).Select(c => $"{c.DimensionType}:{c.ValueMm:F2}{c.Unit}@{c.ViewName}"));
                string operatorName = Environment.UserName;

                using StreamWriter writer = new(auditPath, append: true, System.Text.Encoding.UTF8);
                if (writeHeader)
                {
                    writer.WriteLine("时间,操作人员,文件名,推荐分类,最终分类,绿色数量,黄色数量,红色数量,候选数量,最终保留数量,实际创建数量,保留尺寸,取消尺寸,SLDDRW,PDF,DWG");
                }

                writer.WriteLine(string.Join(",",
                    Csv(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")),
                    Csv(operatorName),
                    Csv(inputPath),
                    Csv(PartClassificationRules.GetDisplayName(recommendedCategory)),
                    Csv(PartClassificationRules.GetDisplayName(finalCategory)),
                    green,
                    yellow,
                    red,
                    candidates.Count,
                    candidates.Count(c => c.IsSelected),
                    dimensionResult.CreatedCount,
                    Csv(kept),
                    Csv(cancelled),
                    Csv(drwPath),
                    Csv(pdfPath),
                    Csv(dwgPath)));

                LogToWindow($"[审核记录] 已写入: {auditPath}");
            }
            catch (Exception ex)
            {
                LogToWindow($"[审核记录] 写入失败: {ex.Message}");
            }
        }

        private static string Csv(string value)
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private ModelPreparationResult PrepareModelForDrawing(SwAppClass swApp, string inputPath)
        {
            int errors = 0;
            int warnings = 0;
            string extension = Path.GetExtension(inputPath);
            string inputKind = IsParasolidFile(inputPath) ? "Parasolid" : "SLDPRT";

            CloseSameTitleDocument(swApp, Path.GetFileName(inputPath));

            SolidWorks.Interop.sldworks.ModelDoc2? model;
            if (IsParasolidFile(inputPath))
            {
                LogToWindow($"[Parasolid导入] 开始导入 {inputPath}");
                errors = 0;
                swApp.LoadFile4(inputPath, "", null, ref errors);
                model = swApp.ActiveDoc as SolidWorks.Interop.sldworks.ModelDoc2;
                if (model == null)
                {
                    throw new InvalidOperationException($"Parasolid导入失败，错误码={errors}（0x{errors:X}）");
                }
            }
            else
            {
                model = swApp.OpenDoc6(
                    inputPath,
                    (int)SolidWorks.Interop.swconst.swDocumentTypes_e.swDocPART,
                    (int)SolidWorks.Interop.swconst.swOpenDocOptions_e.swOpenDocOptions_Silent,
                    "",
                    ref errors,
                    ref warnings) as SolidWorks.Interop.sldworks.ModelDoc2;

                if (model == null)
                {
                    throw new InvalidOperationException($"SLDPRT打开失败，错误码={errors}（0x{errors:X}），警告码={warnings}");
                }
            }

            if (model.GetType() != (int)SolidWorks.Interop.swconst.swDocumentTypes_e.swDocPART)
            {
                throw new InvalidOperationException($"导入后的文档不是零件，文档类型={model.GetType()}");
            }

            swApp.ActivateDoc3(model.GetTitle(), true, (int)SolidWorks.Interop.swconst.swRebuildOnActivation_e.swRebuildActiveDoc, ref errors);
            model.ForceRebuild3(true);
            model.EditRebuild3();
            model.GraphicsRedraw2();

            PartGeometryInfo geometry = ReadPartGeometry(model);
            string modelReferencePath = inputPath;
            string intermediatePath = "";

            if (IsParasolidFile(inputPath))
            {
                string prtDir = Path.Combine(OutputFolder, "SLDPRT");
                Directory.CreateDirectory(prtDir);
                intermediatePath = ResolveOutputPath(prtDir, Path.GetFileNameWithoutExtension(inputPath), ".sldprt");

                bool modelSaved = model.Extension.SaveAs(
                    intermediatePath,
                    (int)SolidWorks.Interop.swconst.swSaveAsVersion_e.swSaveAsCurrentVersion,
                    (int)SolidWorks.Interop.swconst.swSaveAsOptions_e.swSaveAsOptions_Silent,
                    null,
                    ref errors,
                    ref warnings);

                if (!modelSaved || !File.Exists(intermediatePath))
                {
                    throw new InvalidOperationException($"Parasolid中间SLDPRT保存失败，错误码={errors}（0x{errors:X}），警告码={warnings}");
                }

                modelReferencePath = intermediatePath;
                LogToWindow($"[Parasolid导入] 成功，文档类型={model.GetType()}，实体={geometry.BodyCount}，曲面={geometry.SurfaceCount}，中间文件={intermediatePath}");
            }
            else
            {
                LogToWindow($"[SLDPRT打开] 成功，错误码={errors}，警告码={warnings}");
            }

            if (string.IsNullOrWhiteSpace(modelReferencePath) || !File.Exists(modelReferencePath))
            {
                throw new InvalidOperationException($"找不到可用于工程图引用的模型文件路径: {modelReferencePath}");
            }

            return new ModelPreparationResult(
                Model: model,
                ModelReferencePath: modelReferencePath,
                InputKind: inputKind,
                BodyCount: geometry.BodyCount,
                SurfaceCount: geometry.SurfaceCount,
                MaxDimensionMm: geometry.MaxDimensionMm,
                IntermediateSldprtPath: intermediatePath);
        }

        private void CloseSameTitleDocument(SwAppClass swApp, string openFileName)
        {
            try
            {
                object? documentsObject = swApp.GetDocuments();
                if (documentsObject is not object[] openDocuments)
                {
                    return;
                }

                foreach (object documentObject in openDocuments)
                {
                    try
                    {
                        if (documentObject is SolidWorks.Interop.sldworks.ModelDoc2 openDoc &&
                            string.Equals(openDoc.GetTitle(), openFileName, StringComparison.OrdinalIgnoreCase))
                        {
                            LogToWindow($"[清理] 发现残留同名文档 \"{openDoc.GetTitle()}\"，关闭后重新打开。");
                            swApp.CloseDoc(openDoc.GetTitle());
                        }
                    }
                    catch (Exception ex)
                    {
                        LogToWindow($"[清理警告] 检查已打开文档失败: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogToWindow($"[清理警告] 获取已打开文档失败: {ex.Message}");
            }
        }

        private PartGeometryInfo ReadPartGeometry(SolidWorks.Interop.sldworks.ModelDoc2 model)
        {
            int bodyCount = 0;
            int surfaceCount = 0;
            double maxDim = 150.0;

            try
            {
                if (model is SolidWorks.Interop.sldworks.PartDoc partDoc)
                {
                    bodyCount = CountBodies(partDoc, (int)SolidWorks.Interop.swconst.swBodyType_e.swSolidBody);
                    surfaceCount = CountBodies(partDoc, (int)SolidWorks.Interop.swconst.swBodyType_e.swSheetBody);

                    if (partDoc.GetPartBox(true) is double[] box && box.Length >= 6)
                    {
                        double dx = Math.Abs(box[3] - box[0]) * 1000;
                        double dy = Math.Abs(box[4] - box[1]) * 1000;
                        double dz = Math.Abs(box[5] - box[2]) * 1000;
                        maxDim = Math.Max(dx, Math.Max(dy, dz));
                    }
                }
            }
            catch (Exception ex)
            {
                LogToWindow($"[模型检查] 读取实体或包围盒失败，采用默认尺寸。原因: {ex.Message}");
            }

            return new PartGeometryInfo(bodyCount, surfaceCount, maxDim);
        }

        private static int CountBodies(SolidWorks.Interop.sldworks.PartDoc partDoc, int bodyType)
        {
            object? bodies = partDoc.GetBodies2(bodyType, true);
            return bodies is object[] bodyArray ? bodyArray.Length : 0;
        }

        private DrawingSheetPlan SelectDrawingSheetPlan(double maxDimensionMm)
        {
            string requestedPaper = (_settings.PaperMode ?? "Auto").Trim().ToUpperInvariant();
            string sheetSize = requestedPaper is "A4" or "A3"
                ? requestedPaper
                : maxDimensionMm <= 260 ? "A4" : "A3";

            (double usableWidthMm, double usableHeightMm) = sheetSize == "A4"
                ? (260.0, 170.0)
                : (380.0, 250.0);

            (int numerator, int denominator)[] ratios =
            {
                (5, 1), (3, 1), (2, 1), (1, 1), (1, 2), (1, 3), (1, 5), (1, 10), (1, 20)
            };

            foreach ((int numerator, int denominator) in ratios)
            {
                double scale = (double)numerator / denominator;
                double estimatedWidth = maxDimensionMm * scale * 2.35;
                double estimatedHeight = maxDimensionMm * scale * 1.85;
                if (estimatedWidth <= usableWidthMm && estimatedHeight <= usableHeightMm)
                {
                    return new DrawingSheetPlan(sheetSize, numerator, denominator);
                }
            }

            return new DrawingSheetPlan(sheetSize, 1, 20);
        }

        private string ResolveDrawingTemplatePath(SwAppClass swApp, string sheetSize)
        {
            if (!string.IsNullOrWhiteSpace(_settings.DrawingTemplatePath) && File.Exists(_settings.DrawingTemplatePath))
            {
                return _settings.DrawingTemplatePath;
            }

            string configuredTemplate = Path.Combine(TemplateFolder, $"{sheetSize}.drwdot");
            if (File.Exists(configuredTemplate))
            {
                return configuredTemplate;
            }

            try
            {
                string defaultTemplate = swApp.GetUserPreferenceStringValue(
                    (int)SolidWorks.Interop.swconst.swUserPreferenceStringValue_e.swDefaultTemplateDrawing);
                if (!string.IsNullOrWhiteSpace(defaultTemplate) && File.Exists(defaultTemplate))
                {
                    LogToWindow($"[模板提示] 指定模板不存在，改用 SolidWorks 默认模板: {defaultTemplate}");
                    return defaultTemplate;
                }
            }
            catch (Exception ex)
            {
                LogToWindow($"[模板提示] 读取 SolidWorks 默认工程图模板失败: {ex.Message}");
            }

            return "";
        }

        private SolidWorks.Interop.sldworks.DrawingDoc? CreateDrawingDocument(SwAppClass swApp, string templatePath, string sheetSize)
        {
            SolidWorks.Interop.swconst.swDwgPaperSizes_e paperSizeEnum = sheetSize switch
            {
                "A4" => SolidWorks.Interop.swconst.swDwgPaperSizes_e.swDwgPaperA4size,
                "A3" => SolidWorks.Interop.swconst.swDwgPaperSizes_e.swDwgPaperA3size,
                _ => SolidWorks.Interop.swconst.swDwgPaperSizes_e.swDwgPaperA4size,
            };

            return swApp.NewDocument(templatePath, (int)paperSizeEnum, 0, 0) as SolidWorks.Interop.sldworks.DrawingDoc;
        }

        private void CreateNamedStandardViews(SwAppClass swApp, SolidWorks.Interop.sldworks.ModelDoc2 swModel)
        {
            int errors = 0;
            swApp.ActivateDoc3(swModel.GetTitle(), true, 1, ref errors);

            CreateNamedStandardView(swModel, "Auto_Front", SolidWorks.Interop.swconst.swStandardViews_e.swFrontView);
            CreateNamedStandardView(swModel, "Auto_Top", SolidWorks.Interop.swconst.swStandardViews_e.swTopView);
            CreateNamedStandardView(swModel, "Auto_Right", SolidWorks.Interop.swconst.swStandardViews_e.swRightView);
            CreateNamedStandardView(swModel, "Auto_Back", SolidWorks.Interop.swconst.swStandardViews_e.swBackView);
            CreateNamedStandardView(swModel, "Auto_Left", SolidWorks.Interop.swconst.swStandardViews_e.swLeftView);
            CreateNamedStandardView(swModel, "Auto_Bottom", SolidWorks.Interop.swconst.swStandardViews_e.swBottomView);
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

        private void InsertStandardDrawingViews(
            SolidWorks.Interop.sldworks.DrawingDoc swDraw,
            string modelReferencePath,
            string sheetSize,
            double modelScale)
        {
            var layout = GetSheetViewLayout(sheetSize);

            InsertDrawingView(swDraw, modelReferencePath, "Auto_Front", "*Front", layout.FrontX, layout.FrontY, modelScale);
            InsertDrawingView(swDraw, modelReferencePath, "Auto_Top", "*Top", layout.TopX, layout.TopY, modelScale);
            InsertDrawingView(swDraw, modelReferencePath, "Auto_Right", "*Right", layout.RightX, layout.RightY, modelScale);
            InsertDrawingView(swDraw, modelReferencePath, "Auto_ISO", "*Isometric", layout.IsoX, layout.IsoY, modelScale * 0.8);
        }

        private void InsertDrawingView(
            SolidWorks.Interop.sldworks.DrawingDoc swDraw,
            string modelReferencePath,
            string viewName,
            string fallbackViewName,
            double x,
            double y,
            double scale)
        {
            SwView? view = (SwView?)swDraw.CreateDrawViewFromModelView3(modelReferencePath, viewName, x, y, 0);
            if (view == null)
            {
                LogToWindow($"[视图插入] {viewName} 插入失败，尝试标准视图名 {fallbackViewName}。");
                view = (SwView?)swDraw.CreateDrawViewFromModelView3(modelReferencePath, fallbackViewName, x, y, 0);
            }

            if (view == null)
            {
                throw new InvalidOperationException($"CreateDrawViewFromModelView3 插入失败: {viewName}/{fallbackViewName}, 模型路径: {modelReferencePath}");
            }

            view.ScaleDecimal = scale;
            view.UseSheetScale = 0;
            view.SetVisible(true, true);

            object? outlineObject = view.GetOutline();
            string outlineText = outlineObject is double[] outline && outline.Length >= 4
                ? $"{outline[0]:F4},{outline[1]:F4},{outline[2]:F4},{outline[3]:F4}"
                : "无";

            LogToWindow($"[视图插入] {view.GetName2()} 引用={view.GetReferencedModelName()} 已加载={view.IsModelLoaded()} 可见={view.GetVisible()} 轮廓={outlineText}");
        }

        private static SheetViewLayout GetSheetViewLayout(string sheetSize)
        {
            (double width, double height) = sheetSize switch
            {
                "A4" => (0.297, 0.210),
                "A3" => (0.420, 0.297),
                "A2" => (0.594, 0.420),
                "A1" => (0.841, 0.594),
                _ => (0.420, 0.297),
            };

            return new SheetViewLayout(
                FrontX: width * 0.25,
                FrontY: height * 0.64,
                TopX: width * 0.25,
                TopY: height * 0.28,
                RightX: width * 0.55,
                RightY: height * 0.64,
                IsoX: width * 0.78,
                IsoY: height * 0.30);
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

        private void LogDrawingViewDiagnostics(SolidWorks.Interop.sldworks.DrawingDoc swDraw)
        {
            int index = 0;
            SwView? view = GetFirstModelDrawingView(swDraw);
            while (view != null)
            {
                index++;
                try
                {
                    object? outlineObject = view.GetOutline();
                    string outlineText = outlineObject is double[] outline && outline.Length >= 4
                        ? $"{outline[0]:F4},{outline[1]:F4},{outline[2]:F4},{outline[3]:F4}"
                        : "无";

                    LogToWindow($"[视图诊断] #{index} 名称={view.GetName2()} 方向={view.GetOrientationName()} 引用={view.GetReferencedModelName()} 已加载={view.IsModelLoaded()} 可见={view.GetVisible()} 轮廓={outlineText}");
                }
                catch (Exception ex)
                {
                    LogToWindow($"[视图诊断] #{index} 读取失败: {ex.Message}");
                }

                view = (SwView?)view.GetNextView();
            }

            if (index == 0)
            {
                LogToWindow("[视图诊断] 工程图内没有模型视图。");
            }
        }

        private string SaveDwg(SwAppClass swApp, SolidWorks.Interop.sldworks.ModelDoc2 drawModel, string fileName)
        {
            string dwgDir = Path.Combine(OutputFolder, "DWG");
            Directory.CreateDirectory(dwgDir);
            string dwgPath = ResolveOutputPath(dwgDir, fileName, ".dwg");

            PrepareDrawingForSave(swApp, drawModel);

            int errors = 0;
            int warnings = 0;
            bool saved = drawModel.Extension.SaveAs(
                dwgPath,
                (int)SolidWorks.Interop.swconst.swSaveAsVersion_e.swSaveAsCurrentVersion,
                (int)SolidWorks.Interop.swconst.swSaveAsOptions_e.swSaveAsOptions_Silent,
                null,
                ref errors,
                ref warnings);

            LogToWindow($"[保存状态] DWG 成果导出 = {saved}，错误码={errors}，警告码={warnings}，大小 = {(File.Exists(dwgPath) ? new FileInfo(dwgPath).Length : -1)} 字节");
            if (!saved)
            {
                throw new InvalidOperationException($"DWG导出失败，错误码={errors}，警告码={warnings}");
            }

            return dwgPath;
        }

        private string SavePdf(SwAppClass swApp, SolidWorks.Interop.sldworks.ModelDoc2 drawModel, string fileName)
        {
            string pdfDir = Path.Combine(OutputFolder, "PDF");
            Directory.CreateDirectory(pdfDir);
            string pdfPath = ResolveOutputPath(pdfDir, fileName, ".pdf");

            PrepareDrawingForSave(swApp, drawModel);

            SolidWorks.Interop.sldworks.ExportPdfData pdfExportData =
                (SolidWorks.Interop.sldworks.ExportPdfData)swApp.GetExportFileData(
                    (int)SolidWorks.Interop.swconst.swExportDataFileType_e.swExportPdfData);

            pdfExportData.SetSheets(
                (int)SolidWorks.Interop.swconst.swExportDataSheetsToExport_e.swExportData_ExportCurrentSheet,
                null);
            pdfExportData.ViewPdfAfterSaving = false;

            int errors = 0;
            int warnings = 0;
            bool saved = drawModel.Extension.SaveAs(
                pdfPath,
                (int)SolidWorks.Interop.swconst.swSaveAsVersion_e.swSaveAsCurrentVersion,
                (int)SolidWorks.Interop.swconst.swSaveAsOptions_e.swSaveAsOptions_Silent,
                pdfExportData,
                ref errors,
                ref warnings);

            LogToWindow($"[保存状态] PDF 成果导出 = {saved}，错误码={errors}，警告码={warnings}，大小 = {(File.Exists(pdfPath) ? new FileInfo(pdfPath).Length : -1)} 字节");
            if (!saved)
            {
                throw new InvalidOperationException($"PDF导出失败，错误码={errors}，警告码={warnings}");
            }

            return pdfPath;
        }

        private string SaveSlddrw(SwAppClass swApp, SolidWorks.Interop.sldworks.ModelDoc2 drawModel, string fileName)
        {
            string drwDir = Path.Combine(OutputFolder, "SLDDRW");
            Directory.CreateDirectory(drwDir);
            string drwPath = ResolveOutputPath(drwDir, fileName, ".slddrw");

            PrepareDrawingForSave(swApp, drawModel);

            int errors = 0;
            int warnings = 0;
            bool saved = drawModel.Extension.SaveAs(
                drwPath,
                (int)SolidWorks.Interop.swconst.swSaveAsVersion_e.swSaveAsCurrentVersion,
                (int)SolidWorks.Interop.swconst.swSaveAsOptions_e.swSaveAsOptions_Silent,
                null,
                ref errors,
                ref warnings);

            LogToWindow($"[保存状态] SLDDRW 成果导出 = {saved}，错误码={errors}，警告码={warnings}，大小 = {(File.Exists(drwPath) ? new FileInfo(drwPath).Length : -1)} 字节");
            if (!saved)
            {
                throw new InvalidOperationException($"SLDDRW保存失败，错误码={errors}，警告码={warnings}");
            }

            return drwPath;
        }

        private static void PrepareDrawingForSave(SwAppClass swApp, SolidWorks.Interop.sldworks.ModelDoc2 drawModel)
        {
            int errors = 0;
            swApp.ActivateDoc3(
                drawModel.GetTitle(),
                true,
                (int)SolidWorks.Interop.swconst.swRebuildOnActivation_e.swRebuildActiveDoc,
                ref errors);

            drawModel.ClearSelection2(true);
            drawModel.ForceRebuild3(true);
            drawModel.EditRebuild3();
            drawModel.GraphicsRedraw2();
            Thread.Sleep(500);
        }

        private bool WaitForDrawingViewsReady(
            SwAppClass swApp,
            SolidWorks.Interop.sldworks.DrawingDoc swDraw,
            SolidWorks.Interop.sldworks.ModelDoc2 drawModel,
            TimeSpan timeout)
        {
            Stopwatch wait = Stopwatch.StartNew();
            while (wait.Elapsed < timeout)
            {
                PrepareDrawingForSave(swApp, drawModel);

                int modelViewCount = CountModelDrawingViews(swDraw);
                if (modelViewCount > 0 && HasVisibleDrawingView(swDraw) && AllDrawingViewsReferenceLoadedModels(swDraw))
                {
                    LogToWindow($"[视图等待] 视图已加载，数量={modelViewCount}，耗时={wait.ElapsedMilliseconds}ms");
                    return true;
                }

                Thread.Sleep(250);
            }

            LogToWindow($"[视图等待] 超时={timeout.TotalSeconds:F0}s，视图仍未完全可用。");
            return false;
        }

        private bool AllDrawingViewsReferenceLoadedModels(SolidWorks.Interop.sldworks.DrawingDoc swDraw)
        {
            SwView? view = GetFirstModelDrawingView(swDraw);
            while (view != null)
            {
                try
                {
                    string referencedModel = view.GetReferencedModelName();
                    if (string.IsNullOrWhiteSpace(referencedModel) || !view.IsModelLoaded())
                    {
                        return false;
                    }
                }
                catch
                {
                    return false;
                }

                view = (SwView?)view.GetNextView();
            }

            return true;
        }

        private void InsertAutomaticModelDimensions(
            SwAppClass swApp,
            SolidWorks.Interop.sldworks.DrawingDoc swDraw,
            SolidWorks.Interop.sldworks.ModelDoc2 drawModel)
        {
            int errors = 0;
            try
            {
                LogToWindow("[尺寸注入] 配置已启用，开始插入模型尺寸。");
                swApp.ActivateDoc3(drawModel.GetTitle(), true, 1, ref errors);
                drawModel.ClearSelection2(true);

                SolidWorks.Interop.sldworks.View? currentView = (SolidWorks.Interop.sldworks.View?)swDraw.GetFirstView();
                if (currentView != null)
                {
                    currentView = (SolidWorks.Interop.sldworks.View?)currentView.GetNextView();
                }

                while (currentView != null)
                {
                    string viewName = currentView.GetName2();
                    if (!viewName.Contains("ISO", StringComparison.OrdinalIgnoreCase) &&
                        !viewName.Contains("Dimetric", StringComparison.OrdinalIgnoreCase))
                    {
                        drawModel.Extension.SelectByID2(viewName, "DRAWINGVIEW", 0, 0, 0, false, 0, null, 0);
                        swDraw.InsertModelAnnotations3(0, 32769, true, true, false, true);
                    }

                    currentView = (SolidWorks.Interop.sldworks.View?)currentView.GetNextView();
                }

                drawModel.ClearSelection2(true);
                PrepareDrawingForSave(swApp, drawModel);
            }
            catch (Exception annoEx)
            {
                LogToWindow($"[警告] 自动尺寸标注注入异常: {annoEx.Message}");
            }
        }

        private bool ValidateOutputFile(string label, string path, long minimumBytes)
        {
            if (!File.Exists(path))
            {
                LogToWindow($"[输出验证] {label} 不存在: {path}");
                return false;
            }

            long length = new FileInfo(path).Length;
            bool ok = length >= minimumBytes;
            LogToWindow($"[输出验证] {label} 文件大小={length} 字节，结果={(ok ? "通过" : "失败")}");
            return ok;
        }

        private bool ValidatePdfFile(string path)
        {
            if (!ValidateOutputFile("PDF", path, 1024))
            {
                return false;
            }

            try
            {
                byte[] header = File.ReadAllBytes(path).Take(5).ToArray();
                bool hasPdfHeader = header.Length == 5 && System.Text.Encoding.ASCII.GetString(header) == "%PDF-";
                if (!hasPdfHeader)
                {
                    LogToWindow("[输出验证] PDF 文件头无效。");
                    return false;
                }

                string sample = File.ReadAllText(path);
                bool hasPage = sample.Contains("/Type /Page", StringComparison.OrdinalIgnoreCase)
                    || sample.Contains("/Pages", StringComparison.OrdinalIgnoreCase);
                LogToWindow($"[输出验证] PDF 页面结构={(hasPage ? "存在" : "未确认")}");
                return hasPage;
            }
            catch (Exception ex)
            {
                LogToWindow($"[输出验证] PDF 基础检查失败: {ex.Message}");
                return false;
            }
        }

        private string ResolveOutputPath(string directory, string baseName, string extension)
        {
            string strategy = (_settings.OutputConflictStrategy ?? "AutoNumber").Trim();
            string firstPath = Path.Combine(directory, baseName + extension);
            if (!File.Exists(firstPath))
            {
                return firstPath;
            }

            if (strategy.Equals("Overwrite", StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(firstPath);
                return firstPath;
            }

            if (strategy.Equals("Skip", StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException($"输出文件已存在，配置为跳过: {firstPath}");
            }

            for (int i = 1; i < 10000; i++)
            {
                string numberedPath = Path.Combine(directory, $"{baseName}_{i:000}{extension}");
                if (!File.Exists(numberedPath))
                {
                    return numberedPath;
                }
            }

            throw new IOException($"无法生成不重名输出路径: {directory}\\{baseName}{extension}");
        }

        private SwView? GetFirstModelDrawingView(SolidWorks.Interop.sldworks.DrawingDoc swDraw)
        {
            SwView? sheetView = (SwView?)swDraw.GetFirstView();
            return sheetView == null ? null : (SwView?)sheetView.GetNextView();
        }

        private readonly record struct SheetViewLayout(
            double FrontX,
            double FrontY,
            double TopX,
            double TopY,
            double RightX,
            double RightY,
            double IsoX,
            double IsoY);

        private readonly record struct PartGeometryInfo(
            int BodyCount,
            int SurfaceCount,
            double MaxDimensionMm);

        private readonly record struct ModelPreparationResult(
            SolidWorks.Interop.sldworks.ModelDoc2 Model,
            string ModelReferencePath,
            string InputKind,
            int BodyCount,
            int SurfaceCount,
            double MaxDimensionMm,
            string IntermediateSldprtPath);

        private readonly record struct DrawingSheetPlan(
            string SheetSize,
            int ScaleNumerator,
            int ScaleDenominator)
        {
            public double ScaleDecimal => (double)ScaleNumerator / ScaleDenominator;

            public string ScaleText => $"{ScaleNumerator}:{ScaleDenominator}";
        }

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
                        return;
                    }
                    catch
                    {
                    }
                }
            }

            LogToWindow("[提示] 未能自动找到特征识别相关枚举设置，请手动在系统选项中关闭。");
        }

        private void LoadSettingsToForm()
        {
            txtInputFolder.Text = InputFolder;
            txtOutputFolder.Text = OutputFolder;
            txtTemplateFolder.Text = TemplateFolder;
        }

        private void SaveSettingsFromForm()
        {
            _settings.InputFolder = NormalizeFolderPath(txtInputFolder.Text, @"D:\AutoDrawingServer\Input");
            _settings.OutputFolder = NormalizeFolderPath(txtOutputFolder.Text, @"D:\AutoDrawingServer\Output");
            _settings.TemplateFolder = NormalizeFolderPath(txtTemplateFolder.Text, @"D:\AutoDrawingServer\Templates");
            _settings.Save();

            LoadSettingsToForm();
        }

        private void EnsureWorkingFolders()
        {
            Directory.CreateDirectory(InputFolder);
            Directory.CreateDirectory(OutputFolder);
            Directory.CreateDirectory(TemplateFolder);
            Directory.CreateDirectory(Path.Combine(OutputFolder, "Logs"));
        }

        private static string NormalizeFolderPath(string folderPath, string fallbackPath)
        {
            string value = string.IsNullOrWhiteSpace(folderPath) ? fallbackPath : folderPath.Trim();
            return Path.GetFullPath(value);
        }

        private void btnBrowseInput_Click(object sender, EventArgs e)
        {
            SelectFolder(txtInputFolder, "请选择监听输入目录");
        }

        private void btnBrowseOutput_Click(object sender, EventArgs e)
        {
            SelectFolder(txtOutputFolder, "请选择2D图导出目录");
        }

        private void btnBrowseTemplate_Click(object sender, EventArgs e)
        {
            SelectFolder(txtTemplateFolder, "请选择工程图模板目录");
        }

        private void btnSaveSettings_Click(object sender, EventArgs e)
        {
            SaveSettingsFromForm();
            EnsureWorkingFolders();
            LogToWindow("[系统提示] 设置已保存。");
        }

        private static void SelectFolder(TextBox targetTextBox, string description)
        {
            using FolderBrowserDialog dialog = new()
            {
                Description = description,
                SelectedPath = Directory.Exists(targetTextBox.Text) ? targetTextBox.Text : @"D:\"
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                targetTextBox.Text = dialog.SelectedPath;
            }
        }

        private static bool IsSupportedTaskFile(string filePath)
        {
            string fileName = Path.GetFileName(filePath);
            if (fileName.StartsWith("~$", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string extension = Path.GetExtension(filePath);
            return extension.Equals(".step", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".stp", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".sldprt", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".x_t", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".x_b", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsParasolidFile(string filePath)
        {
            string extension = Path.GetExtension(filePath);
            return extension.Equals(".x_t", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".x_b", StringComparison.OrdinalIgnoreCase);
        }

        private bool WaitForFileReady(string filePath, CancellationToken token)
        {
            for (int retry = 0; retry < 10; retry++)
            {
                if (token.IsCancellationRequested)
                {
                    return false;
                }

                try
                {
                    using FileStream stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    return stream.Length > 0;
                }
                catch (IOException)
                {
                    Thread.Sleep(500);
                }
                catch (UnauthorizedAccessException)
                {
                    Thread.Sleep(500);
                }
            }

            return false;
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
                    Directory.CreateDirectory(Path.Combine(OutputFolder, "Logs"));
                    string logFile = Path.Combine(OutputFolder, "Logs", $"{DateTime.Now:yyyy-MM-dd}.log");
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
