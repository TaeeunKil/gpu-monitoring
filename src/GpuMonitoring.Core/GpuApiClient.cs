using System.Net;
using System.Text;
namespace GpuMonitoring.Core;
public sealed class GpuApiClient : IDisposable
{
    readonly HttpClient client;
    const int MaxResponse = 4 * 1024 * 1024;
    public GpuApiClient(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != "http" || !uri.IsLoopback || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.AbsolutePath != "/")
            throw new ArgumentException("로컬 수집 API의 로컬 주소만 입력하세요. 예: http://127.0.0.1:4317", nameof(baseUrl));
        client = new(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false })
        {
            BaseAddress = uri,
            Timeout = TimeSpan.FromSeconds(8)
        };
    }
    public async Task<GpuState> ReadAsync(CancellationToken token = default)
    {
        using var response = await client.GetAsync("/api/state", HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxResponse)
            throw new InvalidDataException("GPU API 응답이 너무 큽니다.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, timeout.Token)) > 0)
        {
            if (buffer.Length + read > MaxResponse)
                throw new InvalidDataException("GPU API 응답이 너무 큽니다.");
            buffer.Write(chunk, 0, read);
        }
        return Monitoring.Read(Encoding.UTF8.GetString(buffer.ToArray()));
    }
    public async Task RefreshAsync(CancellationToken token = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/refresh");
        request.Headers.Add("X-GPU-View", "1");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
    }
    public void Dispose() => client.Dispose();
}
