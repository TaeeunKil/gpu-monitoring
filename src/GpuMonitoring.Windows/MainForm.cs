using System.Text.Json;
using GpuMonitoring.Core;
namespace GpuMonitoring.Windows;

internal sealed class MainForm : Form
{
    readonly ComboBox mode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    readonly ComboBox scenario = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 112 };
    readonly TextBox endpoint = new() { Text = "http://127.0.0.1:4317", Width = 215 };
    readonly Button apply = Theme.Button("적용", 65), refresh = Theme.Button("현황 갱신", 105, true);
    readonly CheckBox auto = new() { Text = "3초 자동조회", Checked = true, AutoSize = true };
    readonly NumericUpDown threshold = new() { Minimum = 1, Maximum = 100, Value = 90, Width = 54 };
    readonly Label badge = Theme.Label("데모 · 가상 데이터", 10, true), summary = Theme.Label("서버를 확인하고 있습니다."), banner = Theme.Label(""), detailTitle = Theme.Label("GPU를 선택하세요.", 12, true), detailMeta = Theme.Label(""), footer = Theme.Label("이력은 이 PC에만 저장됩니다.", 9);
    readonly ListBox serverList = new() { Dock = DockStyle.Fill, DisplayMember = nameof(ServerReading.Name), DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 84, IntegralHeight = false, BorderStyle = BorderStyle.None, BackColor = Theme.Surface, AccessibleName = "서버 목록" };
    readonly DataGridView gpuGrid = Theme.Grid(("GPU", 38), ("모델", 130), ("VRAM 사용 / 전체", 155), ("점유율", 75), ("GPU 연산", 80), ("상태", 80));
    readonly DataGridView processes = Theme.Grid(("PID", 55), ("서비스", 125), ("모델 / 공유 프로세스", 175), ("VRAM", 75));
    readonly DataGridView samples = Theme.Grid(("수집 시각", 105), ("서버", 110), ("GPU", 155), ("VRAM 사용", 95), ("전체", 95), ("GPU 연산", 85));
    readonly DataGridView alarms = Theme.Grid(("발생 시각", 105), ("서버", 110), ("GPU", 140), ("종류", 65), ("내용", 230));
    readonly TabControl tabs = new() { Dock = DockStyle.Fill };
    readonly VramBar bar = new() { Dock = DockStyle.Top };
    readonly FlowLayoutPanel legend = new() { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, BackColor = Color.White };
    readonly System.Windows.Forms.Timer timer = new() { Interval = 3000 };
    readonly HistoryStore store;
    readonly string? smokeFolder;
    CancellationTokenSource sourceCancel = new();
    GpuApiClient? api;
    GpuState? current;
    bool demo = true, busy, stale, rendering, resourcesDisposed;
    int generation, step;
    string source = "demo", storageError = "";
    public int ResultCode
    {
        get; private set;
    }

    public MainForm(string dataFolder, string? smokeFolder)
    {
        this.smokeFolder = smokeFolder;
        store = new(Path.Combine(dataFolder, "history.db"));
        Text = "GPU Monitoring — Windows";
        Font = Theme.Font();
        ForeColor = Theme.Ink;
        BackColor = Color.White;
        ClientSize = new(1360, 880);
        MinimumSize = new(1060, 730);
        StartPosition = FormStartPosition.CenterScreen;
        mode.Items.AddRange(["데모 데이터", "로컬 API"]);
        mode.SelectedIndex = 0;
        scenario.Items.AddRange(["일반", "메모리 경고", "수집 실패"]);
        scenario.SelectedIndex = 0;
        mode.AccessibleName = "데이터 출처";
        scenario.AccessibleName = "데모 시나리오";
        endpoint.AccessibleName = "로컬 수집기 주소";
        gpuGrid.AccessibleName = "GPU 현황";
        processes.AccessibleName = "GPU 프로세스";
        samples.AccessibleName = "수집 이력";
        alarms.AccessibleName = "VRAM 알람 이력";
        foreach (var c in new Control[] { mode, scenario, endpoint, auto, threshold })
            c.Margin = new(5, 7, 5, 4);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(20, 12, 20, 8), ColumnCount = 1, RowCount = 5, BackColor = Color.White };
        root.ColumnStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.Absolute, 74));
        root.RowStyles.Add(new(SizeType.Absolute, 78));
        root.RowStyles.Add(new(SizeType.Absolute, 44));
        root.RowStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.Absolute, 26));
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        header.ColumnStyles.Add(new(SizeType.Percent, 100));
        header.ColumnStyles.Add(new(SizeType.AutoSize));
        header.Controls.Add(Theme.Label("GPU Monitoring", 21, true), 0, 0);
        header.Controls.Add(Theme.Label("GPU 메모리와 서비스 점유를 한 화면에서 확인합니다.", 10), 0, 1);
        badge.Padding = new(10, 7, 10, 7);
        badge.BackColor = Theme.Pale;
        badge.ForeColor = Theme.Green;
        badge.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        header.Controls.Add(badge, 1, 0);
        root.Controls.Add(header, 0, 0);
        var commands = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, BackColor = Theme.Surface, Padding = new(7, 6, 7, 3) };
        commands.Controls.AddRange([mode, scenario, endpoint, apply, refresh, auto]);
        var thresholdLabel = Theme.Label("VRAM 알람 기준", 9);
        thresholdLabel.Margin = new(12, 10, 2, 4);
        commands.Controls.Add(thresholdLabel);
        commands.Controls.Add(threshold);
        var pct = Theme.Label("%", 9);
        pct.Margin = new(0, 10, 0, 0);
        commands.Controls.Add(pct);
        root.Controls.Add(commands, 0, 1);
        var info = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        info.ColumnStyles.Add(new(SizeType.Percent, 100));
        info.ColumnStyles.Add(new(SizeType.AutoSize));
        banner.AutoSize = false;
        banner.Dock = DockStyle.Fill;
        banner.TextAlign = ContentAlignment.MiddleLeft;
        summary.TextAlign = ContentAlignment.MiddleRight;
        summary.Dock = DockStyle.Fill;
        info.Controls.Add(banner, 0, 0);
        info.Controls.Add(summary, 1, 0);
        root.Controls.Add(info, 0, 2);
        BuildTabs();
        root.Controls.Add(tabs, 0, 3);
        footer.Dock = DockStyle.Fill;
        footer.TextAlign = ContentAlignment.MiddleLeft;
        footer.ForeColor = Theme.Muted;
        root.Controls.Add(footer, 0, 4);
        Controls.Add(root);
        mode.SelectedIndexChanged += (_, _) => UpdateSourceControls();
        apply.Click += async (_, _) => await ApplySourceAsync();
        refresh.Click += async (_, _) => await PollAsync(true);
        scenario.SelectedIndexChanged += async (_, _) => { if (demo) await PollAsync(false); };
        timer.Tick += async (_, _) => { if (auto.Checked) await PollAsync(false); };
        serverList.SelectedIndexChanged += (_, _) => RenderServer();
        gpuGrid.SelectionChanged += (_, _) => RenderGpu();
        serverList.DrawItem += DrawServer;
        threshold.ValueChanged += (_, _) => footer.Text = "새 알람 기준은 다음 수집부터 적용됩니다. 이력은 이 PC에 저장됩니다.";
        FormClosing += (_, _) => { timer.Stop(); sourceCancel.Cancel(); };
        UpdateSourceControls();
        var tip = new ToolTip();
        tip.SetToolTip(refresh, "로컬 API 모드에서는 로컬 수집 API에 새 조회를 요청합니다.");
        tip.SetToolTip(endpoint, "로컬 수집 API의 loopback HTTP 주소. 인증정보를 입력하지 마세요.");
        tip.SetToolTip(auto, "API 상태를 읽는 주기입니다. SSH 수집을 자동 반복하지 않습니다.");
        Shown += async (_, _) => { await ApplySourceAsync(); timer.Start(); if (smokeFolder is not null) await SmokeAsync(smokeFolder); };
    }
    void BuildTabs()
    {
        var overview = new TabPage("실시간 현황") { BackColor = Color.White, Padding = new(8) };
        var history = new TabPage("이력 · 알람") { BackColor = Color.White, Padding = new(10) };
        tabs.TabPages.AddRange([overview, history]);
        // WinForms validates panel minimums immediately, including during construction.
        var split = new SplitContainer { Size = new(1250, 600), Dock = DockStyle.Fill, SplitterDistance = 235, FixedPanel = FixedPanel.Panel1, Panel1MinSize = 190, Panel2MinSize = 650, BackColor = Theme.Line };
        split.Panel1.BackColor = Theme.Surface;
        split.Panel2.BackColor = Color.White;
        var left = new Panel { Dock = DockStyle.Fill, Padding = new(8) };
        var serverHeader = Theme.Label("서버", 11, true);
        serverHeader.Dock = DockStyle.Top;
        serverHeader.Height = 32;
        serverHeader.AutoSize = false;
        left.Controls.Add(serverList);
        left.Controls.Add(serverHeader);
        split.Panel1.Controls.Add(left);
        var right = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(14, 0, 0, 0), ColumnCount = 1, RowCount = 4 };
        right.RowStyles.Add(new(SizeType.Absolute, 175));
        right.RowStyles.Add(new(SizeType.Absolute, 58));
        right.RowStyles.Add(new(SizeType.Absolute, 115));
        right.RowStyles.Add(new(SizeType.Percent, 100));
        right.ColumnStyles.Add(new(SizeType.Percent, 100));
        right.Controls.Add(gpuGrid, 0, 0);
        var title = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        title.ColumnStyles.Add(new(SizeType.Percent, 100));
        title.RowStyles.Add(new(SizeType.Absolute, 25));
        title.RowStyles.Add(new(SizeType.Percent, 100));
        title.Controls.Add(detailTitle, 0, 0);
        detailMeta.ForeColor = Theme.Muted;
        detailMeta.Font = Theme.Font(8.5f);
        title.Controls.Add(detailMeta, 0, 1);
        right.Controls.Add(title, 0, 1);
        var allocation = new Panel { Dock = DockStyle.Fill, Padding = new(0, 0, 0, 8) };
        var note = Theme.Label("VRAM은 프로세스 전체 점유입니다. 공유 모델은 합산값입니다.", 9);
        note.ForeColor = Theme.Muted;
        note.Dock = DockStyle.Bottom;
        note.AutoSize = false;
        note.Height = 26;
        allocation.Controls.Add(legend);
        allocation.Controls.Add(note);
        allocation.Controls.Add(bar);
        right.Controls.Add(allocation, 0, 2);
        right.Controls.Add(processes, 0, 3);
        split.Panel2.Controls.Add(right);
        overview.Controls.Add(split);
        var historySplit = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        historySplit.RowStyles.Add(new(SizeType.Absolute, 30));
        historySplit.RowStyles.Add(new(SizeType.Percent, 55));
        historySplit.RowStyles.Add(new(SizeType.Absolute, 38));
        historySplit.RowStyles.Add(new(SizeType.Percent, 45));
        historySplit.ColumnStyles.Add(new(SizeType.Percent, 100));
        historySplit.Controls.Add(Theme.Label("수집 이력 · 현재 데이터 출처", 11, true), 0, 0);
        historySplit.Controls.Add(samples, 0, 1);
        historySplit.Controls.Add(Theme.Label("VRAM 알람 · 기준 진입과 복구 때 기록", 11, true), 0, 2);
        historySplit.Controls.Add(alarms, 0, 3);
        history.Controls.Add(historySplit);
    }
    void UpdateSourceControls()
    {
        var d = mode.SelectedIndex == 0;
        scenario.Enabled = d;
        endpoint.Enabled = !d;
    }
    async Task ApplySourceAsync()
    {
        GpuApiClient? next = null;
        var nextDemo = mode.SelectedIndex == 0;
        try
        {
            if (!nextDemo)
                next = new(endpoint.Text.Trim());
        }
        catch (ArgumentException e) { ShowBanner(e.Message, true); return; }
        sourceCancel.Cancel();
        sourceCancel.Dispose();
        sourceCancel = new();
        generation++;
        api?.Dispose();
        api = next;
        demo = nextDemo;
        source = demo ? "demo" : "api:" + endpoint.Text.Trim().TrimEnd('/');
        busy = false;
        stale = false;
        current = null;
        serverList.Items.Clear();
        gpuGrid.Rows.Clear();
        processes.Rows.Clear();
        bar.Allocation = new(false, "데이터를 기다리고 있습니다.", []);
        bar.Invalidate();
        ClearLegend();
        badge.Text = demo ? "데모 · 가상 데이터" : "로컬 API · 읽기 전용";
        badge.BackColor = demo ? Theme.Pale : Color.FromArgb(235, 240, 248);
        badge.ForeColor = demo ? Theme.Green : Color.FromArgb(49, 79, 113);
        RenderHistory();
        await PollAsync(false);
    }
    async Task PollAsync(bool collect)
    {
        if (busy || IsDisposed)
            return;
        busy = true;
        refresh.Enabled = false;
        var gen = generation;
        var token = sourceCancel.Token;
        try
        {
            GpuState result;
            if (demo)
                result = DemoData.Next(scenario.Text, step++);
            else
            {
                if (collect)
                    await api!.RefreshAsync(token);
                result = await api!.ReadAsync(token);
            }
            if (gen != generation || token.IsCancellationRequested || IsDisposed)
                return;
            current = result;
            stale = false;
            storageError = "";
            try
            {
                store.Save(result, source, (double)threshold.Value);
            }
            catch (Exception e) when (e is Microsoft.Data.Sqlite.SqliteException or IOException) { storageError = "조회 정상 · 이력 저장 실패: " + e.Message; }
            RenderSnapshot();
            RenderHistory();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception e) when (e is HttpRequestException or InvalidDataException or TaskCanceledException or IOException or ObjectDisposedException)
        {
            if (gen == generation && !IsDisposed)
            {
                stale = true;
                ShowBanner("수집기에 연결하지 못했습니다. " + (current is null ? "주소와 실행 상태를 확인하세요." : "화면은 마지막 응답입니다. ") + e.Message, true);
                MarkStale();
            }
        }
        finally { if (gen == generation && !IsDisposed) { busy = false; refresh.Enabled = current?.Refreshing != true; } }
    }
    void RenderSnapshot()
    {
        if (current is null)
            return;
        var selected = (serverList.SelectedItem as ServerReading)?.Id;
        serverList.BeginUpdate();
        rendering = true;
        serverList.Items.Clear();
        foreach (var s in current.Servers)
            serverList.Items.Add(s);
        var index = current.Servers.FindIndex(s => s.Id == selected);
        serverList.SelectedIndex = current.Servers.Count == 0 ? -1 : Math.Max(0, index);
        rendering = false;
        serverList.EndUpdate();
        summary.Text = $"서버 {current.Servers.Count}대 · GPU {current.Servers.Sum(s => s.Gpus.Count)}장 · {Theme.Time(current.UpdatedAt)}";
        var errors = current.Servers.Where(s => s.Status != "live" || s.Errors.Count > 0).ToList();
        var highCount = current.Servers.SelectMany(s => s.Gpus).Count(g => g.MemoryPercent >= (double)threshold.Value);
        ShowBanner(storageError.Length > 0 ? storageError : current.Refreshing ? "새 수집 진행 중 · 화면은 직전 응답입니다." : errors.Count > 0 ? $"{errors.Count}대에서 일부 항목을 확인하지 못했습니다. 서버를 선택해 오류를 확인하세요." : highCount > 0 ? $"VRAM 경고 · {highCount}개 GPU가 기준 {threshold.Value}% 이상입니다. 이력 탭에서 확인하세요." : demo ? "가상 장비와 측정값으로 동작을 보여주는 데모입니다." : "로컬 수집 API의 측정값을 읽었습니다.", storageError.Length > 0 || errors.Count > 0 || highCount > 0);
        RenderServer();
        footer.Text = demo ? "데모/실제 이력은 분리됩니다. 샘플·알람은 각각 최근 5,000건을 이 PC에 보관합니다." : "읽기 전용 클라이언트 · API 조회와 장비 수집 갱신은 구분됩니다. 이력은 이 PC에 보관합니다.";
    }
    void RenderServer()
    {
        if (rendering)
            return;
        rendering = true;
        var oldIndex = gpuGrid.CurrentRow?.Index ?? 0;
        gpuGrid.Rows.Clear();
        if (serverList.SelectedItem is ServerReading s)
        {
            foreach (var g in s.Gpus)
            {
                var idx = gpuGrid.Rows.Add(g.Index, g.Name, $"{Theme.Amount(g.UsedMiB)} / {Theme.Amount(g.TotalMiB)}", Theme.Percent(g.MemoryPercent), Theme.Percent(g.Compute), stale ? "이전 응답" : Theme.Status(s.Status));
                gpuGrid.Rows[idx].Tag = g;
                if (s.Status == "offline")
                    gpuGrid.Rows[idx].DefaultCellStyle.ForeColor = Theme.Muted;
                else if (g.MemoryPercent >= (double)threshold.Value)
                    gpuGrid.Rows[idx].Cells[3].Style.ForeColor = Theme.Amber;
            }
            if (gpuGrid.Rows.Count > 0)
                gpuGrid.CurrentCell = gpuGrid.Rows[Math.Min(oldIndex, gpuGrid.Rows.Count - 1)].Cells[0];
            if (s.Errors.Count > 0)
                ShowBanner(string.Join(" · ", s.Errors), true);
        }
        rendering = false;
        RenderGpu();
        serverList.Invalidate();
    }
    void RenderGpu()
    {
        if (rendering)
            return;
        processes.Rows.Clear();
        ClearLegend();
        if (gpuGrid.CurrentRow?.Tag is not GpuReading g)
        {
            detailTitle.Text = "GPU를 선택하세요.";
            detailMeta.Text = "";
            bar.Allocation = new(false, "데이터 없음", []);
            bar.Invalidate();
            return;
        }
        detailTitle.Text = $"GPU {g.Index} · {g.Name} · VRAM 구성";
        detailMeta.Text = $"수집 근거: {(g.MetricSource.Length == 0 ? "확인 불가" : g.MetricSource)}  ·  UUID: {g.Uuid ?? "미수집"}";
        bar.Allocation = Monitoring.Allocate(g);
        bar.Total = g.TotalMiB ?? 0;
        bar.Invalidate();
        if (!bar.Allocation.Available)
        {
            var l = Theme.Label(bar.Allocation.Reason, 9);
            l.ForeColor = Theme.Amber;
            legend.Controls.Add(l);
        }
        else
        {
            int index = 0;
            foreach (var a in bar.Allocation.Slices)
            {
                var color = a.Kind == "free" ? Color.FromArgb(228, 235, 226) : a.Kind == "other" ? Color.FromArgb(177, 184, 175) : Theme.Services[index++ % Theme.Services.Length];
                var group = new FlowLayoutPanel { AutoSize = true, Height = 26, WrapContents = false, Margin = new(0, 6, 15, 0) };
                group.Controls.Add(new Panel { BackColor = color, Size = new(10, 10), Margin = new(0, 7, 5, 0) });
                group.Controls.Add(Theme.Label($"{a.Label}  {Theme.Amount(a.MiB)} ({a.MiB / bar.Total * 100:F1}%)", 8.5f));
                legend.Controls.Add(group);
            }
        }
        foreach (var p in g.Processes)
            processes.Rows.Add(p.Pid, string.IsNullOrWhiteSpace(p.Owner) ? "미분류" : p.Owner, string.Join(" / ", p.Models), Theme.Amount(p.UsedMiB));
    }
    void ClearLegend()
    {
        foreach (Control c in legend.Controls.Cast<Control>().ToArray())
        {
            legend.Controls.Remove(c);
            c.Dispose();
        }
    }
    void RenderHistory()
    {
        samples.Rows.Clear();
        alarms.Rows.Clear();
        foreach (var r in store.Samples(source))
            samples.Rows.Add(Theme.Time(DateTimeOffset.Parse(r.Time)), r.Server, r.Gpu, Theme.Amount(r.UsedMiB), Theme.Amount(r.TotalMiB), Theme.Percent(r.Compute));
        foreach (var a in store.Alarms(source))
        {
            var n = alarms.Rows.Add(Theme.Time(DateTimeOffset.Parse(a.Time)), a.Server, a.Gpu, a.Kind == "warning" ? "경고" : "복구", a.Message);
            alarms.Rows[n].DefaultCellStyle.ForeColor = a.Kind == "warning" ? Theme.Amber : Theme.Green;
        }
    }
    void ShowBanner(string text, bool warning)
    {
        banner.Text = text;
        banner.ForeColor = warning ? Theme.Amber : Theme.Muted;
    }
    void MarkStale()
    {
        serverList.Invalidate();
        if (current is not null)
            foreach (DataGridViewRow r in gpuGrid.Rows)
                r.Cells[5].Value = "이전 응답";
        summary.Text = current is null ? "응답 없음" : "마지막 응답 · " + Theme.Time(current.UpdatedAt);
    }
    void DrawServer(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= serverList.Items.Count)
            return;
        var s = (ServerReading)serverList.Items[e.Index];
        bool selected = (e.State & DrawItemState.Selected) != 0;
        using (var b = new SolidBrush(selected ? Theme.Pale : Theme.Surface))
            e.Graphics.FillRectangle(b, e.Bounds);
        var bounds = new Rectangle(e.Bounds.X + 12, e.Bounds.Y + 11, e.Bounds.Width - 22, 24);
        using (var font = Theme.Font(10, true))
            TextRenderer.DrawText(e.Graphics, s.Name, font, bounds, Theme.Ink, TextFormatFlags.EndEllipsis);
        bounds.Y += 25;
        using (var font = Theme.Font(8.5f))
            TextRenderer.DrawText(e.Graphics, s.Role, font, bounds, Theme.Muted, TextFormatFlags.EndEllipsis);
        bounds.Y += 19;
        using (var font = Theme.Font(8.5f))
            TextRenderer.DrawText(e.Graphics, $"{s.Gpus.Count} GPU · {(stale ? "이전 응답" : Theme.Status(s.Status))}", font, bounds, s.Status == "offline" ? Theme.Amber : Theme.Green, TextFormatFlags.EndEllipsis);
        if ((e.State & DrawItemState.Focus) != 0)
            e.DrawFocusRectangle();
    }
    async Task SmokeAsync(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            timer.Stop();
            auto.Checked = false;
            await Task.Delay(250);
            if (serverList.Items.Count != 3 || gpuGrid.Rows.Count != 2)
                throw new Exception("기본 서버/GPU 표시 실패");
            SavePreview(Path.Combine(folder, "01-overview.png"));
            scenario.SelectedItem = "메모리 경고";
            await PollAsync(false);
            if (store.Alarms("demo").All(a => a.Kind != "warning"))
                throw new Exception("임계값 알람 저장 실패");
            tabs.SelectedIndex = 1;
            await Task.Delay(100);
            SavePreview(Path.Combine(folder, "02-history.png"));
            tabs.SelectedIndex = 0;
            scenario.SelectedItem = "수집 실패";
            await PollAsync(false);
            serverList.SelectedIndex = 2;
            if (gpuGrid.Rows[0].Cells[3].Value?.ToString() != "확인 불가")
                throw new Exception("수집 실패가0으로 표시됨");
            SavePreview(Path.Combine(folder, "03-offline.png"));
            ClientSize = new(1080, 750);
            await Task.Delay(100);
            SavePreview(Path.Combine(folder, "04-small-window.png"));
            ClientSize = new(1360, 880);
            mode.SelectedIndex = 1;
            endpoint.Text = "http://127.0.0.1:1";
            await ApplySourceAsync();
            if (!banner.Text.Contains("연결하지 못"))
                throw new Exception("API 실패 안내 없음");
            SavePreview(Path.Combine(folder, "05-api-unavailable.png"));
            mode.SelectedIndex = 0;
            scenario.SelectedIndex = 0;
            await ApplySourceAsync();
            tabs.SelectedIndex = 0;
            File.WriteAllText(Path.Combine(folder, "result.json"), JsonSerializer.Serialize(new
            {
                passed = true,
                serverCount = serverList.Items.Count,
                sampleRows = store.Samples("demo").Count,
                alarmRows = store.Alarms("demo").Count,
                checks = new[] { "native controls", "server selection", "GPU unknown vs zero", "threshold warning", "SQLite history", "failed collector", "small window", "HTTP failure", "mode recovery" }
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) { ResultCode = 1; File.WriteAllText(Path.Combine(folder, "failure.txt"), e.ToString()); }
        finally { Close(); }
    }
    void SavePreview(string path)
    {
        using var image = new Bitmap(Width, Height);
        DrawToBitmap(image, new Rectangle(0, 0, Width, Height));
        image.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !resourcesDisposed)
        {
            resourcesDisposed = true;
            timer.Dispose();
            sourceCancel.Cancel();
            sourceCancel.Dispose();
            api?.Dispose();
            store.Dispose();
        }
        base.Dispose(disposing);
    }
}
