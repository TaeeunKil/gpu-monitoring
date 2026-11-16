using System.Net;
using System.Net.Sockets;
using System.Text;
using GpuMonitoring.Core;

var tests = new (string, Func<Task>)[]
{
    ("unknown stays null; measured zero remains zero", () => Run(() => {
        var g = Parse("null", "0").Servers[0].Gpus[0]; Equal(null, g.UsedMiB); Equal(0d,g.Compute); Equal(null,g.MemoryPercent);
        Equal(0d,Parse("0").Servers[0].Gpus[0].MemoryPercent);
    })),
    ("offline cannot reuse prior/inventory readings", () => Run(() => {
        var g=Monitoring.Read(Json("500",status:"offline")).Servers[0].Gpus[0];Equal(null,g.UsedMiB);Equal(null,g.TotalMiB);Equal(null,g.Compute);Equal(0,g.Processes.Count);
    })),
    ("VRAM and compute are independent; numeric strings accepted", () => Run(() => {
        var g=Parse("\"900\"","0").Servers[0].Gpus[0];Equal(90d,g.MemoryPercent);Equal(0d,g.Compute);
    })),
    ("shared service, unattributed remainder and free VRAM balance", () => Run(() => {
        var g=new GpuReading(0,"GPU","u",1000,800,0,[new(1,"ASR","c",300,["asr","aligner"]),new(2,"ASR","c",200,["asr","aligner"])]);
        var a=Monitoring.Allocate(g);True(a.Available);Equal(1000d,a.Slices.Sum(x=>x.MiB));Equal(500d,a.Slices[0].MiB);True(a.Slices[0].Label.Contains("프로세스 합계"));Equal(300d,a.Slices.Single(x=>x.Kind=="other").MiB);
    })),
    ("inconsistent totals do not fabricate proportions", () => Run(() => {
        var g=Parse("1100").Servers[0].Gpus[0];Equal(null,g.MemoryPercent);True(!Monitoring.Allocate(g).Available);
        var g2=new GpuReading(0,"GPU","u",1000,10,0,[new(1,"a","",20,[])]);True(!Monitoring.Allocate(g2).Available);
    })),
    ("invalid and unsupported metrics stay unknown", () => Run(() => {
        foreach(var n in new[]{"\"\"","-1","\"NaN\""}) Equal(null,Parse(n).Servers[0].Gpus[0].UsedMiB);
        Equal(null,Parse("0","101").Servers[0].Gpus[0].Compute);
    })),
    ("malformed root rejected instead of showing a healthy empty screen", () => Run(() => {
        Throws<InvalidDataException>(()=>Monitoring.Read("{}"));Throws<InvalidDataException>(()=>Monitoring.Read("oops"));
    })),
    ("SQLite persists unknown and zero separately", () => WithStore(store=> {
        store.Save(Parse("null","0"),"demo",90);var r=store.Samples("demo").Single();Equal(null,r.UsedMiB);Equal(0d,r.Compute);
    })),
    ("same snapshot is recorded once", () => WithStore(store=> {
        var s=Parse("300");True(store.Save(s,"demo",90));True(!store.Save(s,"demo",90));Equal(1,store.Samples("demo").Count);
    })),
    ("alarm threshold entry deduplicates until recovery", () => WithStore(store=> {
        store.Save(Parse("950",at:"2026-11-16T01:00:00Z"),"demo",90);
        store.Save(Parse("960",at:"2026-11-16T01:00:01Z"),"demo",90);
        store.Save(Parse("200",at:"2026-11-16T01:00:02Z"),"demo",90);
        store.Save(Parse("950",at:"2026-11-16T01:00:03Z"),"demo",90);
        Equal(3,store.Alarms("demo").Count);Equal(2,store.Alarms("demo").Count(x=>x.Kind=="warning"));
    })),
    ("SQL parameters preserve unusual server names", () => WithStore(store=> {
        var s=Parse("100");s.Servers[0]=s.Servers[0] with {Name="x'; DROP TABLE samples; --"};store.Save(s,"demo",90);Equal(s.Servers[0].Name,store.Samples("demo").Single().Server);
    })),
    ("demo and live histories are separate", () => WithStore(store=> {
        var s=Parse("100");store.Save(s,"demo",90);store.Save(s,"api",90);Equal(1,store.Samples("demo").Count);Equal(1,store.Samples("api").Count);
    })),
    ("bounded sample retention removes oldest rows", () => WithStore(store=> {
        for(int i=0;i<4;i++)store.Save(Parse("200",at:$"2026-11-16T01:00:0{i}Z"),"demo",90);
        Equal(2,store.Samples("demo").Count);
    },2)),
    ("in-progress or unobserved snapshots are not recorded", () => WithStore(store=> {
        True(!store.Save(Parse("200") with {Refreshing=true},"demo",90));True(!store.Save(Parse("200") with {UpdatedAt=null},"demo",90));Equal(0,store.Samples("demo").Count);
    })),
    ("missing reading does not pretend a threshold recovered", () => WithStore(store=> {
        store.Save(Parse("950"),"demo",90);store.Save(Parse("null",at:"2026-11-16T01:00:01Z"),"demo",90);Equal(1,store.Alarms("demo").Count);
        store.Save(Parse("0",at:"2026-11-16T01:00:02Z"),"demo",90);Equal(2,store.Alarms("demo").Count);
    })),
    ("HTTP uses state GET and explicit refresh POST/header", async () => {
        await WithHttp([("200 OK",Json("200")),("202 Accepted","{}")], async (url,requests)=> {
            using var api=new GpuApiClient(url);Equal(20d,(await api.ReadAsync()).Servers[0].Gpus[0].MemoryPercent);await api.RefreshAsync();
            Equal("GET /api/state HTTP/1.1",requests[0].Split('\n')[0].Trim());True(requests[1].StartsWith("POST /api/refresh"));True(requests[1].Contains("X-GPU-View: 1",StringComparison.OrdinalIgnoreCase));
        });
    }),
    ("arbitrary hosts and credential-bearing URLs are rejected", () => Run(()=> {
        Throws<ArgumentException>(()=>new GpuApiClient("http://example.com:4317"));Throws<ArgumentException>(()=>new GpuApiClient("http://user:secret@localhost:4317"));Throws<ArgumentException>(()=>new GpuApiClient("http://localhost:4317/?token=test"));
    })),
    ("HTTP errors and redirect do not become empty success", async () => {
        await WithHttp([("503 Unavailable","error")], async (url,_)=> {using var a=new GpuApiClient(url);await ThrowsAsync<HttpRequestException>(async()=>await a.ReadAsync());});
        await WithHttpHeaders([("302 Found","", "Location: http://example.com/\r\n")], async (url,_)=> {using var a=new GpuApiClient(url);await ThrowsAsync<HttpRequestException>(async()=>await a.ReadAsync());});
    })
};
int failed = 0;
foreach (var (name, test) in tests) { try { await test(); Console.WriteLine("PASS " + name); } catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + ": " + e.GetType().Name + " " + e.Message); } }
Console.WriteLine($"{tests.Length - failed}/{tests.Length} passed");
return failed == 0 ? 0 : 1;

static Task Run(Action a) { a(); return Task.CompletedTask; }
static string Json(string used, string compute = "0", string at = "2026-11-16T01:00:00Z", string status = "live") => $$"""
{"updatedAt":"{{at}}","refreshing":false,"servers":[{"id":"s","hostname":"demo-server","role":"test","status":"{{status}}","errors":[],"gpus":[{"index":0,"model":"GPU","uuid":"u","totalMiB":1000,"usedMiB":{{used}},"utilization":{{compute}},"snapshotUsedMiB":500,"memoryPercent":99,"processes":[]}]}]}
""";
static GpuState Parse(string used, string compute = "0", string at = "2026-11-16T01:00:00Z") => Monitoring.Read(Json(used, compute, at));
static void Equal(object? expected, object? actual) { if (!Equals(expected, actual)) throw new Exception($"expected {expected ?? "null"}, got {actual ?? "null"}"); }
static void True(bool b) { if (!b) throw new Exception("condition false"); }
static void Throws<T>(Action a) where T : Exception { try { a(); } catch (T) { return; } throw new Exception("expected " + typeof(T).Name); }
static async Task ThrowsAsync<T>(Func<Task> a) where T : Exception { try { await a(); } catch (T) { return; } throw new Exception("expected " + typeof(T).Name); }
static Task WithStore(Action<HistoryStore> a, int retention = 5000) { var p = Path.Combine(Path.GetTempPath(), "gpu-test-" + Guid.NewGuid() + ".db"); try { using var s = new HistoryStore(p, retention); a(s); } finally { foreach (var f in new[] { p, p + "-wal", p + "-shm" }) if (File.Exists(f)) File.Delete(f); } return Task.CompletedTask; }
static Task WithHttp((string status, string body)[] responses, Func<string, List<string>, Task> action) => WithHttpHeaders(responses.Select(x => (x.status, x.body, "")).ToArray(), action);
static async Task WithHttpHeaders((string status, string body, string headers)[] responses, Func<string, List<string>, Task> action)
{
    using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    var requests = new List<string>();
    var serve = Task.Run(async () => { foreach (var response in responses) { using var client = await listener.AcceptTcpClientAsync(cancel.Token); using var stream = client.GetStream(); using var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, true); var lines = new List<string>(); while (await reader.ReadLineAsync(cancel.Token) is { Length: > 0 } l) lines.Add(l); requests.Add(string.Join('\n', lines)); var body = Encoding.UTF8.GetBytes(response.body); var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {response.status}\r\nContent-Length: {body.Length}\r\nContent-Type: application/json\r\nConnection: close\r\n{response.headers}\r\n"); await stream.WriteAsync(head, cancel.Token); await stream.WriteAsync(body, cancel.Token); } });
    try
    {
        await action($"http://127.0.0.1:{port}", requests);
        await serve;
    }
    finally { cancel.Cancel(); listener.Stop(); try { await serve; } catch (OperationCanceledException) { } catch (SocketException) { } catch (InvalidOperationException) when (cancel.IsCancellationRequested) { } catch (ObjectDisposedException) when (cancel.IsCancellationRequested) { } }
}
