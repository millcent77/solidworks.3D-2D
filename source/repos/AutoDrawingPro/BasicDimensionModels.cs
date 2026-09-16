namespace AutoDrawingPro
{
    internal enum BasicDimensionType
    {
        OverallLength,
        OverallWidth,
        OverallHeight,
        HoleDiameter,
        HoleLocation,
        CenterDistance,
        CylinderDiameter,
        SlotWidth,
        Radius
    }

    internal enum PartCategory
    {
        Unclassified,
        Stamping,
        Machining,
        Casting,
        Mold
    }

    internal enum DimensionReviewStatus
    {
        Green,
        Yellow,
        Red
    }

    internal enum DimensionFeatureType
    {
        OverallBoundingBox,
        Hole,
        Cylinder,
        Slot,
        Radius
    }

    internal enum DimensionDirection
    {
        Horizontal,
        Vertical,
        Diameter,
        Radius
    }

    internal sealed class DimensionCandidate
    {
        public string Id { get; init; } = Guid.NewGuid().ToString("N");

        public BasicDimensionType DimensionType { get; init; }

        public DimensionFeatureType FeatureType { get; init; }

        public double ValueMm { get; init; }

        public string Unit { get; init; } = "mm";

        public string ViewName { get; init; } = "";

        public string SourceFeatureId { get; init; } = "";

        public string SourceFaceId { get; init; } = "";

        public string SourceEdgeId { get; init; } = "";

        public object? Reference1 { get; init; }

        public object? Reference2 { get; init; }

        public DimensionDirection Direction { get; init; }

        public int Priority { get; set; }

        public bool IsDuplicate { get; set; }

        public bool IsReferenceOnly { get; set; }

        public bool IsAmbiguous { get; set; }

        public string SourceKind { get; init; } = "几何推断";

        public string Datum { get; init; } = "模型包围盒";

        public string RejectionReason { get; set; } = "";

        public bool Created { get; set; }

        public int PlacementOrder { get; set; }

        public DimensionReviewStatus ReviewStatus { get; set; } = DimensionReviewStatus.Yellow;

        public bool IsSelected { get; set; }

        public bool CanSelect { get; set; } = true;

        public string ReviewReason { get; set; } = "";

        public string RuleName { get; set; } = "";

        public string EngineerNote { get; set; } = "";

        public string Result => Created ? "已创建" : "未创建";
    }

    internal sealed class BasicDimensionResult
    {
        public int CandidateCount { get; set; }

        public int CreatedCount { get; set; }

        public int DuplicateCount { get; set; }

        public int AmbiguousCount { get; set; }

        public int SpaceRejectedCount { get; set; }

        public int AssociationRejectedCount { get; set; }

        public Dictionary<string, int> DimensionsPerView { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Warnings { get; } = new();
    }
}
