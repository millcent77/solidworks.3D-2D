namespace AutoDrawingPro
{
    partial class Form1
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            btnStart = new Button();
            btnStop = new Button();
            txtLog = new TextBox();
            lblInputFolder = new Label();
            txtInputFolder = new TextBox();
            btnBrowseInput = new Button();
            lblOutputFolder = new Label();
            txtOutputFolder = new TextBox();
            btnBrowseOutput = new Button();
            lblTemplateFolder = new Label();
            txtTemplateFolder = new TextBox();
            btnBrowseTemplate = new Button();
            btnSaveSettings = new Button();
            SuspendLayout();
            // 
            // btnStart
            // 
            btnStart.Location = new Point(144, 112);
            btnStart.Name = "btnStart";
            btnStart.Size = new Size(150, 42);
            btnStart.TabIndex = 0;
            btnStart.Text = "启动监听";
            btnStart.UseVisualStyleBackColor = true;
            btnStart.Click += btnStart_Click;
            // 
            // btnStop
            // 
            btnStop.Location = new Point(316, 112);
            btnStop.Name = "btnStop";
            btnStop.Size = new Size(150, 42);
            btnStop.TabIndex = 1;
            btnStop.Text = "停止监听";
            btnStop.UseVisualStyleBackColor = true;
            btnStop.Click += btnStop_Click;
            // 
            // txtLog
            // 
            txtLog.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            txtLog.Location = new Point(14, 160);
            txtLog.Multiline = true;
            txtLog.Name = "txtLog";
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.Size = new Size(772, 266);
            txtLog.TabIndex = 13;
            // 
            // lblInputFolder
            // 
            lblInputFolder.AutoSize = true;
            lblInputFolder.Location = new Point(14, 17);
            lblInputFolder.Name = "lblInputFolder";
            lblInputFolder.Size = new Size(80, 17);
            lblInputFolder.TabIndex = 2;
            lblInputFolder.Text = "监听输入目录";
            // 
            // txtInputFolder
            // 
            txtInputFolder.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtInputFolder.Location = new Point(101, 14);
            txtInputFolder.Name = "txtInputFolder";
            txtInputFolder.Size = new Size(596, 23);
            txtInputFolder.TabIndex = 3;
            // 
            // btnBrowseInput
            // 
            btnBrowseInput.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnBrowseInput.Location = new Point(711, 13);
            btnBrowseInput.Name = "btnBrowseInput";
            btnBrowseInput.Size = new Size(75, 25);
            btnBrowseInput.TabIndex = 4;
            btnBrowseInput.Text = "浏览...";
            btnBrowseInput.UseVisualStyleBackColor = true;
            btnBrowseInput.Click += btnBrowseInput_Click;
            // 
            // lblOutputFolder
            // 
            lblOutputFolder.AutoSize = true;
            lblOutputFolder.Location = new Point(14, 49);
            lblOutputFolder.Name = "lblOutputFolder";
            lblOutputFolder.Size = new Size(72, 17);
            lblOutputFolder.TabIndex = 5;
            lblOutputFolder.Text = "2D导出目录";
            // 
            // txtOutputFolder
            // 
            txtOutputFolder.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtOutputFolder.Location = new Point(101, 46);
            txtOutputFolder.Name = "txtOutputFolder";
            txtOutputFolder.Size = new Size(596, 23);
            txtOutputFolder.TabIndex = 6;
            // 
            // btnBrowseOutput
            // 
            btnBrowseOutput.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnBrowseOutput.Location = new Point(711, 45);
            btnBrowseOutput.Name = "btnBrowseOutput";
            btnBrowseOutput.Size = new Size(75, 25);
            btnBrowseOutput.TabIndex = 7;
            btnBrowseOutput.Text = "浏览...";
            btnBrowseOutput.UseVisualStyleBackColor = true;
            btnBrowseOutput.Click += btnBrowseOutput_Click;
            // 
            // lblTemplateFolder
            // 
            lblTemplateFolder.AutoSize = true;
            lblTemplateFolder.Location = new Point(14, 81);
            lblTemplateFolder.Name = "lblTemplateFolder";
            lblTemplateFolder.Size = new Size(80, 17);
            lblTemplateFolder.TabIndex = 8;
            lblTemplateFolder.Text = "图纸模板目录";
            // 
            // txtTemplateFolder
            // 
            txtTemplateFolder.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtTemplateFolder.Location = new Point(101, 78);
            txtTemplateFolder.Name = "txtTemplateFolder";
            txtTemplateFolder.Size = new Size(596, 23);
            txtTemplateFolder.TabIndex = 9;
            // 
            // btnBrowseTemplate
            // 
            btnBrowseTemplate.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnBrowseTemplate.Location = new Point(711, 77);
            btnBrowseTemplate.Name = "btnBrowseTemplate";
            btnBrowseTemplate.Size = new Size(75, 25);
            btnBrowseTemplate.TabIndex = 10;
            btnBrowseTemplate.Text = "浏览...";
            btnBrowseTemplate.UseVisualStyleBackColor = true;
            btnBrowseTemplate.Click += btnBrowseTemplate_Click;
            // 
            // btnSaveSettings
            // 
            btnSaveSettings.Location = new Point(488, 112);
            btnSaveSettings.Name = "btnSaveSettings";
            btnSaveSettings.Size = new Size(150, 42);
            btnSaveSettings.TabIndex = 12;
            btnSaveSettings.Text = "保存设置";
            btnSaveSettings.UseVisualStyleBackColor = true;
            btnSaveSettings.Click += btnSaveSettings_Click;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 450);
            Controls.Add(btnSaveSettings);
            Controls.Add(btnBrowseTemplate);
            Controls.Add(txtTemplateFolder);
            Controls.Add(lblTemplateFolder);
            Controls.Add(btnBrowseOutput);
            Controls.Add(txtOutputFolder);
            Controls.Add(lblOutputFolder);
            Controls.Add(btnBrowseInput);
            Controls.Add(txtInputFolder);
            Controls.Add(lblInputFolder);
            Controls.Add(txtLog);
            Controls.Add(btnStop);
            Controls.Add(btnStart);
            Name = "Form1";
            Text = "AutoDrawingPro 自动2D出图";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Button btnStart;
        private Button btnStop;
        private TextBox txtLog;
        private Label lblInputFolder;
        private TextBox txtInputFolder;
        private Button btnBrowseInput;
        private Label lblOutputFolder;
        private TextBox txtOutputFolder;
        private Button btnBrowseOutput;
        private Label lblTemplateFolder;
        private TextBox txtTemplateFolder;
        private Button btnBrowseTemplate;
        private Button btnSaveSettings;
    }
}
