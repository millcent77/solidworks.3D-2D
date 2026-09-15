using System.Text.Json;

namespace AutoDrawingPro
{
    internal sealed class AppSettings
    {
        private static readonly string SettingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AutoDrawingPro");

        private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "appsettings.json");

        public string InputFolder { get; set; } = @"D:\AutoDrawingServer\Input";

        public string OutputFolder { get; set; } = @"D:\AutoDrawingServer\Output";

        public string TemplateFolder { get; set; } = @"D:\AutoDrawingServer\Templates";

        public string DrawingTemplatePath { get; set; } = "";

        public string PaperMode { get; set; } = "Auto";

        public string OutputConflictStrategy { get; set; } = "AutoNumber";

        public bool KeepIntermediateSldprt { get; set; } = true;

        public bool EnableAutomaticDimensions { get; set; } = false;

        public bool EnableBasicDimensions { get; set; } = true;

        public bool EnableOverallDimensions { get; set; } = true;

        public bool EnableHoleDiameterDimensions { get; set; } = true;

        public bool EnableHoleLocationDimensions { get; set; } = true;

        public bool EnableCylinderDiameterDimensions { get; set; } = true;

        public bool EnableSlotWidthDimensions { get; set; } = true;

        public bool EnableRadiusDimensions { get; set; } = true;

        public int MinDimensionsPerView { get; set; } = 6;

        public int MaxDimensionsPerView { get; set; } = 10;

        public int MaxDimensionsPerDrawing { get; set; } = 40;

        public int MaxDiameterDimensionsPerView { get; set; } = 4;

        public int MaxRadiusDimensionsPerView { get; set; } = 4;

        public int MaxCenterDistanceDimensionsPerView { get; set; } = 2;

        public double DuplicateTolerance { get; set; } = 0.01;

        public double MinimumFeatureSize { get; set; } = 1.0;

        public double DimensionOffset { get; set; } = 0.014;

        public double DimensionSpacing { get; set; } = 0.012;

        public int LinearPrecision { get; set; } = 0;

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath);
                    AppSettings? settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null)
                    {
                        settings.ApplyDefaults();
                        return settings;
                    }
                }
            }
            catch
            {
            }

            return new AppSettings();
        }

        public void Save()
        {
            Directory.CreateDirectory(SettingsDirectory);

            string json = JsonSerializer.Serialize(this, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(SettingsPath, json);
        }

        private void ApplyDefaults()
        {
            if (string.IsNullOrWhiteSpace(InputFolder))
            {
                InputFolder = @"D:\AutoDrawingServer\Input";
            }

            if (string.IsNullOrWhiteSpace(OutputFolder))
            {
                OutputFolder = @"D:\AutoDrawingServer\Output";
            }

            if (string.IsNullOrWhiteSpace(TemplateFolder))
            {
                TemplateFolder = @"D:\AutoDrawingServer\Templates";
            }

            if (string.IsNullOrWhiteSpace(PaperMode))
            {
                PaperMode = "Auto";
            }

            if (string.IsNullOrWhiteSpace(OutputConflictStrategy))
            {
                OutputConflictStrategy = "AutoNumber";
            }

            if (MinDimensionsPerView <= 0)
            {
                MinDimensionsPerView = 6;
            }

            if (MaxDimensionsPerView <= 0)
            {
                MaxDimensionsPerView = 10;
            }

            if (MaxDimensionsPerDrawing <= 0)
            {
                MaxDimensionsPerDrawing = 40;
            }

            if (MaxDiameterDimensionsPerView <= 0)
            {
                MaxDiameterDimensionsPerView = 4;
            }

            if (MaxRadiusDimensionsPerView <= 0)
            {
                MaxRadiusDimensionsPerView = 4;
            }

            if (MaxCenterDistanceDimensionsPerView <= 0)
            {
                MaxCenterDistanceDimensionsPerView = 2;
            }

            if (DuplicateTolerance <= 0)
            {
                DuplicateTolerance = 0.01;
            }

            if (MinimumFeatureSize <= 0)
            {
                MinimumFeatureSize = 1.0;
            }

            if (DimensionOffset <= 0)
            {
                DimensionOffset = 0.014;
            }

            if (DimensionSpacing <= 0)
            {
                DimensionSpacing = 0.012;
            }
        }
    }
}
