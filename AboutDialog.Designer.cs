namespace StreamExtract;

partial class AboutDialog
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
            components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        lblAbout = new Label();
        pbLogo = new PictureBox();
        label1 = new Label();
        llblMkvToolnix = new LinkLabel();
        LlblMp4box = new LinkLabel();
        linkLabel1 = new LinkLabel();
        ((System.ComponentModel.ISupportInitialize)pbLogo).BeginInit();
        SuspendLayout();
        // 
        // lblAbout
        // 
        lblAbout.Font = new Font("Microsoft Sans Serif", 12F);
        lblAbout.Location = new Point(12, 9);
        lblAbout.Name = "lblAbout";
        lblAbout.Size = new Size(259, 31);
        lblAbout.TabIndex = 0;
        lblAbout.Text = "StreamExtract v1.0";
        lblAbout.TextAlign = ContentAlignment.MiddleCenter;
        // 
        // pbLogo
        // 
        pbLogo.Location = new Point(90, 43);
        pbLogo.Name = "pbLogo";
        pbLogo.Size = new Size(100, 100);
        pbLogo.SizeMode = PictureBoxSizeMode.Zoom;
        pbLogo.TabIndex = 1;
        pbLogo.TabStop = false;
        pbLogo.Click += PbLogo_Click;
        // 
        // label1
        // 
        label1.AutoSize = true;
        label1.Location = new Point(106, 146);
        label1.Name = "label1";
        label1.Size = new Size(72, 15);
        label1.TabIndex = 2;
        label1.Text = "Powered by:";
        // 
        // llblMkvToolnix
        // 
        llblMkvToolnix.ForeColor = Color.White;
        llblMkvToolnix.LinkBehavior = LinkBehavior.HoverUnderline;
        llblMkvToolnix.LinkColor = Color.Black;
        llblMkvToolnix.Location = new Point(12, 164);
        llblMkvToolnix.Name = "llblMkvToolnix";
        llblMkvToolnix.Size = new Size(259, 15);
        llblMkvToolnix.TabIndex = 3;
        llblMkvToolnix.TabStop = true;
        llblMkvToolnix.Text = "mkvextract and mkvmerge from MKVToolNix";
        llblMkvToolnix.TextAlign = ContentAlignment.MiddleCenter;
        llblMkvToolnix.LinkClicked += LlblMkvToolnix_LinkClicked;
        // 
        // LlblMp4box
        // 
        LlblMp4box.ForeColor = Color.White;
        LlblMp4box.LinkBehavior = LinkBehavior.HoverUnderline;
        LlblMp4box.LinkColor = Color.Black;
        LlblMp4box.Location = new Point(12, 182);
        LlblMp4box.Name = "LlblMp4box";
        LlblMp4box.Size = new Size(259, 15);
        LlblMp4box.TabIndex = 4;
        LlblMp4box.TabStop = true;
        LlblMp4box.Text = "MP44Box from GPAC";
        LlblMp4box.TextAlign = ContentAlignment.MiddleCenter;
        LlblMp4box.LinkClicked += LlblMp4box_LinkClicked;
        // 
        // linkLabel1
        // 
        linkLabel1.Dock = DockStyle.Bottom;
        linkLabel1.ForeColor = Color.White;
        linkLabel1.LinkBehavior = LinkBehavior.HoverUnderline;
        linkLabel1.LinkColor = Color.Black;
        linkLabel1.Location = new Point(0, 201);
        linkLabel1.Name = "linkLabel1";
        linkLabel1.Size = new Size(283, 55);
        linkLabel1.TabIndex = 5;
        linkLabel1.TabStop = true;
        linkLabel1.Text = "These tools remain the property of their respective authors and are distributed under their original licenses.";
        linkLabel1.TextAlign = ContentAlignment.MiddleCenter;
        // 
        // AboutDialog
        // 
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(283, 256);
        Controls.Add(linkLabel1);
        Controls.Add(LlblMp4box);
        Controls.Add(llblMkvToolnix);
        Controls.Add(label1);
        Controls.Add(pbLogo);
        Controls.Add(lblAbout);
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "AboutDialog";
        StartPosition = FormStartPosition.CenterParent;
        Text = "About StreamExtract";
        ((System.ComponentModel.ISupportInitialize)pbLogo).EndInit();
        ResumeLayout(false);
        PerformLayout();
    }

    private Label lblAbout;
    private PictureBox pbLogo;
    private Label label1;
    private LinkLabel llblMkvToolnix;
    private LinkLabel LlblMp4box;
    private LinkLabel linkLabel1;
}
