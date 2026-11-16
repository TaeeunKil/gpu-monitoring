namespace GpuMonitoring.Core;

public static class DemoData
{
    // Entirely fabricated. No operational inventory, endpoint or screenshot is embedded.
    public static GpuState Next(string scenario, int step)
    {
        var wave = (step % 12) * 70d;
        var first = scenario == "메모리 경고" ? 23500 : 18000 + wave;
        var nodes = new List<ServerReading>
        {
            new("demo-a","compute-alpha","LLM · 음성 처리","live",[],[
                Gpu(0,"RTX 4090",24576,first,0,"demo-a-0","Example LLM",first-600),
                Gpu(1,"RTX 4090",24576,8500+wave,36,"demo-a-1","Example ASR",7800,["asr","aligner"])]),
            new("demo-b","inference-beta","임베딩 · 검색","live",[],[
                Gpu(0,"RTX A6000",49152,28000+wave,22,"demo-b-0","Example Embedding",25000)]),
            new("demo-c","edge-gamma","추론 서비스","live",[],[
                Gpu(0,"NVIDIA A2",16384,4200,12,"demo-c-0","Example Reranker",3600)])
        };
        if (scenario == "수집 실패")
            nodes[2] = nodes[2] with
            {
                Status = "offline",
                Errors = ["가상 시나리오: 장비 수집 응답 시간 초과"],
                Gpus = [new(0, "NVIDIA A2", "demo-c-0", null, null, null, [], "데모 · 수집 실패")]
            };
        return new(DateTimeOffset.UtcNow, false, nodes);
    }
    static GpuReading Gpu(int index, string name, double total, double used, double compute, string uuid, string owner, double process, List<string>? models = null)
        => new(index, name, uuid, total, used, compute, [new(1000 + index, owner, "demo-service", process, models ?? ["example/model"])], "가상 데이터");
}
