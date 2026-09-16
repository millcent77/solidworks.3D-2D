namespace AutoDrawingPro
{
    internal sealed class PartDimensionRule
    {
        public PartCategory Category { get; init; }

        public string Name { get; init; } = "";

        public HashSet<BasicDimensionType> AllowedDimensionTypes { get; init; } = new();

        public Dictionary<BasicDimensionType, int> Priorities { get; init; } = new();

        public int MaxDimensionsPerView { get; init; }

        public int MaxDimensionsPerDrawing { get; init; }

        public double MinimumFeatureSize { get; init; }

        public double DuplicateTolerance { get; init; }

        public bool AllowAutoKeep { get; init; }

        public bool RequiresHumanReview { get; init; }

        public string ConservativeWarning { get; init; } = "";
    }

    internal static class PartClassificationRules
    {
        public static IReadOnlyDictionary<PartCategory, PartDimensionRule> CreateDefaultRules(AppSettings settings)
        {
            return new Dictionary<PartCategory, PartDimensionRule>
            {
                [PartCategory.Unclassified] = new()
                {
                    Category = PartCategory.Unclassified,
                    Name = "未分类——仅生成保守基础尺寸",
                    AllowedDimensionTypes = new()
                    {
                        BasicDimensionType.OverallLength,
                        BasicDimensionType.OverallWidth,
                        BasicDimensionType.OverallHeight,
                        BasicDimensionType.HoleDiameter,
                        BasicDimensionType.CenterDistance
                    },
                    Priorities = PriorityMap(100, 80, 0, 0, 0, 70),
                    MaxDimensionsPerView = Math.Min(settings.MaxDimensionsPerView, 6),
                    MaxDimensionsPerDrawing = Math.Min(settings.MaxDimensionsPerDrawing, 18),
                    MinimumFeatureSize = settings.MinimumFeatureSize,
                    DuplicateTolerance = settings.DuplicateTolerance,
                    AllowAutoKeep = false,
                    RequiresHumanReview = true,
                    ConservativeWarning = "无法可靠分类，默认仅保留外形、孔径和少量中心距候选。"
                },
                [PartCategory.Stamping] = new()
                {
                    Category = PartCategory.Stamping,
                    Name = "冲压件",
                    AllowedDimensionTypes = new()
                    {
                        BasicDimensionType.OverallLength,
                        BasicDimensionType.OverallWidth,
                        BasicDimensionType.OverallHeight,
                        BasicDimensionType.HoleDiameter,
                        BasicDimensionType.HoleLocation,
                        BasicDimensionType.CenterDistance,
                        BasicDimensionType.SlotWidth
                    },
                    Priorities = PriorityMap(100, 90, 70, 0, 80, 85),
                    MaxDimensionsPerView = settings.MaxDimensionsPerView,
                    MaxDimensionsPerDrawing = settings.MaxDimensionsPerDrawing,
                    MinimumFeatureSize = settings.MinimumFeatureSize,
                    DuplicateTolerance = settings.DuplicateTolerance,
                    AllowAutoKeep = true,
                    RequiresHumanReview = true,
                    ConservativeWarning = "冲压件不自动加入所有小圆角、展开尺寸或复杂折弯尺寸。"
                },
                [PartCategory.Machining] = new()
                {
                    Category = PartCategory.Machining,
                    Name = "机加工件",
                    AllowedDimensionTypes = new()
                    {
                        BasicDimensionType.OverallLength,
                        BasicDimensionType.OverallWidth,
                        BasicDimensionType.OverallHeight,
                        BasicDimensionType.HoleDiameter,
                        BasicDimensionType.HoleLocation,
                        BasicDimensionType.CenterDistance,
                        BasicDimensionType.CylinderDiameter,
                        BasicDimensionType.SlotWidth,
                        BasicDimensionType.Radius
                    },
                    Priorities = PriorityMap(100, 90, 80, 70, 75, 85),
                    MaxDimensionsPerView = settings.MaxDimensionsPerView,
                    MaxDimensionsPerDrawing = settings.MaxDimensionsPerDrawing,
                    MinimumFeatureSize = settings.MinimumFeatureSize,
                    DuplicateTolerance = settings.DuplicateTolerance,
                    AllowAutoKeep = true,
                    RequiresHumanReview = true,
                    ConservativeWarning = "机加工件不自动生成公差、粗糙度、形位公差或工艺要求。"
                },
                [PartCategory.Casting] = new()
                {
                    Category = PartCategory.Casting,
                    Name = "铸件",
                    AllowedDimensionTypes = new()
                    {
                        BasicDimensionType.OverallLength,
                        BasicDimensionType.OverallWidth,
                        BasicDimensionType.OverallHeight,
                        BasicDimensionType.HoleDiameter,
                        BasicDimensionType.HoleLocation,
                        BasicDimensionType.CenterDistance,
                        BasicDimensionType.CylinderDiameter,
                        BasicDimensionType.SlotWidth
                    },
                    Priorities = PriorityMap(100, 75, 65, 0, 60, 70),
                    MaxDimensionsPerView = Math.Min(settings.MaxDimensionsPerView, 8),
                    MaxDimensionsPerDrawing = Math.Min(settings.MaxDimensionsPerDrawing, 24),
                    MinimumFeatureSize = settings.MinimumFeatureSize * 1.5,
                    DuplicateTolerance = settings.DuplicateTolerance,
                    AllowAutoKeep = false,
                    RequiresHumanReview = true,
                    ConservativeWarning = "铸件加工余量、拔模斜度、检验基准和关键尺寸必须人工确认。"
                },
                [PartCategory.Mold] = new()
                {
                    Category = PartCategory.Mold,
                    Name = "模具零件",
                    AllowedDimensionTypes = new()
                    {
                        BasicDimensionType.OverallLength,
                        BasicDimensionType.OverallWidth,
                        BasicDimensionType.OverallHeight,
                        BasicDimensionType.HoleDiameter,
                        BasicDimensionType.HoleLocation,
                        BasicDimensionType.CenterDistance,
                        BasicDimensionType.CylinderDiameter,
                        BasicDimensionType.SlotWidth,
                        BasicDimensionType.Radius
                    },
                    Priorities = PriorityMap(100, 95, 85, 60, 75, 90),
                    MaxDimensionsPerView = Math.Min(settings.MaxDimensionsPerView, 8),
                    MaxDimensionsPerDrawing = Math.Min(settings.MaxDimensionsPerDrawing, 28),
                    MinimumFeatureSize = settings.MinimumFeatureSize,
                    DuplicateTolerance = settings.DuplicateTolerance,
                    AllowAutoKeep = true,
                    RequiresHumanReview = true,
                    ConservativeWarning = "模具零件孔和重复特征较多，红色重复尺寸必须逐项确认才可保留。"
                }
            };
        }

        public static PartCategory RecommendCategory(string inputPath, int bodyCount, int surfaceCount)
        {
            string name = Path.GetFileNameWithoutExtension(inputPath);
            if (ContainsAny(name, "mold", "die", "tool", "模具", "模板", "导柱", "导套"))
            {
                return PartCategory.Mold;
            }

            if (ContainsAny(name, "cast", "casting", "铸"))
            {
                return PartCategory.Casting;
            }

            if (ContainsAny(name, "stamp", "sheet", "钣金", "冲压", "折弯"))
            {
                return PartCategory.Stamping;
            }

            if (ContainsAny(name, "mach", "cnc", "车", "铣", "轴", "加工"))
            {
                return PartCategory.Machining;
            }

            return PartCategory.Unclassified;
        }

        public static string GetDisplayName(PartCategory category)
        {
            return category switch
            {
                PartCategory.Stamping => "冲压件",
                PartCategory.Machining => "机加工件",
                PartCategory.Casting => "铸件",
                PartCategory.Mold => "模具零件",
                _ => "未分类——仅生成保守基础尺寸"
            };
        }

        public static string GetStatusDisplayName(DimensionReviewStatus status)
        {
            return status switch
            {
                DimensionReviewStatus.Green => "绿色-建议保留",
                DimensionReviewStatus.Yellow => "黄色-人工确认",
                DimensionReviewStatus.Red => "红色-不建议使用",
                _ => status.ToString()
            };
        }

        private static Dictionary<BasicDimensionType, int> PriorityMap(
            int overall,
            int hole,
            int cylinder,
            int radius,
            int slot,
            int center)
        {
            return new()
            {
                [BasicDimensionType.OverallLength] = overall,
                [BasicDimensionType.OverallWidth] = overall,
                [BasicDimensionType.OverallHeight] = overall,
                [BasicDimensionType.HoleDiameter] = hole,
                [BasicDimensionType.HoleLocation] = center,
                [BasicDimensionType.CenterDistance] = center,
                [BasicDimensionType.CylinderDiameter] = cylinder,
                [BasicDimensionType.Radius] = radius,
                [BasicDimensionType.SlotWidth] = slot
            };
        }

        private static bool ContainsAny(string value, params string[] patterns)
        {
            return patterns.Any(pattern => value.Contains(pattern, StringComparison.OrdinalIgnoreCase));
        }
    }
}
