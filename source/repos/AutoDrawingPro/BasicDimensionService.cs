using System.Diagnostics;

using SwAppClass = SolidWorks.Interop.sldworks.SldWorks;
using SwView = SolidWorks.Interop.sldworks.View;

namespace AutoDrawingPro
{
    internal sealed class BasicDimensionService
    {
        private readonly AppSettings _settings;
        private readonly Action<string> _log;

        public BasicDimensionService(AppSettings settings, Action<string> log)
        {
            _settings = settings;
            _log = log;
        }

        public BasicDimensionResult ApplyBasicDimensions(
            SwAppClass swApp,
            SolidWorks.Interop.sldworks.ModelDoc2 partModel,
            SolidWorks.Interop.sldworks.DrawingDoc drawing,
            SolidWorks.Interop.sldworks.ModelDoc2 drawingModel,
            string inputKind)
        {
            IReadOnlyDictionary<PartCategory, PartDimensionRule> rules = PartClassificationRules.CreateDefaultRules(_settings);
            PartDimensionRule rule = rules[PartCategory.Unclassified];
            List<DimensionCandidate> candidates = BuildDimensionCandidates(partModel, drawing, inputKind, rule);
            foreach (DimensionCandidate candidate in candidates.Where(c => c.IsSelected))
            {
                candidate.IsSelected = false;
            }

            return ApplyConfirmedDimensions(swApp, drawing, drawingModel, candidates);
        }

        public List<DimensionCandidate> BuildDimensionCandidates(
            SolidWorks.Interop.sldworks.ModelDoc2 partModel,
            SolidWorks.Interop.sldworks.DrawingDoc drawing,
            string inputKind,
            PartDimensionRule rule)
        {
            BasicDimensionResult result = new();

            if (!_settings.EnableBasicDimensions)
            {
                _log("[基础尺寸] EnableBasicDimensions=false，跳过阶段二尺寸。");
                return new List<DimensionCandidate>();
            }

            _log($"[基础尺寸] 开始建立候选尺寸，分类规则={rule.Name}，尺寸来源={(inputKind.Equals("Parasolid", StringComparison.OrdinalIgnoreCase) ? "几何推断" : "模型特征/几何推断")}");

            List<DimensionCandidate> candidates = BuildCandidates(partModel, drawing, inputKind, result);
            MarkDuplicates(candidates);
            RejectInvalidCandidates(candidates, result);
            ApplyClassificationRule(candidates, rule);
            SelectReadableDimensionSet(candidates, rule);

            List<DimensionCandidate> ordered = candidates
                .Where(c => c.CanSelect && string.IsNullOrWhiteSpace(c.RejectionReason))
                .OrderByDescending(c => c.Priority)
                .ToList();
            EnforceDimensionLimits(ordered, result, rule);
            ApplyReviewDefaults(candidates, rule);

            _log($"[基础尺寸候选] 分类={rule.Name} 候选={candidates.Count} 绿色={candidates.Count(c => c.ReviewStatus == DimensionReviewStatus.Green)} 黄色={candidates.Count(c => c.ReviewStatus == DimensionReviewStatus.Yellow)} 红色={candidates.Count(c => c.ReviewStatus == DimensionReviewStatus.Red)}");
            foreach (DimensionCandidate candidate in candidates)
            {
                LogCandidate(candidate);
            }

            return candidates;
        }

        public BasicDimensionResult ApplyConfirmedDimensions(
            SwAppClass swApp,
            SolidWorks.Interop.sldworks.DrawingDoc drawing,
            SolidWorks.Interop.sldworks.ModelDoc2 drawingModel,
            IEnumerable<DimensionCandidate> confirmedCandidates)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            BasicDimensionResult result = new();
            List<DimensionCandidate> candidates = confirmedCandidates.ToList();
            result.CandidateCount = candidates.Count;

            foreach (DimensionCandidate candidate in candidates.Where(c => c.IsSelected && c.CanSelect))
            {
                TryCreateDimension(swApp, drawing, drawingModel, candidate, result);
                LogCandidate(candidate);
            }

            foreach (DimensionCandidate candidate in candidates.Where(c => !c.Created && (!c.IsSelected || !c.CanSelect)))
            {
                LogCandidate(candidate);
            }

            PrepareAfterDimensions(swApp, drawingModel);
            result.CreatedCount = candidates.Count(c => c.Created);
            if (result.CreatedCount == 0 && result.CandidateCount > 0)
            {
                result.Warnings.Add("本次没有成功创建尺寸。请检查日志中的几何引用选择失败、视图实体不可选或模板比例问题。");
            }

            if (result.AssociationRejectedCount > 0 || result.SpaceRejectedCount > 0)
            {
                result.Warnings.Add("存在被拒绝的确认尺寸，工程图已生成基础尺寸，请工程师检查。");
            }

            _log($"[基础尺寸汇总] 候选={result.CandidateCount} 确认={candidates.Count(c => c.IsSelected)} 创建={result.CreatedCount} 空间不足={result.SpaceRejectedCount} 无法关联={result.AssociationRejectedCount} 耗时={stopwatch.Elapsed}");
            foreach (var item in result.DimensionsPerView)
            {
                _log($"[基础尺寸汇总] 视图={item.Key} 尺寸数={item.Value}");
                if (item.Value < _settings.MinDimensionsPerView)
                {
                    _log($"[人工检查提示] 视图={item.Key} 当前尺寸数少于{_settings.MinDimensionsPerView}个，通常表示候选几何不足、关联选择失败或排版避让后被拒绝。");
                }
            }

            foreach (string warning in result.Warnings)
            {
                _log($"[人工检查提示] {warning}");
            }

            return result;
        }

        private List<DimensionCandidate> BuildCandidates(
            SolidWorks.Interop.sldworks.ModelDoc2 partModel,
            SolidWorks.Interop.sldworks.DrawingDoc drawing,
            string inputKind,
            BasicDimensionResult result)
        {
            List<DimensionCandidate> candidates = new();
            PartBox box = ReadPartBox(partModel);

            if (_settings.EnableOverallDimensions)
            {
                AddOverallCandidates(candidates, drawing, box, inputKind);
            }

            AddFeatureCandidatesFromVisibleGeometry(candidates, drawing, inputKind, result);
            return candidates;
        }

        private void AddOverallCandidates(
            List<DimensionCandidate> candidates,
            SolidWorks.Interop.sldworks.DrawingDoc drawing,
            PartBox box,
            string inputKind)
        {
            if (!box.IsValid)
            {
                candidates.Add(RejectedCandidate(BasicDimensionType.OverallLength, "模型包围盒无效"));
                return;
            }

            string front = FindViewName(drawing, "Auto_Front", "Front");
            string top = FindViewName(drawing, "Auto_Top", "Top");

            AddOverallCandidate(candidates, BasicDimensionType.OverallLength, box.LengthMm, front, DimensionDirection.Horizontal, 100, inputKind);
            AddOverallCandidate(candidates, BasicDimensionType.OverallHeight, box.HeightMm, front, DimensionDirection.Vertical, 100, inputKind);
            AddOverallCandidate(candidates, BasicDimensionType.OverallWidth, box.WidthMm, string.IsNullOrWhiteSpace(top) ? front : top, DimensionDirection.Vertical, 100, inputKind);
        }

        private void AddOverallCandidate(
            List<DimensionCandidate> candidates,
            BasicDimensionType type,
            double valueMm,
            string viewName,
            DimensionDirection direction,
            int priority,
            string inputKind)
        {
            DimensionCandidate candidate = new()
            {
                DimensionType = type,
                FeatureType = DimensionFeatureType.OverallBoundingBox,
                ValueMm = valueMm,
                ViewName = viewName,
                Direction = direction,
                Priority = priority,
                SourceKind = inputKind.Equals("Parasolid", StringComparison.OrdinalIgnoreCase) ? "几何推断" : "模型包围盒",
                SourceFeatureId = "OverallBoundingBox",
                Datum = "模型包围盒最小边界"
            };

            if (valueMm < _settings.MinimumFeatureSize)
            {
                candidate.RejectionReason = "尺寸小于最小特征阈值";
            }

            if (string.IsNullOrWhiteSpace(viewName))
            {
                candidate.RejectionReason = "找不到可表达该外形尺寸的视图";
            }

            candidates.Add(candidate);
        }

        private void AddFeatureCandidatesFromVisibleGeometry(
            List<DimensionCandidate> candidates,
            SolidWorks.Interop.sldworks.DrawingDoc drawing,
            string inputKind,
            BasicDimensionResult result)
        {
            SwView? view = GetFirstModelDrawingView(drawing);
            while (view != null)
            {
                if (!IsOrthographicDimensionView(view))
                {
                    _log($"[基础尺寸] 跳过非正投影视图: 名称={SafeViewName(view)} 方向={SafeOrientationName(view)}");
                    view = (SwView?)view.GetNextView();
                    continue;
                }

                string viewName = SafeViewName(view);
                List<VisibleCircle> circles = GetVisibleCircles(view);
                foreach (VisibleCircle circle in circles)
                {
                    if (circle.DiameterMm < _settings.MinimumFeatureSize)
                    {
                        continue;
                    }

                    DimensionCandidate holeCandidate = new()
                    {
                        DimensionType = BasicDimensionType.HoleDiameter,
                        FeatureType = DimensionFeatureType.Hole,
                        ValueMm = circle.DiameterMm,
                        ViewName = viewName,
                        Direction = DimensionDirection.Diameter,
                        Priority = 90,
                        SourceKind = inputKind.Equals("Parasolid", StringComparison.OrdinalIgnoreCase) ? "几何推断" : "可见圆边",
                        SourceEdgeId = circle.Id,
                        Reference1 = circle.Edge,
                        Datum = "圆柱/圆边几何"
                    };

                    if (!_settings.EnableHoleDiameterDimensions)
                    {
                        holeCandidate.RejectionReason = "孔径尺寸配置关闭";
                    }

                    if (!circle.IsArc)
                    {
                        candidates.Add(holeCandidate);
                    }

                    DimensionCandidate cylinderCandidate = new()
                    {
                        DimensionType = BasicDimensionType.CylinderDiameter,
                        FeatureType = DimensionFeatureType.Cylinder,
                        ValueMm = circle.DiameterMm,
                        ViewName = viewName,
                        Direction = DimensionDirection.Diameter,
                        Priority = 80,
                        SourceKind = inputKind.Equals("Parasolid", StringComparison.OrdinalIgnoreCase) ? "几何推断" : "可见圆边",
                        SourceEdgeId = circle.Id,
                        Reference1 = circle.Edge,
                        Datum = "圆柱/圆边几何",
                        RejectionReason = _settings.EnableCylinderDiameterDimensions
                            ? "同一圆边已由孔径/直径候选处理，避免重复直径尺寸"
                            : "圆柱直径尺寸配置关闭"
                    };
                    candidates.Add(cylinderCandidate);

                    if (_settings.EnableRadiusDimensions && circle.IsArc)
                    {
                        candidates.Add(new DimensionCandidate
                        {
                            DimensionType = BasicDimensionType.Radius,
                            FeatureType = DimensionFeatureType.Radius,
                            ValueMm = circle.DiameterMm / 2.0,
                            ViewName = viewName,
                            Direction = DimensionDirection.Radius,
                            Priority = 70,
                            SourceKind = inputKind.Equals("Parasolid", StringComparison.OrdinalIgnoreCase) ? "几何推断" : "可见圆弧/圆边",
                            SourceEdgeId = circle.Id,
                            Reference1 = circle.Edge,
                            Datum = "圆弧几何"
                        });
                    }

                    if (_settings.EnableHoleLocationDimensions)
                    {
                        candidates.Add(new DimensionCandidate
                        {
                            DimensionType = BasicDimensionType.HoleLocation,
                            FeatureType = DimensionFeatureType.Hole,
                            ValueMm = 0,
                            ViewName = viewName,
                            Direction = DimensionDirection.Horizontal,
                            Priority = 85,
                            SourceKind = inputKind.Equals("Parasolid", StringComparison.OrdinalIgnoreCase) ? "几何推断" : "可见圆边",
                            SourceEdgeId = circle.Id,
                            Reference1 = circle.Edge,
                            Datum = "模型包围盒最小边界",
                            IsAmbiguous = true,
                            RejectionReason = "孔中心和基准关联未能可靠建立，拒绝孔位置尺寸"
                        });
                    }
                }

                if (_settings.EnableSlotWidthDimensions)
                {
                    DetectSlotWidthCandidates(candidates, view, circles, inputKind);
                }

                if (_settings.EnableHoleLocationDimensions)
                {
                    DetectCenterDistanceCandidates(candidates, view, circles, inputKind);
                }

                view = (SwView?)view.GetNextView();
            }
        }

        private void DetectCenterDistanceCandidates(
            List<DimensionCandidate> candidates,
            SwView view,
            List<VisibleCircle> circles,
            string inputKind)
        {
            List<VisibleCircle> fullCircles = circles
                .Where(c => !c.IsArc && c.DiameterMm >= _settings.MinimumFeatureSize)
                .ToList();

            if (fullCircles.Count < 2)
            {
                return;
            }

            (VisibleCircle First, VisibleCircle Second, double DistanceMm)? bestPair = null;
            for (int i = 0; i < fullCircles.Count; i++)
            {
                for (int j = i + 1; j < fullCircles.Count; j++)
                {
                    VisibleCircle first = fullCircles[i];
                    VisibleCircle second = fullCircles[j];
                    double dx = (second.CenterX - first.CenterX) * 1000.0;
                    double dy = (second.CenterY - first.CenterY) * 1000.0;
                    double dz = (second.CenterZ - first.CenterZ) * 1000.0;
                    double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (distance < _settings.MinimumFeatureSize)
                    {
                        continue;
                    }

                    if (bestPair == null || distance > bestPair.Value.DistanceMm)
                    {
                        bestPair = (first, second, distance);
                    }
                }
            }

            if (bestPair == null)
            {
                return;
            }

            VisibleCircle a = bestPair.Value.First;
            VisibleCircle b = bestPair.Value.Second;
            double xDistance = Math.Abs((b.CenterX - a.CenterX) * 1000.0);
            double zDistance = Math.Abs((b.CenterZ - a.CenterZ) * 1000.0);

            candidates.Add(new DimensionCandidate
            {
                DimensionType = BasicDimensionType.CenterDistance,
                FeatureType = DimensionFeatureType.Hole,
                ValueMm = bestPair.Value.DistanceMm,
                ViewName = SafeViewName(view),
                Direction = xDistance >= zDistance ? DimensionDirection.Horizontal : DimensionDirection.Vertical,
                Priority = 88,
                SourceKind = inputKind.Equals("Parasolid", StringComparison.OrdinalIgnoreCase) ? "几何推断" : "可见圆中心",
                SourceFeatureId = $"{a.Id}-{b.Id}",
                SourceEdgeId = $"{a.Id}-{b.Id}",
                Reference1 = a.Edge,
                Reference2 = b.Edge,
                Datum = "两个圆中心"
            });
        }

        private void DetectSlotWidthCandidates(
            List<DimensionCandidate> candidates,
            SwView view,
            List<VisibleCircle> circles,
            string inputKind)
        {
            if (circles.Count < 2)
            {
                return;
            }

            foreach (var group in circles.GroupBy(c => Math.Round(c.DiameterMm, 2)))
            {
                if (group.Count() < 2)
                {
                    continue;
                }

                candidates.Add(new DimensionCandidate
                {
                    DimensionType = BasicDimensionType.SlotWidth,
                    FeatureType = DimensionFeatureType.Slot,
                    ValueMm = group.Key,
                    ViewName = SafeViewName(view),
                    Direction = DimensionDirection.Vertical,
                    Priority = 75,
                    SourceKind = inputKind.Equals("Parasolid", StringComparison.OrdinalIgnoreCase) ? "几何推断" : "可见圆边",
                    SourceFeatureId = "PotentialSlot",
                    Datum = "槽侧面/端部圆弧",
                    IsAmbiguous = true,
                    RejectionReason = "仅发现相同圆弧，未可靠确认两条平行槽侧面，拒绝槽宽尺寸"
                });
            }
        }

        private void MarkDuplicates(List<DimensionCandidate> candidates)
        {
            foreach (DimensionCandidate candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(candidate.RejectionReason))
                {
                    continue;
                }

                bool duplicate = candidates.Any(other =>
                    !ReferenceEquals(candidate, other) &&
                    string.IsNullOrWhiteSpace(other.RejectionReason) &&
                    other.DimensionType == candidate.DimensionType &&
                    SameMeaning(candidate, other));

                if (duplicate)
                {
                    candidate.IsDuplicate = true;
                    candidate.RejectionReason = "重复尺寸";
                    candidate.Priority -= 1000;
                }
            }
        }

        private bool SameMeaning(DimensionCandidate a, DimensionCandidate b)
        {
            if (!string.Equals(a.SourceFeatureId, b.SourceFeatureId, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(a.SourceEdgeId, b.SourceEdgeId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return Math.Abs(a.ValueMm - b.ValueMm) <= _settings.DuplicateTolerance
                && a.Direction == b.Direction;
        }

        private void RejectInvalidCandidates(List<DimensionCandidate> candidates, BasicDimensionResult result)
        {
            foreach (DimensionCandidate candidate in candidates)
            {
                if (candidate.IsDuplicate)
                {
                    result.DuplicateCount++;
                }

                if (candidate.IsAmbiguous)
                {
                    result.AmbiguousCount++;
                    if (string.IsNullOrWhiteSpace(candidate.RejectionReason))
                    {
                        candidate.RejectionReason = "特征识别存在歧义";
                    }
                }

                if (candidate.ValueMm > 0 && candidate.ValueMm < _settings.MinimumFeatureSize)
                {
                    candidate.RejectionReason = "尺寸小于最小特征阈值";
                    candidate.Priority -= 100;
                }

                if (!string.IsNullOrWhiteSpace(candidate.RejectionReason))
                {
                    candidate.Priority -= 1000;
                }
            }
        }

        private void ApplyClassificationRule(List<DimensionCandidate> candidates, PartDimensionRule rule)
        {
            foreach (DimensionCandidate candidate in candidates)
            {
                candidate.RuleName = rule.Name;

                if (!rule.AllowedDimensionTypes.Contains(candidate.DimensionType))
                {
                    candidate.ReviewStatus = DimensionReviewStatus.Red;
                    candidate.IsSelected = false;
                    candidate.ReviewReason = $"当前分类规则不允许该尺寸类型: {candidate.DimensionType}";
                    candidate.RejectionReason = string.IsNullOrWhiteSpace(candidate.RejectionReason)
                        ? candidate.ReviewReason
                        : candidate.RejectionReason;
                    continue;
                }

                if (candidate.ValueMm > 0 && candidate.ValueMm < rule.MinimumFeatureSize)
                {
                    candidate.ReviewStatus = DimensionReviewStatus.Red;
                    candidate.IsSelected = false;
                    candidate.ReviewReason = $"小于当前分类最小特征尺寸 {rule.MinimumFeatureSize:F2}mm";
                    candidate.RejectionReason = candidate.ReviewReason;
                    continue;
                }

                if (rule.Priorities.TryGetValue(candidate.DimensionType, out int priority))
                {
                    candidate.Priority = priority;
                }
            }
        }

        private void ApplyReviewDefaults(List<DimensionCandidate> candidates, PartDimensionRule rule)
        {
            foreach (DimensionCandidate candidate in candidates)
            {
                if (candidate.FeatureType != DimensionFeatureType.OverallBoundingBox && candidate.Reference1 == null)
                {
                    candidate.ReviewStatus = DimensionReviewStatus.Red;
                    candidate.IsSelected = false;
                    candidate.CanSelect = false;
                    candidate.ReviewReason = "无法建立几何关联，禁止选中";
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(candidate.RejectionReason) || candidate.IsDuplicate)
                {
                    candidate.ReviewStatus = DimensionReviewStatus.Red;
                    candidate.IsSelected = false;
                    candidate.CanSelect = !string.IsNullOrWhiteSpace(candidate.RejectionReason)
                        ? !candidate.RejectionReason.Contains("缺少可关联", StringComparison.OrdinalIgnoreCase)
                        : candidate.CanSelect;
                    candidate.ReviewReason = string.IsNullOrWhiteSpace(candidate.ReviewReason)
                        ? candidate.RejectionReason
                        : candidate.ReviewReason;
                    continue;
                }

                if (candidate.IsAmbiguous || !rule.AllowAutoKeep)
                {
                    candidate.ReviewStatus = DimensionReviewStatus.Yellow;
                    candidate.IsSelected = false;
                    candidate.ReviewReason = candidate.IsAmbiguous
                        ? "存在歧义，需要工程师确认"
                        : "当前分类要求人工确认";
                    continue;
                }

                candidate.ReviewStatus = DimensionReviewStatus.Green;
                candidate.IsSelected = true;
                candidate.ReviewReason = "系统判断可靠，建议保留";
            }
        }

        private void SelectReadableDimensionSet(List<DimensionCandidate> candidates, PartDimensionRule rule)
        {
            foreach (var viewGroup in candidates
                .Where(c => string.IsNullOrWhiteSpace(c.RejectionReason))
                .GroupBy(c => c.ViewName, StringComparer.OrdinalIgnoreCase))
            {
                RejectOverflow(
                    viewGroup.Where(c => c.DimensionType is BasicDimensionType.HoleDiameter or BasicDimensionType.CylinderDiameter)
                        .OrderByDescending(c => c.ValueMm),
                    Math.Min(_settings.MaxDiameterDimensionsPerView, rule.MaxDimensionsPerView),
                    "直径尺寸超过当前视图可读上限");

                RejectOverflow(
                    viewGroup.Where(c => c.DimensionType == BasicDimensionType.Radius)
                        .OrderBy(c => c.ValueMm),
                    Math.Min(_settings.MaxRadiusDimensionsPerView, rule.MaxDimensionsPerView),
                    "R角尺寸超过当前视图可读上限");

                RejectOverflow(
                    viewGroup.Where(c => c.DimensionType == BasicDimensionType.CenterDistance)
                        .OrderByDescending(c => c.ValueMm),
                    Math.Min(_settings.MaxCenterDistanceDimensionsPerView, rule.MaxDimensionsPerView),
                    "中心距尺寸超过当前视图可读上限");
            }
        }

        private static void RejectOverflow(IEnumerable<DimensionCandidate> orderedCandidates, int keepCount, string reason)
        {
            int index = 0;
            foreach (DimensionCandidate candidate in orderedCandidates)
            {
                index++;
                if (index > keepCount)
                {
                    candidate.RejectionReason = reason;
                    candidate.Priority -= 200;
                }
            }
        }

        private void EnforceDimensionLimits(List<DimensionCandidate> selected, BasicDimensionResult result, PartDimensionRule rule)
        {
            int drawingCount = 0;
            Dictionary<string, int> perView = new(StringComparer.OrdinalIgnoreCase);

            foreach (DimensionCandidate candidate in selected)
            {
                if (!string.IsNullOrWhiteSpace(candidate.RejectionReason))
                {
                    continue;
                }

                perView.TryGetValue(candidate.ViewName, out int viewCount);
                if (viewCount >= rule.MaxDimensionsPerView)
                {
                    candidate.RejectionReason = "超过每视图尺寸上限";
                    result.SpaceRejectedCount++;
                    continue;
                }

                if (drawingCount >= rule.MaxDimensionsPerDrawing)
                {
                    candidate.RejectionReason = "超过整张工程图尺寸上限";
                    result.SpaceRejectedCount++;
                    continue;
                }

                perView[candidate.ViewName] = viewCount + 1;
                candidate.PlacementOrder = viewCount;
                drawingCount++;
            }
        }

        private void TryCreateDimension(
            SwAppClass swApp,
            SolidWorks.Interop.sldworks.DrawingDoc drawing,
            SolidWorks.Interop.sldworks.ModelDoc2 drawingModel,
            DimensionCandidate candidate,
            BasicDimensionResult result)
        {
            SwView? view = FindView(drawing, candidate.ViewName);
            if (view == null)
            {
                candidate.RejectionReason = "找不到目标工程视图";
                result.AssociationRejectedCount++;
                return;
            }

            if (!IsOrthographicDimensionView(view))
            {
                candidate.RejectionReason = $"目标视图不是前/后/左/右/上/下正投影视图，禁止标注: {SafeOrientationName(view)}";
                result.AssociationRejectedCount++;
                return;
            }

            double[]? outline = GetViewOutline(view);
            if (outline == null)
            {
                candidate.RejectionReason = "无法读取视图轮廓，拒绝排版";
                result.SpaceRejectedCount++;
                return;
            }

            if (candidate.FeatureType == DimensionFeatureType.OverallBoundingBox)
            {
                TryCreateOverallDimension(swApp, drawing, view, drawingModel, candidate, outline, result);
                return;
            }

            if (candidate.Reference1 == null)
            {
                candidate.RejectionReason = "缺少可关联几何引用";
                result.AssociationRejectedCount++;
                return;
            }

            try
            {
                drawingModel.ClearSelection2(true);
                bool selected = SelectEntityInView(view, candidate.Reference1, false);
                if (selected && candidate.Reference2 != null)
                {
                    selected = SelectEntityInView(view, candidate.Reference2, true);
                }

                if (!selected)
                {
                    candidate.RejectionReason = "几何引用选择失败";
                    result.AssociationRejectedCount++;
                    return;
                }

                (double x, double y) = GetDimensionPlacement(candidate, outline, drawing);
                object? dimension = candidate.Direction switch
                {
                    DimensionDirection.Horizontal => drawingModel.AddHorizontalDimension2(x, y, 0),
                    DimensionDirection.Vertical => drawingModel.AddVerticalDimension2(x, y, 0),
                    DimensionDirection.Diameter => drawingModel.AddDiameterDimension2(x, y, 0),
                    DimensionDirection.Radius => drawingModel.AddRadialDimension2(x, y, 0),
                    _ => null
                };

                drawingModel.ClearSelection2(true);
                if (dimension == null)
                {
                    candidate.RejectionReason = "SolidWorks未返回尺寸对象";
                    result.AssociationRejectedCount++;
                    return;
                }

                candidate.Created = true;
                result.DimensionsPerView.TryGetValue(candidate.ViewName, out int count);
                result.DimensionsPerView[candidate.ViewName] = count + 1;
            }
            catch (Exception ex)
            {
                candidate.RejectionReason = $"尺寸创建API失败: {ex.Message}";
                result.AssociationRejectedCount++;
            }
        }

        private void TryCreateOverallDimension(
            SwAppClass swApp,
            SolidWorks.Interop.sldworks.DrawingDoc drawing,
            SwView view,
            SolidWorks.Interop.sldworks.ModelDoc2 drawingModel,
            DimensionCandidate candidate,
            double[] outline,
            BasicDimensionResult result)
        {
            List<VisibleLineEdge> lineEdges = GetVisibleLineEdges(view);
            if (lineEdges.Count < 2)
            {
                candidate.RejectionReason = "目标视图没有足够可见直线边建立外形尺寸关联";
                result.AssociationRejectedCount++;
                return;
            }

            VisibleLineEdge first;
            VisibleLineEdge second;
            if (candidate.Direction == DimensionDirection.Horizontal)
            {
                first = lineEdges.OrderBy(e => e.CenterX).First();
                second = lineEdges.OrderByDescending(e => e.CenterX).First();
            }
            else
            {
                first = lineEdges.OrderBy(e => e.CenterZ).First();
                second = lineEdges.OrderByDescending(e => e.CenterZ).First();
            }

            if (ReferenceEquals(first.Edge, second.Edge))
            {
                candidate.RejectionReason = "外形尺寸极值边相同，拒绝创建";
                result.AssociationRejectedCount++;
                return;
            }

            try
            {
                drawingModel.ClearSelection2(true);
                bool selected1 = SelectEntityInView(view, first.Edge, false);
                bool selected2 = SelectEntityInView(view, second.Edge, true);
                if (!selected1 || !selected2)
                {
                    candidate.RejectionReason = "外形尺寸极值边选择失败";
                    result.AssociationRejectedCount++;
                    return;
                }

                (double x, double y) = GetDimensionPlacement(candidate, outline, drawing);
                object? dimension = candidate.Direction == DimensionDirection.Horizontal
                    ? drawingModel.AddHorizontalDimension2(x, y, 0)
                    : drawingModel.AddVerticalDimension2(x, y, 0);

                drawingModel.ClearSelection2(true);
                if (dimension == null)
                {
                    candidate.RejectionReason = "SolidWorks未返回外形尺寸对象";
                    result.AssociationRejectedCount++;
                    return;
                }

                candidate.Created = true;
                result.DimensionsPerView.TryGetValue(candidate.ViewName, out int count);
                result.DimensionsPerView[candidate.ViewName] = count + 1;
            }
            catch (Exception ex)
            {
                candidate.RejectionReason = $"外形尺寸创建API失败: {ex.Message}";
                result.AssociationRejectedCount++;
            }
        }

        private (double X, double Y) GetDimensionPlacement(
            DimensionCandidate candidate,
            double[] outline,
            SolidWorks.Interop.sldworks.DrawingDoc? drawing)
        {
            double offset = _settings.DimensionOffset;
            double spacing = _settings.DimensionSpacing;
            int baseLayer = candidate.DimensionType switch
            {
                BasicDimensionType.OverallLength or BasicDimensionType.OverallWidth or BasicDimensionType.OverallHeight => 2,
                BasicDimensionType.HoleDiameter or BasicDimensionType.HoleLocation or BasicDimensionType.CenterDistance => 0,
                _ => 1
            };
            int layer = baseLayer + Math.Max(0, candidate.PlacementOrder);

            double xMid = (outline[0] + outline[2]) / 2.0;
            double yMid = (outline[1] + outline[3]) / 2.0;
            double distance = offset + spacing * layer;
            double stagger = spacing * Math.Max(0, candidate.PlacementOrder);
            List<(double X, double Y)> preferred = candidate.Direction switch
            {
                DimensionDirection.Horizontal => new()
                {
                    (xMid, outline[1] - distance),
                    (xMid, outline[3] + distance),
                    (outline[2] + distance, yMid - stagger),
                    (outline[0] - distance, yMid + stagger)
                },
                DimensionDirection.Vertical => new()
                {
                    (outline[0] - distance, yMid),
                    (outline[2] + distance, yMid),
                    (xMid + stagger, outline[3] + distance),
                    (xMid - stagger, outline[1] - distance)
                },
                DimensionDirection.Diameter => new()
                {
                    (outline[2] + distance, outline[3] - stagger),
                    (outline[2] + distance, yMid - stagger),
                    (outline[0] - distance, outline[3] - stagger),
                    (xMid + stagger, outline[3] + distance),
                    (xMid - stagger, outline[1] - distance)
                },
                DimensionDirection.Radius => new()
                {
                    (outline[2] + distance, yMid - stagger),
                    (outline[2] + distance, outline[1] + stagger),
                    (outline[0] - distance, yMid + stagger),
                    (xMid + stagger, outline[3] + distance),
                    (xMid - stagger, outline[1] - distance)
                },
                _ => new()
                {
                    (outline[2] + distance, outline[3] - stagger)
                }
            };

            if (drawing == null)
            {
                return preferred[0];
            }

            List<double[]> protectedOutlines = GetProtectedOtherViewOutlines(drawing, candidate.ViewName);
            foreach ((double x, double y) in preferred)
            {
                if (!IsPointInsideAny(x, y, protectedOutlines))
                {
                    return (x, y);
                }
            }

            return preferred[0];
        }

        private static bool SelectEntityInView(SwView view, object? entity, bool append)
        {
            if (entity == null)
            {
                return false;
            }

            try
            {
                dynamic dynamicView = view;
                bool selectedInView = dynamicView.SelectEntity(entity, append);
                if (selectedInView)
                {
                    return true;
                }
            }
            catch
            {
            }

            try
            {
                dynamic dynamicEntity = entity;
                return dynamicEntity.Select4(append, null);
            }
            catch
            {
                return false;
            }
        }

        private List<double[]> GetProtectedOtherViewOutlines(
            SolidWorks.Interop.sldworks.DrawingDoc drawing,
            string currentViewName)
        {
            List<double[]> outlines = new();
            double protectMargin = Math.Max(_settings.DimensionOffset, _settings.DimensionSpacing);

            SwView? view = GetFirstModelDrawingView(drawing);
            while (view != null)
            {
                string viewName = SafeViewName(view);
                if (!string.Equals(viewName, currentViewName, StringComparison.OrdinalIgnoreCase) &&
                    IsOrthographicDimensionView(view))
                {
                    double[]? outline = GetViewOutline(view);
                    if (outline != null && outline.Length >= 4)
                    {
                        outlines.Add(new[]
                        {
                            outline[0] - protectMargin,
                            outline[1] - protectMargin,
                            outline[2] + protectMargin,
                            outline[3] + protectMargin
                        });
                    }
                }

                view = (SwView?)view.GetNextView();
            }

            return outlines;
        }

        private static bool IsPointInsideAny(double x, double y, IEnumerable<double[]> outlines)
        {
            foreach (double[] outline in outlines)
            {
                if (outline.Length >= 4 &&
                    x >= outline[0] && x <= outline[2] &&
                    y >= outline[1] && y <= outline[3])
                {
                    return true;
                }
            }

            return false;
        }

        private void LogCandidate(DimensionCandidate candidate)
        {
            string value = candidate.ValueMm > 0
                ? Math.Round(candidate.ValueMm, Math.Clamp(_settings.LinearPrecision, 0, 3)).ToString()
                : "未计算";
            string result = candidate.Created ? "已创建" : "拒绝";
            string reason = candidate.Created ? "" : candidate.RejectionReason;
            _log($"[尺寸候选] 视图={candidate.ViewName} 类型={candidate.DimensionType} 数值={value}{candidate.Unit} 来源={candidate.SourceKind} 基准={candidate.Datum} 状态={candidate.ReviewStatus} 选中={candidate.IsSelected} 规则={candidate.RuleName} 优先级={candidate.Priority} 结果={result} 原因={candidate.ReviewReason} 拒绝原因={reason}");
        }

        private static void PrepareAfterDimensions(SwAppClass swApp, SolidWorks.Interop.sldworks.ModelDoc2 drawingModel)
        {
            int errors = 0;
            swApp.ActivateDoc3(
                drawingModel.GetTitle(),
                true,
                (int)SolidWorks.Interop.swconst.swRebuildOnActivation_e.swRebuildActiveDoc,
                ref errors);
            drawingModel.ClearSelection2(true);
            drawingModel.ForceRebuild3(true);
            drawingModel.EditRebuild3();
            drawingModel.GraphicsRedraw2();
        }

        private List<VisibleCircle> GetVisibleCircles(SwView view)
        {
            List<VisibleCircle> circles = new();
            try
            {
                object? visibleEdges = view.GetVisibleEntities2(null, (int)SolidWorks.Interop.swconst.swViewEntityType_e.swViewEntityType_Edge);
                if (visibleEdges is not object[] edges)
                {
                    return circles;
                }

                int index = 0;
                foreach (object edge in edges)
                {
                    index++;
                    try
                    {
                        dynamic dynamicEdge = edge;
                        object curveObject = dynamicEdge.GetCurve();
                        dynamic curve = curveObject;
                        bool isCircle = curve.IsCircle();
                        if (!isCircle)
                        {
                            continue;
                        }

                        object circleParamsObject = curve.CircleParams;
                        if (circleParamsObject is not double[] circleParams || circleParams.Length < 7)
                        {
                            continue;
                        }

                        double radiusMeters = Math.Abs(circleParams[6]);
                        if (radiusMeters <= 0)
                        {
                            continue;
                        }

                        bool isArc = HasDistinctCircularEdgeEnds(dynamicEdge);

                        circles.Add(new VisibleCircle(
                            Id: $"{SafeViewName(view)}:Edge{index}",
                            Edge: edge,
                            DiameterMm: radiusMeters * 2000.0,
                            IsArc: isArc,
                            CenterX: circleParams[0],
                            CenterY: circleParams[1],
                            CenterZ: circleParams[2]));
                    }
                    catch
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                _log($"[基础尺寸] 读取可见圆边失败，视图={SafeViewName(view)}，原因={ex.Message}");
            }

            return circles;
        }

        private static bool HasDistinctCircularEdgeEnds(dynamic edge)
        {
            try
            {
                object? startVertexObject = edge.GetStartVertex();
                object? endVertexObject = edge.GetEndVertex();
                if (startVertexObject == null || endVertexObject == null)
                {
                    return false;
                }

                dynamic startVertex = startVertexObject;
                dynamic endVertex = endVertexObject;
                object startPointObject = startVertex.GetPoint();
                object endPointObject = endVertex.GetPoint();
                if (startPointObject is not double[] startPoint || endPointObject is not double[] endPoint ||
                    startPoint.Length < 3 || endPoint.Length < 3)
                {
                    return false;
                }

                double dx = startPoint[0] - endPoint[0];
                double dy = startPoint[1] - endPoint[1];
                double dz = startPoint[2] - endPoint[2];
                return Math.Sqrt(dx * dx + dy * dy + dz * dz) > 0.000001;
            }
            catch
            {
                return false;
            }
        }

        private List<VisibleLineEdge> GetVisibleLineEdges(SwView view)
        {
            List<VisibleLineEdge> lineEdges = new();
            try
            {
                object? visibleEdges = view.GetVisibleEntities2(null, (int)SolidWorks.Interop.swconst.swViewEntityType_e.swViewEntityType_Edge);
                if (visibleEdges is not object[] edges)
                {
                    return lineEdges;
                }

                int index = 0;
                foreach (object edge in edges)
                {
                    index++;
                    try
                    {
                        dynamic dynamicEdge = edge;
                        object curveObject = dynamicEdge.GetCurve();
                        dynamic curve = curveObject;
                        bool isLine = curve.IsLine();
                        if (!isLine)
                        {
                            continue;
                        }

                        object? startVertexObject = dynamicEdge.GetStartVertex();
                        object? endVertexObject = dynamicEdge.GetEndVertex();
                        if (startVertexObject == null || endVertexObject == null)
                        {
                            continue;
                        }

                        dynamic startVertex = startVertexObject;
                        dynamic endVertex = endVertexObject;
                        object startPointObject = startVertex.GetPoint();
                        object endPointObject = endVertex.GetPoint();
                        if (startPointObject is not double[] startPoint || endPointObject is not double[] endPoint ||
                            startPoint.Length < 3 || endPoint.Length < 3)
                        {
                            continue;
                        }

                        lineEdges.Add(new VisibleLineEdge(
                            Id: $"{SafeViewName(view)}:Line{index}",
                            Edge: edge,
                            CenterX: (startPoint[0] + endPoint[0]) / 2.0,
                            CenterY: (startPoint[1] + endPoint[1]) / 2.0,
                            CenterZ: (startPoint[2] + endPoint[2]) / 2.0));
                    }
                    catch
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                _log($"[基础尺寸] 读取可见直线边失败，视图={SafeViewName(view)}，原因={ex.Message}");
            }

            return lineEdges;
        }

        private static PartBox ReadPartBox(SolidWorks.Interop.sldworks.ModelDoc2 partModel)
        {
            try
            {
                if (partModel is not SolidWorks.Interop.sldworks.PartDoc partDoc)
                {
                    return PartBox.Invalid;
                }

                if (partDoc.GetPartBox(true) is not double[] box || box.Length < 6)
                {
                    return PartBox.Invalid;
                }

                double length = Math.Abs(box[3] - box[0]) * 1000.0;
                double width = Math.Abs(box[4] - box[1]) * 1000.0;
                double height = Math.Abs(box[5] - box[2]) * 1000.0;
                if (length <= 0 || width <= 0 || height <= 0)
                {
                    return PartBox.Invalid;
                }

                return new PartBox(true, length, width, height);
            }
            catch
            {
                return PartBox.Invalid;
            }
        }

        private static string FindViewName(SolidWorks.Interop.sldworks.DrawingDoc drawing, params string[] preferredNames)
        {
            SwView? view = GetFirstModelDrawingView(drawing);
            while (view != null)
            {
                string name = SafeViewName(view);
                string orientation = SafeOrientationName(view);
                if (IsOrthographicDimensionView(view) &&
                    preferredNames.Any(preferred =>
                        name.Contains(preferred, StringComparison.OrdinalIgnoreCase) ||
                        orientation.Contains(preferred, StringComparison.OrdinalIgnoreCase)))
                {
                    return name;
                }

                view = (SwView?)view.GetNextView();
            }

            return "";
        }

        private static SwView? FindView(SolidWorks.Interop.sldworks.DrawingDoc drawing, string viewName)
        {
            SwView? view = GetFirstModelDrawingView(drawing);
            while (view != null)
            {
                if (string.Equals(SafeViewName(view), viewName, StringComparison.OrdinalIgnoreCase))
                {
                    return view;
                }

                view = (SwView?)view.GetNextView();
            }

            return null;
        }

        private static SwView? GetFirstModelDrawingView(SolidWorks.Interop.sldworks.DrawingDoc drawing)
        {
            SwView? sheetView = (SwView?)drawing.GetFirstView();
            return sheetView == null ? null : (SwView?)sheetView.GetNextView();
        }

        private static string SafeViewName(SwView view)
        {
            try
            {
                return view.GetName2();
            }
            catch
            {
                return "";
            }
        }

        private static string SafeOrientationName(SwView view)
        {
            try
            {
                return view.GetOrientationName();
            }
            catch
            {
                return "";
            }
        }

        private static bool IsOrthographicDimensionView(SwView view)
        {
            string name = SafeViewName(view);
            string orientation = SafeOrientationName(view);
            string combined = $"{name} {orientation}";

            if (ContainsAny(combined, "ISO", "Isometric", "Dimetric", "Trimetric", "3D"))
            {
                return false;
            }

            return ContainsAny(
                combined,
                "Front",
                "Back",
                "Left",
                "Right",
                "Top",
                "Bottom",
                "Auto_Front",
                "Auto_Top",
                "Auto_Right",
                "Auto_Left",
                "Auto_Back",
                "Auto_Bottom");
        }

        private static bool ContainsAny(string value, params string[] patterns)
        {
            return patterns.Any(pattern => value.Contains(pattern, StringComparison.OrdinalIgnoreCase));
        }

        private static double[]? GetViewOutline(SwView view)
        {
            try
            {
                return view.GetOutline() as double[];
            }
            catch
            {
                return null;
            }
        }

        private static DimensionCandidate RejectedCandidate(BasicDimensionType type, string reason)
        {
            return new DimensionCandidate
            {
                DimensionType = type,
                FeatureType = DimensionFeatureType.OverallBoundingBox,
                RejectionReason = reason,
                Priority = -1000
            };
        }

        private readonly record struct VisibleCircle(
            string Id,
            object Edge,
            double DiameterMm,
            bool IsArc,
            double CenterX,
            double CenterY,
            double CenterZ);

        private readonly record struct VisibleLineEdge(string Id, object Edge, double CenterX, double CenterY, double CenterZ);

        private readonly record struct PartBox(bool IsValid, double LengthMm, double WidthMm, double HeightMm)
        {
            public static PartBox Invalid => new(false, 0, 0, 0);
        }
    }
}
