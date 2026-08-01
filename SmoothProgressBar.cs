namespace StreamExtract;

public static class ProgressMath
{
    public static float Percent(int value, int min, int max)
        => Math.Clamp((value - min) / Math.Max(1f, (float)max - min), 0f, 1f);
}

public class SmoothProgressBar : UserControl
{
    private int _min;
    private int _max = 100;
    private int _val = 0;
    private Color _barColor = Color.FromArgb(100, 100, 130, 255);
    private Color _textColor = Color.Black;

    public SmoothProgressBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.DoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using SolidBrush brush = new(_barColor);
        float percent = ProgressMath.Percent(_val, _min, _max);
        Rectangle rect = ClientRectangle;

        rect.Width = (int)(rect.Width * percent);

        e.Graphics.FillRectangle(brush, rect);

        Draw3DBorder(e.Graphics);

        float textSize = Height * 0.30f;
        int textPercent = (int)(ProgressMath.Percent(Value, Minimum, Maximum) * 100);

        using StringFormat sf = new()
        {
            LineAlignment = StringAlignment.Center,
            Alignment = StringAlignment.Center
        };
        using Font textFont = new(DefaultFont.Name, textSize);
        using SolidBrush textBrush = new(_textColor);

        e.Graphics.DrawString(textPercent + "%", textFont, textBrush, ClientRectangle, sf);
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Minimum
    {
        get => _min;
        set
        {
            if (value < 0) value = 0;
            if (value > _max) _max = value;
            _min = value;
            if (_val < _min) _val = _min;
            Invalidate();
        }
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Maximum
    {
        get => _max;
        set
        {
            if (value < _min) _min = value;
            _max = value;
            if (_val > _max) _val = _max;
            Invalidate();
        }
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Value
    {
        get => _val;
        set
        {
            if (value < _min) _val = _min;
            else if (value > _max) _val = _max;
            else _val = value;

            Invalidate();
        }
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color ProgressBarColor
    {
        get => _barColor;
        set { _barColor = value; Invalidate(); }
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public new Color ForeColor
    {
        get => _textColor;
        set { _textColor = value; Invalidate(); }
    }

    private void Draw3DBorder(Graphics g)
    {
        int penWidth = (int)Pens.White.Width;

        g.DrawLine(Pens.DarkGray,
            new Point(ClientRectangle.Left, ClientRectangle.Top),
            new Point(ClientRectangle.Width - penWidth, ClientRectangle.Top));
        g.DrawLine(Pens.DarkGray,
            new Point(ClientRectangle.Left, ClientRectangle.Top),
            new Point(ClientRectangle.Left, ClientRectangle.Height - penWidth));
        g.DrawLine(Pens.White,
            new Point(ClientRectangle.Left, ClientRectangle.Height - penWidth),
            new Point(ClientRectangle.Width - penWidth, ClientRectangle.Height - penWidth));
        g.DrawLine(Pens.White,
            new Point(ClientRectangle.Width - penWidth, ClientRectangle.Top),
            new Point(ClientRectangle.Width - penWidth, ClientRectangle.Height - penWidth));
    }
}
