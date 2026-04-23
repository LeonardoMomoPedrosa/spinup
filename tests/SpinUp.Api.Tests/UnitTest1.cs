using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SpinUp.Api.Contracts;
using SpinUp.Api.Data;

namespace SpinUp.Api.Tests;

public class ServiceRegistryApiTests : IClassFixture<ServiceRegistryApiTests.CustomFactory>
{
    private readonly HttpClient _client;
    private const string SystemPath = "C:\\Windows\\System32";

    public ServiceRegistryApiTests(CustomFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CrudFlow_Persists_And_Deletes_Service()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/services", new ServiceCreateRequest(
            "ERPCOM",
            "C:\\projects\\erpcom",
            "dotnet run",
            null,
            null));

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<ServiceResponse>();
        Assert.NotNull(created);

        var listResponse = await _client.GetAsync("/api/services");
        listResponse.EnsureSuccessStatusCode();
        var list = await listResponse.Content.ReadFromJsonAsync<List<ServiceResponse>>();
        Assert.NotNull(list);
        Assert.Contains(list!, x => x.Id == created!.Id);

        var updateResponse = await _client.PutAsJsonAsync($"/api/services/{created!.Id}", new ServiceUpdateRequest(
            "ERPCOM-UPDATED",
            "C:\\projects\\erpcom",
            "dotnet watch run",
            "--no-restore",
            new Dictionary<string, string> { ["ASPNETCORE_ENVIRONMENT"] = "Development" }));

        updateResponse.EnsureSuccessStatusCode();
        var updated = await updateResponse.Content.ReadFromJsonAsync<ServiceResponse>();
        Assert.NotNull(updated);
        Assert.Equal("ERPCOM-UPDATED", updated!.Name);

        var deleteResponse = await _client.DeleteAsync($"/api/services/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getDeletedResponse = await _client.GetAsync($"/api/services/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getDeletedResponse.StatusCode);
    }

    [Fact]
    public async Task Create_With_Invalid_Path_Returns_BadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/services", new ServiceCreateRequest(
            "InvalidPathService",
            "relative/path",
            "dotnet run",
            null,
            null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ApiError>();
        Assert.NotNull(error);
        Assert.Equal("validation_error", error!.Code);
    }

    [Fact]
    public async Task Create_With_Duplicate_Name_Returns_Conflict()
    {
        var request = new ServiceCreateRequest(
            "DuplicateName",
            "C:\\projects\\service-a",
            "dotnet run",
            null,
            null);

        var first = await _client.PostAsJsonAsync("/api/services", request);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await _client.PostAsJsonAsync("/api/services", request);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Start_And_Stop_Service_Works()
    {
        var service = await CreateServiceAsync(
            "LifecycleService",
            "ping",
            "127.0.0.1 -n 30");

        var startResponse = await _client.PostAsync($"/api/services/{service.Id}/start", null);
        startResponse.EnsureSuccessStatusCode();

        var started = await startResponse.Content.ReadFromJsonAsync<RuntimeActionResponse>();
        Assert.NotNull(started);
        Assert.True(started!.Success);
        Assert.Equal(RuntimeStatus.Up, started.Runtime.Status);
        Assert.NotNull(started.Runtime.Pid);

        var stopResponse = await _client.PostAsync($"/api/services/{service.Id}/stop", null);
        stopResponse.EnsureSuccessStatusCode();
        var stopped = await stopResponse.Content.ReadFromJsonAsync<RuntimeActionResponse>();

        Assert.NotNull(stopped);
        Assert.True(stopped!.Success);
        Assert.Equal(RuntimeStatus.Down, stopped.Runtime.Status);
    }

    [Fact]
    public async Task Start_When_Already_Running_Returns_Conflict()
    {
        var service = await CreateServiceAsync(
            "AlreadyRunningService",
            "ping",
            "127.0.0.1 -n 30");

        var firstStart = await _client.PostAsync($"/api/services/{service.Id}/start", null);
        firstStart.EnsureSuccessStatusCode();

        var secondStart = await _client.PostAsync($"/api/services/{service.Id}/start", null);
        Assert.Equal(HttpStatusCode.Conflict, secondStart.StatusCode);

        await _client.PostAsync($"/api/services/{service.Id}/stop", null);
    }

    [Fact]
    public async Task Unexpected_Exit_Updates_Runtime_To_Error()
    {
        var service = await CreateServiceAsync(
            "UnexpectedExitService",
            "cmd",
            "/c exit 2");

        var startResponse = await _client.PostAsync($"/api/services/{service.Id}/start", null);
        startResponse.EnsureSuccessStatusCode();

        var runtime = await WaitForRuntimeStatusAsync(service.Id, RuntimeStatus.Error, TimeSpan.FromSeconds(5));
        Assert.Equal(RuntimeStatus.Error, runtime.Status);
        Assert.Equal(2, runtime.LastExitCode);
        Assert.NotNull(runtime.LastError);
    }

    [Fact]
    public async Task StartAll_And_StopAll_Return_Results_For_Each_Service()
    {
        var serviceA = await CreateServiceAsync("BulkServiceA", "ping", "127.0.0.1 -n 30");
        var serviceB = await CreateServiceAsync("BulkServiceB", "ping", "127.0.0.1 -n 30");

        var startAll = await _client.PostAsync("/api/services/start-all", null);
        startAll.EnsureSuccessStatusCode();
        var startPayload = await startAll.Content.ReadFromJsonAsync<BulkRuntimeActionResponse>();
        Assert.NotNull(startPayload);
        Assert.Contains(startPayload!.Results, x => x.Runtime.ServiceId == serviceA.Id);
        Assert.Contains(startPayload.Results, x => x.Runtime.ServiceId == serviceB.Id);

        var stopAll = await _client.PostAsync("/api/services/stop-all", null);
        stopAll.EnsureSuccessStatusCode();
        var stopPayload = await stopAll.Content.ReadFromJsonAsync<BulkRuntimeActionResponse>();
        Assert.NotNull(stopPayload);
        Assert.Contains(stopPayload!.Results, x => x.Runtime.ServiceId == serviceA.Id && x.Success);
        Assert.Contains(stopPayload.Results, x => x.Runtime.ServiceId == serviceB.Id && x.Success);
    }

    [Fact]
    public async Task Logs_Are_Captured_And_Available_From_Endpoint()
    {
        var service = await CreateServiceAsync(
            "LogCaptureService",
            "cmd",
            "/c echo spinup-log-line");

        var startResponse = await _client.PostAsync($"/api/services/{service.Id}/start", null);
        startResponse.EnsureSuccessStatusCode();

        var logs = await WaitForLogsAsync(service.Id, "spinup-log-line", TimeSpan.FromSeconds(5));
        Assert.Contains(logs.Logs, x => x.Stream == "stdout" && x.Message.Contains("spinup-log-line", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<ServiceResponse> CreateServiceAsync(string name, string command, string args)
    {
        var createResponse = await _client.PostAsJsonAsync("/api/services", new ServiceCreateRequest(
            name,
            SystemPath,
            command,
            args,
            null));

        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<ServiceResponse>();
        Assert.NotNull(created);
        return created!;
    }

    private async Task<ServiceRuntimeResponse> WaitForRuntimeStatusAsync(Guid serviceId, RuntimeStatus status, TimeSpan timeout)
    {
        var started = DateTimeOffset.UtcNow;
        while (DateTimeOffset.UtcNow - started < timeout)
        {
            var runtimeResponse = await _client.GetAsync($"/api/services/{serviceId}/runtime");
            runtimeResponse.EnsureSuccessStatusCode();
            var runtime = await runtimeResponse.Content.ReadFromJsonAsync<ServiceRuntimeResponse>();
            Assert.NotNull(runtime);
            if (runtime!.Status == status)
            {
                return runtime;
            }

            await Task.Delay(200);
        }

        throw new TimeoutException($"Timed out waiting for runtime status {status}.");
    }

    private async Task<ServiceLogListResponse> WaitForLogsAsync(Guid serviceId, string text, TimeSpan timeout)
    {
        var started = DateTimeOffset.UtcNow;
        while (DateTimeOffset.UtcNow - started < timeout)
        {
            var response = await _client.GetAsync($"/api/services/{serviceId}/logs");
            response.EnsureSuccessStatusCode();
            var logs = await response.Content.ReadFromJsonAsync<ServiceLogListResponse>();
            Assert.NotNull(logs);

            if (logs!.Logs.Any(x => x.Message.Contains(text, StringComparison.OrdinalIgnoreCase)))
            {
                return logs;
            }

            await Task.Delay(200);
        }

        throw new TimeoutException($"Timed out waiting for logs containing '{text}'.");
    }

    public sealed class CustomFactory : WebApplicationFactory<Program>
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"spinup-tests-{Guid.NewGuid():N}.db");
        private SqliteConnection? _connection;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");

            builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(DbContextOptions<SpinUpDbContext>));

                _connection = new SqliteConnection($"Data Source={_dbPath}");
                _connection.Open();

                services.AddDbContext<SpinUpDbContext>(options =>
                    options.UseSqlite(_connection));

                using var scope = services.BuildServiceProvider().CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<SpinUpDbContext>();
                db.Database.EnsureCreated();
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            _connection?.Dispose();
        }
    }
}