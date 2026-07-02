using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Pulso.AiWorker.Services;
using Xunit;

namespace Pulso.AiWorker.Tests;

// Mismo patrón hand-rolled de GeminiStructuredClientTests (HttpMessageHandler de
// prueba, sin Moq/NSubstitute): nunca sube nada a un bucket real.
public class SupabaseStorageServiceTests
{
    private static readonly Guid IncidentId = Guid.Parse("9fa3c1e2-7b40-4d11-8c2a-1e5f6a8b9c0d");

    private static SupabaseStorageService CreateService(FakeHttpMessageHandler handler, bool withConfig = true)
    {
        var settings = withConfig
            ? new Dictionary<string, string?>
            {
                ["Supabase:Url"] = "https://test-project.supabase.co",
                ["Supabase:ServiceRoleKey"] = "test-service-role-key",
                ["Supabase:BucketName"] = "reports"
            }
            : new Dictionary<string, string?>();

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var httpClient = new HttpClient(handler);
        return new SupabaseStorageService(new FakeHttpClientFactory(httpClient), configuration, NullLogger<SupabaseStorageService>.Instance);
    }

    [Fact]
    public async Task UploadAsync_Success_ReturnsRelativeProxyUrl_NotTheRawSupabaseUrl()
    {
        var service = CreateService(new FakeHttpMessageHandler(HttpStatusCode.OK));

        var url = await service.UploadAsync(new byte[] { 1, 2, 3 }, "image/jpeg", IncidentId, CancellationToken.None);

        Assert.Equal($"/api/v1/pulso/media/{IncidentId}.jpg", url);
    }

    [Fact]
    public async Task UploadAsync_Success_SendsExpectedRequest()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK);
        var service = CreateService(handler);

        await service.UploadAsync(new byte[] { 1, 2, 3 }, "image/jpeg", IncidentId, CancellationToken.None);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal($"https://test-project.supabase.co/storage/v1/object/reports/pets/{IncidentId}.jpg",
            handler.LastRequest.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.LastRequest.Headers.Authorization?.Scheme);
        Assert.Equal("test-service-role-key", handler.LastRequest.Headers.Authorization?.Parameter);
        Assert.Equal("true", handler.LastRequest.Headers.GetValues("x-upsert").Single());
        Assert.Equal("image/jpeg", handler.LastRequest.Content?.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task UploadAsync_NonSuccessStatus_ReturnsNull()
    {
        var service = CreateService(new FakeHttpMessageHandler(HttpStatusCode.Unauthorized));

        var url = await service.UploadAsync(new byte[] { 1 }, "image/jpeg", IncidentId, CancellationToken.None);

        Assert.Null(url);
    }

    [Fact]
    public async Task UploadAsync_NetworkException_ReturnsNull_DoesNotThrow()
    {
        var service = CreateService(new FakeHttpMessageHandler(throwOnSend: true));

        var url = await service.UploadAsync(new byte[] { 1 }, "image/jpeg", IncidentId, CancellationToken.None);

        Assert.Null(url);
    }

    [Fact]
    public async Task UploadAsync_MissingConfig_ReturnsNull_WithoutSendingRequest()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK);
        var service = CreateService(handler, withConfig: false);

        var url = await service.UploadAsync(new byte[] { 1 }, "image/jpeg", IncidentId, CancellationToken.None);

        Assert.Null(url);
        Assert.Null(handler.LastRequest);
    }

    public sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly bool _throwOnSend;

        public HttpRequestMessage? LastRequest { get; private set; }

        public FakeHttpMessageHandler(HttpStatusCode statusCode) => _statusCode = statusCode;

        public FakeHttpMessageHandler(bool throwOnSend) => _throwOnSend = throwOnSend;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (_throwOnSend)
                throw new HttpRequestException("Simulated network failure.");

            return Task.FromResult(new HttpResponseMessage(_statusCode) { Content = new StringContent("") });
        }
    }

    private sealed class FakeHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
