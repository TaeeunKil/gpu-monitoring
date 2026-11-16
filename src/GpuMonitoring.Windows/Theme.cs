using GpuMonitoring.Core;
using System.Drawing.Drawing2D;
namespace GpuMonitoring.Windows;

internal static class Theme
{
    public static readonly Color Ink = Color.FromArgb(34, 54, 47), Muted = Color.FromArgb(85, 104, 96), Green = Color.FromArgb(49, 106, 80), Pale = Color.FromArgb(235, 243, 236), Line = Color.FromArgb(220, 229, 222), Surface = Color.FromArgb(247, 249, 246), Amber = Color.FromArgb(137, 83, 17), Red = Color.FromArgb(158, 54, 49);
    public static readonly Color[] Services = [Color.FromArgb(78, 128, 104), Color.FromArgb(123, 153, 174), Color.FromArgb(168, 146, 113), Color.FromArgb(139, 139, 179)];
    public static Font Font(float size = 9.5f, bool bold = false) => new("맑은 고딕", size, bold ? FontStyle.Bold : FontStyle.Regular);
    public static Button Button(string text, int width = 100, bool primary = false)
    {
        var b = new Button { Text = text, Width = width, Height = 34, FlatStyle = FlatStyle.Flat, BackColor = primary ? Green : Color.White, ForeColor = primary ? Color.White : Ink, Cursor = Cursors.Hand, Margin = new(4, 4, 4, 4), UseVisualStyleBackColor = false };
        b.FlatAppearance.BorderColor = primary ? Green : Line;
        return b;
    }
    public static Label Label(string text, float size = 9.5f, bool bold = false) => new() { Text = text, Font = Font(size, bold), ForeColor = Ink, AutoSize = true, Margin = new(0, 3, 0, 3) };
    public static DataGridView Grid(params (string title, float width)[] columns)
    {
        var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, RowHeadersVisible = false, BackgroundColor = Color.White, BorderStyle = BorderStyle.None, GridColor = Line, EnableHeadersVisualStyles = false, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing, ColumnHeadersHeight = 38, ShowCellToolTips = true, AccessibleName = "모니터링 데이터" };
        grid.ColumnHeadersDefaultCellStyle = new()
        {
            BackColor = Surface,
            ForeColor = Muted,
            Font = Font(9, true),
            SelectionBackColor = Surface,
            Padding = new(8, 0, 0, 0)
        };
        grid.DefaultCellStyle = new()
        {
            BackColor = Color.White,
            ForeColor = Ink,
            SelectionBackColor = Pale,
            SelectionForeColor = Ink,
            Font = Font(9.5f),
            Padding = new(8, 0, 0, 0),
            NullValue = "—"
        };
        grid.RowTemplate.Height = 38;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        foreach (var (title, width) in columns)
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = title, FillWeight = width, SortMode = DataGridViewColumnSortMode.NotSortable });
        return grid;
    }
    public static string Amount(double? n) => n is null ? "확인 불가" : $"{n / 1024:F2} GiB";
    public static string Percent(double? n) => n is null ? "확인 불가" : $"{n:F1}%";
    public static string Time(DateTimeOffset? time) => time?.ToLocalTime().ToString("MM-dd HH:mm:ss") ?? "미수집";
    public static string Status(string status) => status switch { "live" => "수집 정상", "partial" => "일부 수집", "offline" => "미연결", _ => "확인 필요" };
}

internal sealed class VramBar : Control
{
    public AllocationResult Allocation { get; set; } = new(false, "GPU를 선택하세요.", []);
    public double Total
    {
        get; set;
    }
    public VramBar()
    {
        DoubleBuffered = true;
        Height = 24;
        BackColor = Theme.Surface;
        AccessibleName = "서비스별 VRAM 구성";
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(Theme.Surface);
        if (!Allocation.Available || Total <= 0)
        {
            TextRenderer.DrawText(e.Graphics, "VRAM 비율 확인 불가", Font, ClientRectangle, Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }
        double x = 0;
        int index = 0;
        foreach (var s in Allocation.Slices)
        {
            var color = s.Kind == "free" ? Color.FromArgb(228, 235, 226) : s.Kind == "other" ? Color.FromArgb(177, 184, 175) : Theme.Services[index++ % Theme.Services.Length];
            var end = x + s.MiB / Total * Width;
            using var brush = new SolidBrush(color);
            e.Graphics.FillRectangle(brush, (float)x, 0, Math.Max(0, (float)(end - x)), Height);
            x = end;
        }
    }
}
