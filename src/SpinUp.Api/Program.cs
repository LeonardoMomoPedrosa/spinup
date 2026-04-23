using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Threading.Channels;
using SpinUp.Api.Contracts;
using SpinUp.Api.Data;
using SpinUp.Api.Infrastructure;
using SpinUp.Api.Models;
using SpinUp.Api.Services;
using SpinUp.Api.Validation;

var builder = WebApplication.CreateBuilder(args);

if (OperatingSystem.IsWindows())
{
    builder.Host.UseWindowsService(options =>
    {
        options.ServiceName = "SpinUp.Api";
    });
}

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDbContext<SpinUpDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("SpinUp")
                      ?? "Data Source=spinup.db"));
builder.Services.Configure<LogBufferOptions>(builder.Configuration.GetSection("LogBuffer"));
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LogBufferOptions>>().Value;
    return options;
});
builder.Services.AddSingleton<IServiceLogStore, InMemoryServiceLogStore>();
builder.Services.AddSingleton<IEventStreamBroadcaster, EventStreamBroadcaster>();
builder.Services.AddSingleton<IServiceRuntimeManager, ServiceRuntimeManager>();
builder.Services.AddHostedService<ServiceHealthMonitor>();
builder.Services.AddScoped<ServiceReadinessProbe>();

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseDefaultFiles();
app.UseStaticFiles();

await EnsureDatabaseAsync(app.Services);

var servicesGroup = app.MapGroup("/api/services");

servicesGroup.MapGet("/", async (SpinUpDbContext db) =>
{
    var services = await db.ServiceDefinitions
        .OrderBy(x => x.Name)
        .Select(x => ServiceResponse.FromEntity(x))
        .ToListAsync();

    return Results.Ok(services);
});

servicesGroup.MapGet("/{id:guid}", async (Guid id, SpinUpDbContext db) =>
{
    var existing = await db.ServiceDefinitions.FindAsync(id);
    if (existing is null)
    {
        return Results.NotFound(new ApiError("not_found", $"Service '{id}' was not found."));
    }

    return Results.Ok(ServiceResponse.FromEntity(existing));
});

servicesGroup.MapPost("/", async (ServiceCreateRequest request, SpinUpDbContext db) =>
{
    var validationErrors = ServiceRequestValidator.Validate(request);
    if (validationErrors.Count > 0)
    {
        return Results.BadRequest(new ApiError("validation_error", "Request validation failed.", validationErrors));
    }

    var normalizedName = request.Name.Trim();
    var nameConflict = await db.ServiceDefinitions.AnyAsync(x => x.Name.ToLower() == normalizedName.ToLower());
    if (nameConflict)
    {
        return Results.Conflict(new ApiError(
            "duplicate_name",
            $"Service name '{normalizedName}' already exists."));
    }

    var now = DateTimeOffset.UtcNow;
    var entity = new ServiceDefinition
    {
        Id = Guid.NewGuid(),
        Name = normalizedName,
        Path = request.Path.Trim(),
        Command = request.Command.Trim(),
        Args = string.IsNullOrWhiteSpace(request.Args) ? null : request.Args.Trim(),
        Env = request.Env ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        CreatedAt = now,
        UpdatedAt = now
    };

    db.ServiceDefinitions.Add(entity);
    await db.SaveChangesAsync();

    return Results.Created($"/api/services/{entity.Id}", ServiceResponse.FromEntity(entity));
});

servicesGroup.MapPut("/{id:guid}", async (Guid id, ServiceUpdateRequest request, SpinUpDbContext db) =>
{
    var validationErrors = ServiceRequestValidator.Validate(request);
    if (validationErrors.Count > 0)
    {
        return Results.BadRequest(new ApiError("validation_error", "Request validation failed.", validationErrors));
    }

    var entity = await db.ServiceDefinitions.FindAsync(id);
    if (entity is null)
    {
        return Results.NotFound(new ApiError("not_found", $"Service '{id}' was not found."));
    }

    var normalizedName = request.Name.Trim();
    var nameConflict = await db.ServiceDefinitions.AnyAsync(x =>
        x.Id != id && x.Name.ToLower() == normalizedName.ToLower());

    if (nameConflict)
    {
        return Results.Conflict(new ApiError(
            "duplicate_name",
            $"Service name '{normalizedName}' already exists."));
    }

    entity.Name = normalizedName;
    entity.Path = request.Path.Trim();
    entity.Command = request.Command.Trim();
    entity.Args = string.IsNullOrWhiteSpace(request.Args) ? null : request.Args.Trim();
    entity.Env = request.Env ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    entity.UpdatedAt = DateTimeOffset.UtcNow;

    await db.SaveChangesAsync();

    return Results.Ok(ServiceResponse.FromEntity(entity));
});

servicesGroup.MapDelete("/{id:guid}", async (Guid id, SpinUpDbContext db) =>
{
    var entity = await db.ServiceDefinitions.FindAsync(id);
    if (entity is null)
    {
        return Results.NotFound(new ApiError("not_found", $"Service '{id}' was not found."));
    }

    db.ServiceDefinitions.Remove(entity);
    await db.SaveChangesAsync();

    return Results.NoContent();
});

servicesGroup.MapPost("/{id:guid}/start", async (Guid id, IServiceRuntimeManager runtime, CancellationToken cancellationToken) =>
{
    var result = await runtime.StartAsync(id, cancellationToken);
    return ToLifecycleResult(result);
});

servicesGroup.MapPost("/{id:guid}/stop", async (Guid id, IServiceRuntimeManager runtime, CancellationToken cancellationToken) =>
{
    var result = await runtime.StopAsync(id, cancellationToken);
    return ToLifecycleResult(result);
});

servicesGroup.MapPost("/{id:guid}/restart", async (Guid id, IServiceRuntimeManager runtime, CancellationToken cancellationToken) =>
{
    var result = await runtime.RestartAsync(id, cancellationToken);
    return ToLifecycleResult(result);
});

servicesGroup.MapPost("/start-all", async (IServiceRuntimeManager runtime, CancellationToken cancellationToken) =>
{
    var result = await runtime.StartAllAsync(cancellationToken);
    return Results.Ok(result);
});

servicesGroup.MapPost("/stop-all", async (IServiceRuntimeManager runtime, CancellationToken cancellationToken) =>
{
    var result = await runtime.StopAllAsync(cancellationToken);
    return Results.Ok(result);
});

servicesGroup.MapGet("/{id:guid}/runtime", async (Guid id, IServiceRuntimeManager runtime, CancellationToken cancellationToken) =>
{
    await runtime.CheckHealthAsync(cancellationToken);
    return Results.Ok(runtime.GetRuntime(id));
});

servicesGroup.MapGet("/runtime", async (IServiceRuntimeManager runtime, CancellationToken cancellationToken) =>
{
    await runtime.CheckHealthAsync(cancellationToken);
    return Results.Ok(runtime.GetAllRuntime());
});

servicesGroup.MapGet("/{id:guid}/logs", (Guid id, IServiceLogStore logs, int? take, DateTimeOffset? since) =>
{
    var entries = logs.GetRecent(id, take ?? 200, since);
    return Results.Ok(new ServiceLogListResponse(id, entries));
});

servicesGroup.MapDelete("/{id:guid}/logs", (Guid id, IServiceLogStore logs) =>
{
    logs.Clear(id);
    return Results.NoContent();
});

app.MapGet("/api/stream", async (HttpContext context, IEventStreamBroadcaster broadcaster, CancellationToken cancellationToken) =>
{
    context.Response.Headers.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";
    context.Response.Headers.Connection = "keep-alive";

    var channel = Channel.CreateUnbounded<StreamEvent>();
    var subscriptionId = broadcaster.Subscribe(channel);

    try
    {
        var heartbeatInterval = TimeSpan.FromSeconds(15);
        var nextHeartbeat = Task.Delay(heartbeatInterval, cancellationToken);
        while (!cancellationToken.IsCancellationRequested)
        {
            var waitForEvent = channel.Reader.WaitToReadAsync(cancellationToken).AsTask();
            var completed = await Task.WhenAny(waitForEvent, nextHeartbeat);

            if (completed == waitForEvent && await waitForEvent)
            {
                while (channel.Reader.TryRead(out var evt))
                {
                    var payload = JsonSerializer.Serialize(evt);
                    await context.Response.WriteAsync($"event: {evt.Type}\n", cancellationToken);
                    await context.Response.WriteAsync($"data: {payload}\n\n", cancellationToken);
                    await context.Response.Body.FlushAsync(cancellationToken);
                }

                continue;
            }

            await context.Response.WriteAsync("event: heartbeat\n", cancellationToken);
            await context.Response.WriteAsync("data: {}\n\n", cancellationToken);
            await context.Response.Body.FlushAsync(cancellationToken);
            nextHeartbeat = Task.Delay(heartbeatInterval, cancellationToken);
        }
    }
    catch (OperationCanceledException)
    {
        // Client disconnected.
    }
    finally
    {
        broadcaster.Unsubscribe(subscriptionId);
    }
});

app.MapGet("/health/live", () =>
{
    return Results.Ok(new
    {
        status = "live",
        utcNow = DateTimeOffset.UtcNow
    });
});

app.MapGet("/health/ready", async (ServiceReadinessProbe readiness, CancellationToken cancellationToken) =>
{
    var result = await readiness.CheckReadinessAsync(cancellationToken);
    if (result.IsReady)
    {
        return Results.Ok(new
        {
            status = "ready",
            utcNow = DateTimeOffset.UtcNow
        });
    }

    return Results.Json(new
    {
        status = "not_ready",
        utcNow = DateTimeOffset.UtcNow,
        issues = result.Issues
    }, statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.MapGet("/health/startup", (ServiceReadinessProbe readiness) =>
{
    return Results.Ok(readiness.BuildStartupDiagnostics());
});

app.MapFallbackToFile("index.html");

app.Run();

static async Task EnsureDatabaseAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<SpinUpDbContext>();
    await db.Database.EnsureCreatedAsync();
}

static IResult ToLifecycleResult(RuntimeActionResponse action)
{
    if (action.Success)
    {
        return Results.Ok(action);
    }

    return action.Code switch
    {
        "not_found" => Results.NotFound(new ApiError(action.Code, action.Message ?? "Service not found.")),
        "already_running" => Results.Conflict(new ApiError(action.Code, action.Message ?? "Service already running.")),
        "invalid_path" => Results.BadRequest(new ApiError(action.Code, action.Message ?? "Invalid service path.")),
        _ => Results.BadRequest(new ApiError(action.Code ?? "runtime_error", action.Message ?? "Runtime operation failed."))
    };
}

public partial class Program;
