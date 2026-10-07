using System.Net;
using NovelToEink.Core;

namespace NovelToEink.Tests;

/// <summary>テスト用の HTTP。実際の通信はせず、決めた応答を返す／固まる。</summary>
internal sealed class FakeHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    : HttpMessageHandler
{
    public int Calls { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Calls++;
        return respond(request, ct);
    }

    /// <summary>常に同じ本文を 200 で返す。</summary>
    public static FakeHttpHandler Returns(byte[] body) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }));

    public static FakeHttpHandler Returns(string body) => Returns(System.Text.Encoding.UTF8.GetBytes(body));

    /// <summary>応答せずに固まる。HttpClient のタイムアウト（または利用者の中断）でだけ終わる。</summary>
    public static FakeHttpHandler Hangs() =>
        new(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("到達しない");
        });
}

internal static class FakeHttp
{
    /// <summary>
    /// 指定のハンドラを使う HttpFetcher。タイムアウトは短くして、固まるハンドラでもテストが速く終わるようにする。
    /// 実際の HttpClient が TaskCanceledException を投げる経路（本物のタイムアウト）をそのまま通る。
    /// </summary>
    public static HttpFetcher Fetcher(HttpMessageHandler handler, int timeoutMs = 150) =>
        new(handler, TimeSpan.FromMilliseconds(timeoutMs), delayMs: 0);

    /// <summary>1x1 の PNG。ImageProcessor.TryLoad が画像として読める最小のデータ。</summary>
    public static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
}
