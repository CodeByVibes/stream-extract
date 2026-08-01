using System.ComponentModel;

#nullable disable

namespace StreamExtract;

partial class Form1
{
    private IContainer components = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new Container();
        tvFiles = new TreeView();
        ilIcons = new ImageList(components);
        rtbDebug = new RichTextBox();
        lblBrowseOutputDirectory = new Label();
        txtBrowseOutputDirectory = new TextBox();
        cbUseSourceDirectory = new CheckBox();
        btnBrowseOutputDirectory = new Button();
        lblInputFiles = new Label();
        btnOpenFiles = new Button();
        btnExtract = new Button();
        statusStrip1 = new StatusStrip();
        btnAbout = new Button();
        btnNewVersion = new Button();
        pbProgress = new SmoothProgressBar();
        SuspendLayout();
        // 
        // tvFiles
        // 
        tvFiles.AllowDrop = true;
        tvFiles.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        tvFiles.CheckBoxes = true;
        tvFiles.Font = new Font("Segoe UI", 9F);
        tvFiles.ImageIndex = 0;
        tvFiles.ImageList = ilIcons;
        tvFiles.Location = new Point(8, 107);
        tvFiles.Margin = new Padding(1, 3, 1, 3);
        tvFiles.Name = "tvFiles";
        tvFiles.SelectedImageIndex = 0;
        tvFiles.Size = new Size(676, 236);
        tvFiles.TabIndex = 0;
        // 
        // ilIcons
        // 
        ilIcons.ColorDepth = ColorDepth.Depth8Bit;
        ilIcons.ImageSize = new Size(21, 16);
        ilIcons.TransparentColor = Color.Transparent;
        // 
        // rtbDebug
        // 
        rtbDebug.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        rtbDebug.Location = new Point(8, 348);
        rtbDebug.Margin = new Padding(1, 3, 1, 3);
        rtbDebug.Name = "rtbDebug";
        rtbDebug.Size = new Size(676, 65);
        rtbDebug.TabIndex = 1;
        rtbDebug.Text = "";
        // 
        // lblBrowseOutputDirectory
        // 
        lblBrowseOutputDirectory.AutoSize = true;
        lblBrowseOutputDirectory.Location = new Point(8, 34);
        lblBrowseOutputDirectory.Margin = new Padding(2, 0, 2, 0);
        lblBrowseOutputDirectory.Name = "lblBrowseOutputDirectory";
        lblBrowseOutputDirectory.Size = new Size(95, 15);
        lblBrowseOutputDirectory.TabIndex = 2;
        lblBrowseOutputDirectory.Text = "Output directory";
        // 
        // txtBrowseOutputDirectory
        // 
        txtBrowseOutputDirectory.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        txtBrowseOutputDirectory.Enabled = false;
        txtBrowseOutputDirectory.Location = new Point(8, 52);
        txtBrowseOutputDirectory.Margin = new Padding(2, 2, 2, 2);
        txtBrowseOutputDirectory.Name = "txtBrowseOutputDirectory";
        txtBrowseOutputDirectory.Size = new Size(597, 23);
        txtBrowseOutputDirectory.TabIndex = 3;
        // 
        // cbUseSourceDirectory
        // 
        cbUseSourceDirectory.AutoSize = true;
        cbUseSourceDirectory.Checked = true;
        cbUseSourceDirectory.CheckState = CheckState.Checked;
        cbUseSourceDirectory.Location = new Point(107, 33);
        cbUseSourceDirectory.Margin = new Padding(2, 2, 2, 2);
        cbUseSourceDirectory.Name = "cbUseSourceDirectory";
        cbUseSourceDirectory.Size = new Size(83, 19);
        cbUseSourceDirectory.TabIndex = 4;
        cbUseSourceDirectory.Text = "Use source";
        cbUseSourceDirectory.UseVisualStyleBackColor = true;
        // 
        // btnBrowseOutputDirectory
        // 
        btnBrowseOutputDirectory.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnBrowseOutputDirectory.Enabled = false;
        btnBrowseOutputDirectory.Location = new Point(609, 52);
        btnBrowseOutputDirectory.Margin = new Padding(2, 2, 2, 2);
        btnBrowseOutputDirectory.Name = "btnBrowseOutputDirectory";
        btnBrowseOutputDirectory.Size = new Size(73, 23);
        btnBrowseOutputDirectory.TabIndex = 5;
        btnBrowseOutputDirectory.Text = "Browse";
        btnBrowseOutputDirectory.UseVisualStyleBackColor = true;
        // 
        // lblInputFiles
        // 
        lblInputFiles.AutoSize = true;
        lblInputFiles.Location = new Point(8, 83);
        lblInputFiles.Margin = new Padding(2, 0, 2, 0);
        lblInputFiles.Name = "lblInputFiles";
        lblInputFiles.Size = new Size(59, 15);
        lblInputFiles.TabIndex = 6;
        lblInputFiles.Text = "Input files";
        // 
        // btnOpenFiles
        // 
        btnOpenFiles.Location = new Point(71, 79);
        btnOpenFiles.Margin = new Padding(2, 2, 2, 2);
        btnOpenFiles.Name = "btnOpenFiles";
        btnOpenFiles.Size = new Size(72, 23);
        btnOpenFiles.TabIndex = 7;
        btnOpenFiles.Text = "Open files";
        btnOpenFiles.UseVisualStyleBackColor = true;
        // 
        // btnExtract
        // 
        btnExtract.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnExtract.Enabled = false;
        btnExtract.Location = new Point(609, 79);
        btnExtract.Margin = new Padding(2, 2, 2, 2);
        btnExtract.Name = "btnExtract";
        btnExtract.Size = new Size(73, 23);
        btnExtract.TabIndex = 9;
        btnExtract.Text = "Extract";
        btnExtract.UseVisualStyleBackColor = true;
        // 
        // statusStrip1
        // 
        statusStrip1.ImageScalingSize = new Size(24, 24);
        statusStrip1.Location = new Point(0, 406);
        statusStrip1.Name = "statusStrip1";
        statusStrip1.Padding = new Padding(1, 0, 10, 0);
        statusStrip1.Size = new Size(690, 22);
        statusStrip1.TabIndex = 11;
        statusStrip1.Text = "statusStrip1";
        // 
        // btnAbout
        // 
        btnAbout.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnAbout.Location = new Point(609, 12);
        btnAbout.Margin = new Padding(2, 2, 2, 2);
        btnAbout.Name = "btnAbout";
        btnAbout.Size = new Size(73, 23);
        btnAbout.TabIndex = 12;
        btnAbout.Text = "About";
        btnAbout.UseVisualStyleBackColor = true;
        // 
        // btnNewVersion
        // 
        btnNewVersion.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnNewVersion.BackColor = Color.IndianRed;
        btnNewVersion.ForeColor = Color.White;
        btnNewVersion.Location = new Point(506, 11);
        btnNewVersion.Margin = new Padding(2, 2, 2, 2);
        btnNewVersion.Name = "btnNewVersion";
        btnNewVersion.Size = new Size(99, 24);
        btnNewVersion.TabIndex = 13;
        btnNewVersion.Text = "New version available!";
        btnNewVersion.UseVisualStyleBackColor = false;
        btnNewVersion.Visible = false;
        // 
        // pbProgress
        // 
        pbProgress.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        pbProgress.Location = new Point(146, 83);
        pbProgress.Margin = new Padding(1, 1, 1, 1);
        pbProgress.Name = "pbProgress";
        pbProgress.Size = new Size(459, 15);
        pbProgress.TabIndex = 10;
        // 
        // Form1
        // 
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(690, 428);
        Controls.Add(btnNewVersion);
        Controls.Add(btnAbout);
        Controls.Add(statusStrip1);
        Controls.Add(pbProgress);
        Controls.Add(btnExtract);
        Controls.Add(btnOpenFiles);
        Controls.Add(lblInputFiles);
        Controls.Add(btnBrowseOutputDirectory);
        Controls.Add(cbUseSourceDirectory);
        Controls.Add(txtBrowseOutputDirectory);
        Controls.Add(lblBrowseOutputDirectory);
        Controls.Add(rtbDebug);
        Controls.Add(tvFiles);
        Font = new Font("Segoe UI", 9F);
        Margin = new Padding(1, 3, 1, 3);
        MinimumSize = new Size(355, 316);
        Name = "Form1";
        StartPosition = FormStartPosition.CenterScreen;
        ResumeLayout(false);
        PerformLayout();
    }
}
