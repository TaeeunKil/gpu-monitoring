using System.Globalization;
using System.Text.Json;
namespace GpuMonitoring.Core;

public sealed record GpuState(DateTimeOffset? UpdatedAt, bool Refreshing, List<ServerReading> Servers);
public sealed record ServerReading(string Id, string Name, string Role, string Status, List<string> Errors, List<GpuReading> Gpus);
public sealed record GpuReading(int Index, string Name, string? Uuid, double? TotalMiB, double? UsedMiB, double? Compute, List<ProcessReading> Processes, string MetricSource = "")
{
    public double? MemoryPercent => TotalMiB is > 0 && UsedMiB is >= 0 && UsedMiB <= TotalMiB ? UsedMiB / TotalMiB * 100 : null;
}
public sealed record ProcessReading(int Pid, string Owner, string Container, double? UsedMiB, List<string> Models);
public sealed record MemorySlice(string Label, double MiB, string Kind);
public sealed record AllocationResult(bool Available, string Reason, List<MemorySlice> Slices);
public static class Monitoring
{
    public static GpuState Read(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 48 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("servers", out var servers) || servers.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("GPU API의 servers 배열이 없습니다.");
            var readings = new List<ServerReading>();
            foreach (var s in servers.EnumerateArray())
            {
                if (s.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("서버 항목 형식이 잘못됐습니다.");
                var status = Text(s, "status");
                var offline = status == "offline";
                var gpus = new List<GpuReading>();
                foreach (var g in Array(s, "gpus"))
                {
                    var processes = new List<ProcessReading>();
                    if (!offline)
                        foreach (var p in Array(g, "processes"))
                            processes.Add(new(Int(p, "pid"), Text(p, "owner"), Text(p, "container"), Metric(p, "usedMiB"), Strings(p, "models")));
                    var compute = offline ? null : Metric(g, "utilization");
                    if (compute > 100)
                        compute = null;
                    gpus.Add(new(Int(g, "index"), Text(g, "liveModel") is { Length: > 0 } model ? model : Text(g, "model"),
                        Text(g, "uuid") is { Length: > 0 } uuid ? uuid : null, offline ? null : Metric(g, "totalMiB"), offline ? null : Metric(g, "usedMiB"), compute, processes, Text(g, "source")));
                }
                readings.Add(new(Text(s, "id"), Text(s, "hostname"), Text(s, "role"), status, Strings(s, "errors"), gpus));
            }
            var stamp = DateTimeOffset.TryParse(Text(root, "updatedAt"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) ? at : (DateTimeOffset?)null;
            return new(stamp, root.TryGetProperty("refreshing", out var refresh) && refresh.ValueKind == JsonValueKind.True, readings);
        }
        catch (JsonException e) { throw new InvalidDataException("GPU API의 JSON 응답을 읽을 수 없습니다.", e); }
        catch (InvalidOperationException e) { throw new InvalidDataException("GPU API의 항목 형식이 잘못됐습니다.", e); }
    }
    public static AllocationResult Allocate(GpuReading gpu)
    {
        if (gpu.TotalMiB is not > 0 || gpu.UsedMiB is null)
            return new(false, "VRAM 수집값을 확인할 수 없습니다.", []);
        var total = gpu.TotalMiB.Value;
        var used = gpu.UsedMiB.Value;
        var groups = new Dictionary<string, double>();
        foreach (var p in gpu.Processes)
        {
            if (p.UsedMiB is not > 0)
                continue;
            var name = string.IsNullOrWhiteSpace(p.Owner) ? $"미분류 · PID {p.Pid}" : p.Owner + (p.Models.Count > 1 ? " 외 · 프로세스 합계" : "");
            groups[name] = groups.GetValueOrDefault(name) + p.UsedMiB.Value;
        }
        var measured = groups.Values.Sum();
        if (used > total || measured > used + 0.000001)
            return new(false, "전체·프로세스 VRAM 수집값이 일치하지 않습니다.", []);
        var slices = groups.Select(p => new MemorySlice(p.Key, p.Value, "service")).ToList();
        if (used > measured)
            slices.Add(new("기타 · 미분류", used - measured, "other"));
        if (total > used)
            slices.Add(new("여유", total - used, "free"));
        return new(true, "", slices);
    }
    static IEnumerable<JsonElement> Array(JsonElement o, string name) => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray() : [];
    static List<string> Strings(JsonElement o, string name) => Array(o, name).Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString() ?? "").ToList();
    static string Text(JsonElement o, string name) => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    static int Int(JsonElement o, string name) => o.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) ? n : 0;
    static double? Metric(JsonElement o, string name)
    {
        if (!o.TryGetProperty(name, out var v))
            return null;
        var raw = v.ValueKind == JsonValueKind.String ? v.GetString() : v.ValueKind == JsonValueKind.Number ? v.GetRawText() : null;
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) && n >= 0 ? n : null;
    }
}
