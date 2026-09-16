using System.Text.Json;

namespace AutoDrawingPro
{
    internal sealed class DimensionReviewForm : Form
    {
        private readonly List<DimensionCandidate> _candidates;
        private readonly IReadOnlyDictionary<PartCategory, PartDimensionRule> _rules;
        private readonly string _draftFolder;
        private readonly Action<string> _log;

        private ComboBox cboCategory = null!;
        private ComboBox cboView = null!;
        private ComboBox cboType = null!;
        private DataGridView grid = null!;
        private Label lblSummary = null!;
        private TextBox txtNote = null!;

        public PartCategory RecommendedCategory { get; }

        public PartCategory SelectedCategory { get; private set; }

        public bool Confirmed { get; private set; }

        public DimensionReviewForm(
            List<DimensionCandidate> candidates,
            IReadOnlyDictionary<PartCategory, PartDimensionRule> rules,
            PartCategory recommendedCategory,
            string draftFolder,
            Action<string> log)
        {
            _candidates = candidates;
            _rules = rules;
            RecommendedCategory = recommendedCategory;
            SelectedCategory = recommendedCategory;
            _draftFolder = draftFolder;
            _log = log;

            InitializeComponent();
            PopulateFilters();
            RefreshGrid();
        }

        private void InitializeComponent()
        {
            Text = "候选尺寸确认";
            StartPosition = FormStartPosition.CenterParent;
            Width = 1180;
            Height = 720;
            MinimizeBox = false;
            MaximizeBox = true;

            Label lblCategory = new()
            {
                Text = "零件分类",
                Left = 12,
                Top = 14,
                Width = 70
            };
            cboCategory = new()
            {
                Left = 88,
                Top = 10,
                Width = 230,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cboCategory.SelectedIndexChanged += (_, _) => ChangeCategory();

            Label lblRecommended = new()
            {
                Text = $"推荐: {PartClassificationRules.GetDisplayName(RecommendedCategory)}",
                Left = 330,
                Top = 14,
                Width = 260
            };

            Label lblView = new()
            {
                Text = "视图",
                Left = 600,
                Top = 14,
                Width = 40
            };
            cboView = new()
            {
                Left = 642,
                Top = 10,
                Width = 150,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cboView.SelectedIndexChanged += (_, _) => RefreshGrid();

            Label lblType = new()
            {
                Text = "类型",
                Left = 808,
                Top = 14,
                Width = 40
            };
            cboType = new()
            {
                Left = 850,
                Top = 10,
                Width = 170,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cboType.SelectedIndexChanged += (_, _) => RefreshGrid();

            grid = new()
            {
                Left = 12,
                Top = 44,
                Width = 1138,
                Height = 530,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            grid.CellValueChanged += (_, e) =>
            {
                if (e.RowIndex >= 0 && grid.Columns[e.ColumnIndex].Name == "Selected")
                {
                    UpdateSelectionFromRow(grid.Rows[e.RowIndex]);
                    UpdateSummary();
                }
            };
            grid.CurrentCellDirtyStateChanged += (_, _) =>
            {
                if (grid.IsCurrentCellDirty)
                {
                    grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
                }
            };
            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Selected", HeaderText = "保留", FillWeight = 35 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "状态", ReadOnly = true, FillWeight = 80 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Type", HeaderText = "尺寸类型", ReadOnly = true, FillWeight = 80 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Value", HeaderText = "数值", ReadOnly = true, FillWeight = 70 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "View", HeaderText = "视图", ReadOnly = true, FillWeight = 95 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Source", HeaderText = "来源特征", ReadOnly = true, FillWeight = 95 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Datum", HeaderText = "基准", ReadOnly = true, FillWeight = 95 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Reason", HeaderText = "判断/冲突原因", ReadOnly = true, FillWeight = 210 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Note", HeaderText = "工程师备注", ReadOnly = false, FillWeight = 120 });

            Button btnKeepGreen = new()
            {
                Text = "保留全部绿色",
                Left = 12,
                Top = 586,
                Width = 120,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            btnKeepGreen.Click += (_, _) => BatchSet(DimensionReviewStatus.Green, true, false);

            Button btnClearRed = new()
            {
                Text = "取消全部红色",
                Left = 140,
                Top = 586,
                Width = 120,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            btnClearRed.Click += (_, _) => BatchSet(DimensionReviewStatus.Red, false, false);

            Button btnKeepYellow = new()
            {
                Text = "黄色改为保留",
                Left = 268,
                Top = 586,
                Width = 120,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            btnKeepYellow.Click += (_, _) => BatchSet(DimensionReviewStatus.Yellow, true, false);

            Button btnUnkeepGreen = new()
            {
                Text = "绿色改不保留",
                Left = 396,
                Top = 586,
                Width = 120,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            btnUnkeepGreen.Click += (_, _) => BatchSet(DimensionReviewStatus.Green, false, false);

            Button btnLocate = new()
            {
                Text = "定位对应视图",
                Left = 524,
                Top = 586,
                Width = 120,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            btnLocate.Click += (_, _) => LocateCurrent();

            Button btnSaveDraft = new()
            {
                Text = "保存草稿",
                Left = 652,
                Top = 586,
                Width = 100,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            btnSaveDraft.Click += (_, _) => SaveDraft();

            txtNote = new()
            {
                Left = 760,
                Top = 586,
                Width = 220,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
                PlaceholderText = "修改原因/备注"
            };

            Button btnConfirm = new()
            {
                Text = "确认并生成正式图",
                Left = 790,
                Top = 632,
                Width = 160,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };
            btnConfirm.Click += (_, _) => Confirm();

            Button btnCancel = new()
            {
                Text = "取消",
                Left = 960,
                Top = 632,
                Width = 90,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };
            btnCancel.Click += (_, _) =>
            {
                Confirmed = false;
                DialogResult = DialogResult.Cancel;
                Close();
            };

            lblSummary = new()
            {
                Left = 12,
                Top = 638,
                Width = 760,
                Height = 24,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };

            Controls.AddRange(new Control[]
            {
                lblCategory, cboCategory, lblRecommended, lblView, cboView, lblType, cboType,
                grid, btnKeepGreen, btnClearRed, btnKeepYellow, btnUnkeepGreen, btnLocate,
                btnSaveDraft, txtNote, lblSummary, btnConfirm, btnCancel
            });
        }

        private void PopulateFilters()
        {
            cboCategory.Items.Clear();
            foreach (PartCategory category in Enum.GetValues<PartCategory>())
            {
                cboCategory.Items.Add(new CategoryItem(category));
            }
            cboCategory.SelectedItem = cboCategory.Items.Cast<CategoryItem>().First(i => i.Category == SelectedCategory);

            cboView.Items.Clear();
            cboView.Items.Add("全部");
            foreach (string viewName in _candidates.Select(c => c.ViewName).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct().OrderBy(v => v))
            {
                cboView.Items.Add(viewName);
            }
            cboView.SelectedIndex = 0;

            cboType.Items.Clear();
            cboType.Items.Add("全部");
            foreach (BasicDimensionType type in Enum.GetValues<BasicDimensionType>())
            {
                cboType.Items.Add(type.ToString());
            }
            cboType.SelectedIndex = 0;
        }

        private void ChangeCategory()
        {
            if (cboCategory.SelectedItem is not CategoryItem item)
            {
                return;
            }

            SelectedCategory = item.Category;
            if (!_rules.TryGetValue(SelectedCategory, out PartDimensionRule? rule))
            {
                return;
            }

            foreach (DimensionCandidate candidate in _candidates)
            {
                candidate.RuleName = rule.Name;
                if (!rule.AllowedDimensionTypes.Contains(candidate.DimensionType))
                {
                    candidate.ReviewStatus = DimensionReviewStatus.Red;
                    candidate.IsSelected = false;
                    candidate.ReviewReason = $"分类已切换，当前规则不允许 {candidate.DimensionType}";
                }
                else if (candidate.ReviewStatus == DimensionReviewStatus.Red)
                {
                    candidate.IsSelected = false;
                }
                else if (rule.AllowAutoKeep && candidate.ReviewStatus == DimensionReviewStatus.Green)
                {
                    candidate.IsSelected = true;
                }
                else if (!rule.AllowAutoKeep)
                {
                    candidate.ReviewStatus = DimensionReviewStatus.Yellow;
                    candidate.IsSelected = false;
                    candidate.ReviewReason = "当前分类要求人工确认";
                }
            }

            RefreshGrid();
        }

        private void RefreshGrid()
        {
            if (grid == null || cboView == null || cboType == null)
            {
                return;
            }

            grid.Rows.Clear();
            string viewFilter = cboView.SelectedItem?.ToString() ?? "全部";
            string typeFilter = cboType.SelectedItem?.ToString() ?? "全部";

            foreach (DimensionCandidate candidate in _candidates)
            {
                if (viewFilter != "全部" && !string.Equals(candidate.ViewName, viewFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (typeFilter != "全部" && !string.Equals(candidate.DimensionType.ToString(), typeFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int rowIndex = grid.Rows.Add(
                    candidate.IsSelected,
                    PartClassificationRules.GetStatusDisplayName(candidate.ReviewStatus),
                    candidate.DimensionType,
                    candidate.ValueMm > 0 ? $"{candidate.ValueMm:F2}{candidate.Unit}" : "",
                    candidate.ViewName,
                    string.IsNullOrWhiteSpace(candidate.SourceFeatureId) ? candidate.SourceEdgeId : candidate.SourceFeatureId,
                    candidate.Datum,
                    string.IsNullOrWhiteSpace(candidate.ReviewReason) ? candidate.RejectionReason : candidate.ReviewReason,
                    candidate.EngineerNote);

                DataGridViewRow row = grid.Rows[rowIndex];
                row.Tag = candidate;
                row.Cells["Selected"].ReadOnly = !candidate.CanSelect;
                ApplyRowColor(row, candidate.ReviewStatus);
            }

            UpdateSummary();
        }

        private static void ApplyRowColor(DataGridViewRow row, DimensionReviewStatus status)
        {
            row.DefaultCellStyle.BackColor = status switch
            {
                DimensionReviewStatus.Green => Color.FromArgb(221, 245, 227),
                DimensionReviewStatus.Yellow => Color.FromArgb(255, 246, 204),
                DimensionReviewStatus.Red => Color.FromArgb(255, 225, 225),
                _ => Color.White
            };
        }

        private void UpdateSelectionFromRow(DataGridViewRow row)
        {
            if (row.Tag is not DimensionCandidate candidate)
            {
                return;
            }

            candidate.IsSelected = Convert.ToBoolean(row.Cells["Selected"].Value);
            candidate.EngineerNote = row.Cells["Note"].Value?.ToString() ?? "";
            if (!string.IsNullOrWhiteSpace(txtNote.Text))
            {
                candidate.EngineerNote = txtNote.Text.Trim();
                row.Cells["Note"].Value = candidate.EngineerNote;
            }
        }

        private void BatchSet(DimensionReviewStatus status, bool selected, bool allowRed)
        {
            foreach (DimensionCandidate candidate in _candidates.Where(c => c.ReviewStatus == status))
            {
                if (!candidate.CanSelect)
                {
                    continue;
                }

                if (status == DimensionReviewStatus.Red && selected && !allowRed)
                {
                    continue;
                }

                candidate.IsSelected = selected;
                if (!string.IsNullOrWhiteSpace(txtNote.Text))
                {
                    candidate.EngineerNote = txtNote.Text.Trim();
                }
            }

            RefreshGrid();
        }

        private void LocateCurrent()
        {
            if (grid.CurrentRow?.Tag is DimensionCandidate candidate)
            {
                _log($"[候选定位] 请在工程图中查看视图={candidate.ViewName}，类型={candidate.DimensionType}，来源={candidate.SourceFeatureId}{candidate.SourceEdgeId}");
                MessageBox.Show(this, $"对应视图: {candidate.ViewName}\n尺寸类型: {candidate.DimensionType}", "定位提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void SaveDraft()
        {
            Directory.CreateDirectory(_draftFolder);
            string path = Path.Combine(_draftFolder, $"DRAFT-dimension-review-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            var payload = new
            {
                SelectedCategory = PartClassificationRules.GetDisplayName(SelectedCategory),
                RecommendedCategory = PartClassificationRules.GetDisplayName(RecommendedCategory),
                SavedAt = DateTime.Now,
                Candidates = _candidates.Select(c => new
                {
                    c.DimensionType,
                    c.ValueMm,
                    c.Unit,
                    c.ViewName,
                    c.SourceFeatureId,
                    c.SourceEdgeId,
                    c.Datum,
                    c.ReviewStatus,
                    c.IsSelected,
                    c.CanSelect,
                    c.ReviewReason,
                    c.RejectionReason,
                    c.EngineerNote
                })
            };
            File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
            _log($"[草稿] 已保存候选尺寸草稿: {path}");
            MessageBox.Show(this, $"草稿已保存:\n{path}", "保存草稿", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void Confirm()
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                UpdateSelectionFromRow(row);
            }

            if (_candidates.Any(c => c.ReviewStatus == DimensionReviewStatus.Red && c.IsSelected && string.IsNullOrWhiteSpace(c.EngineerNote)))
            {
                MessageBox.Show(this, "红色尺寸如需保留，必须逐项填写工程师备注/修改原因。", "需要备注", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Confirmed = true;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void UpdateSummary()
        {
            if (lblSummary == null)
            {
                return;
            }

            int green = _candidates.Count(c => c.ReviewStatus == DimensionReviewStatus.Green);
            int yellow = _candidates.Count(c => c.ReviewStatus == DimensionReviewStatus.Yellow);
            int red = _candidates.Count(c => c.ReviewStatus == DimensionReviewStatus.Red);
            int selected = _candidates.Count(c => c.IsSelected);
            lblSummary.Text = $"绿色 {green} / 黄色 {yellow} / 红色 {red} / 当前保留 {selected}。正式图只会包含已勾选尺寸。";
        }

        private sealed class CategoryItem
        {
            public CategoryItem(PartCategory category)
            {
                Category = category;
            }

            public PartCategory Category { get; }

            public override string ToString() => PartClassificationRules.GetDisplayName(Category);
        }
    }
}
