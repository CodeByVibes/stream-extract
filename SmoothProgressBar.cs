namespace StreamExtract;

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
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        using SolidBrush brush = new(_barColor);
        float percent = (_val - _min) / (float)(_max - _min);
        Rectangle rect = ClientRectangle;

        rect.Width = (int)(rect.Width * percent);

        g.FillRectangle(brush, rect);

        Draw3DBorder(g);

        float textSize = Height * 0.30f;
        decimal textPercent = (decimal)Value / Maximum * 100;

        StringFormat sf = new()
        {
            LineAlignment = StringAlignment.Center,
            Alignment = StringAlignment.Center
        };

        g.DrawString((int)textPercent + "%", new Font(DefaultFont.Name, textSize), new SolidBrush(_textColor), ClientRectangle, sf);
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
            int oldValue = _val;

            if (value < _min) _val = _min;
            else if (value > _max) _val = _max;
            else _val = value;

            _ = oldValue;

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
