using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TypedRequestContext.Propagation;

namespace TypedRequestContext.AspNetCore.IntegrationTests;

public class RequestContextMiddlewareIntegrationTests
{
    [Fact]
    public async Task NoMetadataEndpoint_PassesThrough()
    {
        await using var app = await BuildAppAsync(services =>
        {
            services.AddTypedRequestContext();
            services.AddTypedRequestContext<CustomerContext>();
        }, app =>
        {
            app.MapGet("/ping", () => Results.Ok(new { ok = true }));
        });

        var response = await app.Client.GetAsync("/ping");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MetadataEndpoint_ExtractsAndInjectsTypedContext()
    {
        await using var app = await BuildAppAsync(services =>
        {
            services.AddTypedRequestContext();
            services.AddTypedRequestContext<CustomerContext>();
        }, app =>
        {
            app.MapGet("/orders", (CustomerContext context) =>
                Results.Ok(new { context.UserId, context.TenantId }))
                .WithRequestContext<CustomerContext>();
        });

        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var request = new HttpRequestMessage(HttpMethod.Get, "/orders");
        request.Headers.Add("x-claim-sub", userId.ToString());
        request.Headers.Add("x-tenant-id", tenantId.ToString());

        var response = await app.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<Dictionary<string, Guid>>();
        Assert.NotNull(payload);
        Assert.Equal(userId, payload["userId"]);
        Assert.Equal(tenantId, payload["tenantId"]);
    }

    [Fact]
    public async Task MissingRequiredClaim_Returns401()
    {
        await using var app = await BuildAppAsync(services =>
        {
            services.AddTypedRequestContext();
            services.AddTypedRequestContext<CustomerContext>();
        }, app =>
        {
            app.MapGet("/secure", (CustomerContext context) => Results.Ok(context.UserId))
                .WithRequestContext<CustomerContext>();
        });

        var request = new HttpRequestMessage(HttpMethod.Get, "/secure");
        request.Headers.Add("x-tenant-id", Guid.NewGuid().ToString());

        var response = await app.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.NotNull(body);
        Assert.Contains("UserId", body["message"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingRequiredHeader_Returns400()
    {
        await using var app = await BuildAppAsync(services =>
        {
            services.AddTypedRequestContext();
            services.AddTypedRequestContext<CustomerContext>();
        }, app =>
        {
            app.MapGet("/secure", (CustomerContext context) => Results.Ok(context.UserId))
                .WithRequestContext<CustomerContext>();
        });

        var request = new HttpRequestMessage(HttpMethod.Get, "/secure");
        request.Headers.Add("x-claim-sub", Guid.NewGuid().ToString());

        var response = await app.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.NotNull(body);
        Assert.Contains("TenantId", body["message"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task GroupDefaultAndEndpointOverride_UsesLastMetadata()
    {
        await using var app = await BuildAppAsync(services =>
        {
            services.AddTypedRequestContext();
            services.AddTypedRequestContext<GroupContext>();
            services.AddTypedRequestContext<EndpointContext>();
        }, app =>
        {
            var group = app.MapGroup("/g").WithRequestContext<GroupContext>();

            group.MapGet("/default", (GroupContext context) => Results.Ok(context.GroupName));

            group.MapGet("/override", (EndpointContext context) => Results.Ok(context.ServiceName))
                .WithRequestContext<EndpointContext>();
        });

        var defaultRequest = new HttpRequestMessage(HttpMethod.Get, "/g/default");
        defaultRequest.Headers.Add("x-group-name", "group-a");
        var defaultResponse = await app.Client.SendAsync(defaultRequest);

        Assert.Equal(HttpStatusCode.OK, defaultResponse.StatusCode);
        Assert.Equal("group-a", await defaultResponse.Content.ReadAsStringAsync().ContinueWith(t => t.Result.Trim('"')));

        var overrideRequest = new HttpRequestMessage(HttpMethod.Get, "/g/override");
        overrideRequest.Headers.Add("x-service-name", "service-a");
        var overrideResponse = await app.Client.SendAsync(overrideRequest);

        Assert.Equal(HttpStatusCode.OK, overrideResponse.StatusCode);
        Assert.Equal("service-a", await overrideResponse.Content.ReadAsStringAsync().ContinueWith(t => t.Result.Trim('"')));
    }

    [Fact]
    public async Task AccessorIsCleared_BetweenRequests()
    {
        await using var app = await BuildAppAsync(services =>
        {
            services.AddTypedRequestContext();
            services.AddTypedRequestContext<CustomerContext>();
        }, app =>
        {
            app.MapGet("/ctx", (IRequestContextAccessor accessor) =>
                Results.Ok(new { has = accessor.Current is CustomerContext }))
                .WithRequestContext<CustomerContext>();

            app.MapGet("/none", (IRequestContextAccessor accessor) =>
                Results.Ok(new { has = accessor.Current is not null }));
        });

        var first = new HttpRequestMessage(HttpMethod.Get, "/ctx");
        first.Headers.Add("x-claim-sub", Guid.NewGuid().ToString());
        first.Headers.Add("x-tenant-id", Guid.NewGuid().ToString());
        var firstResponse = await app.Client.SendAsync(first);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        var secondResponse = await app.Client.GetAsync("/none");

        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var payload = await secondResponse.Content.ReadFromJsonAsync<Dictionary<string, bool>>();
        Assert.NotNull(payload);
        Assert.False(payload["has"]);
    }

    [Fact]
    public async Task UnregisteredContextType_ThrowsInvalidOperationException()
    {
        await using var app = await BuildAppAsync(services =>
        {
            services.AddTypedRequestContext();
        }, app =>
        {
            app.MapGet("/broken", () => Results.Ok())
                .WithRequestContext<UnregisteredContext>();
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => app.Client.GetAsync("/broken"));

        Assert.Contains("No extractor registered", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PropagationHeaders_ContainContextAndCorrelation()
    {
        await using var app = await BuildAppAsync(services =>
        {
            services.AddTypedRequestContext();
            services.AddTypedRequestContextPropagation();
            services.AddCorrelationId();
            services.AddTypedRequestContext<CustomerContext>();
        }, app =>
        {
            app.MapGet("/outbound", (IPropagationHeadersProvider provider) =>
                Results.Ok(provider.GetCurrentHeaders()))
                .WithRequestContext<CustomerContext>();
        });

        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var request = new HttpRequestMessage(HttpMethod.Get, "/outbound");
        request.Headers.Add("x-claim-sub", userId.ToString());
        request.Headers.Add("x-tenant-id", tenantId.ToString());
        request.Headers.Add("x-correlation-id", "corr-123");

        var response = await app.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var headers = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.NotNull(headers);
        Assert.Equal(userId.ToString(), headers["x-user-id"]);
        Assert.Equal(tenantId.ToString(), headers["x-tenant-id"]);
        Assert.Equal("corr-123", headers["x-correlation-id"]);
    }

    [Fact]
    public async Task CustomExtractor_IsUsed_WhenConfigured()
    {
        await using var app = await BuildAppAsync(services =>
        {
            services.AddTypedRequestContext();
            services.AddTypedRequestContext<CustomExtractedContext>(b =>
                b.UseExtractor<CustomExtractedContextExtractor>());
        }, app =>
        {
            app.MapGet("/custom", (CustomExtractedContext context) => Results.Ok(context.UserId))
                .WithRequestContext<CustomExtractedContext>();
        });

        var expectedId = Guid.NewGuid();
        var request = new HttpRequestMessage(HttpMethod.Get, "/custom");
        request.Headers.Add("x-custom-user", expectedId.ToString());

        var response = await app.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var actualId = Guid.Parse((await response.Content.ReadAsStringAsync()).Trim('"'));
        Assert.Equal(expectedId, actualId);
    }

    private static async Task<TestHostApp> BuildAppAsync(
        Action<IServiceCollection> configureServices,
        Action<WebApplication> mapEndpoints)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        configureServices(builder.Services);

        var app = builder.Build();

        app.Use(async (httpContext, next) =>
        {
            if (httpContext.Request.Headers.TryGetValue("x-claim-sub", out var subHeader)
                && Guid.TryParse(subHeader.ToString(), out var subGuid))
            {
                httpContext.User = new ClaimsPrincipal(
                    new ClaimsIdentity(
                    [
                        new Claim("sub", subGuid.ToString())
                    ],
                    authenticationType: "test"));
            }

            await next(httpContext);
        });

        app.UseTypedRequestContext();
        mapEndpoints(app);

        await app.StartAsync();

        return new TestHostApp(app, app.GetTestClient());
    }

    private sealed class TestHostApp(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    public sealed class CustomerContext : ITypedRequestContext
    {
        [FromClaim("sub"), RequiredContextValue, PropagationKey("x-user-id")]
        public Guid UserId { get; init; }

        [FromHeader("x-tenant-id"), RequiredContextValue, PropagationKey("x-tenant-id")]
        public Guid TenantId { get; init; }
    }

    public sealed class GroupContext : ITypedRequestContext
    {
        [FromHeader("x-group-name"), RequiredContextValue]
        public string GroupName { get; init; } = string.Empty;
    }

    public sealed class EndpointContext : ITypedRequestContext
    {
        [FromHeader("x-service-name"), RequiredContextValue]
        public string ServiceName { get; init; } = string.Empty;
    }

    public sealed class UnregisteredContext : ITypedRequestContext
    {
        [FromHeader("x-tenant-id"), RequiredContextValue]
        public Guid TenantId { get; init; }
    }

    public sealed class CustomExtractedContext : ITypedRequestContext
    {
        public Guid UserId { get; init; }
    }

    public sealed class CustomExtractedContextExtractor : IRequestContextExtractor<CustomExtractedContext>
    {
        public CustomExtractedContext Extract(HttpContext httpContext)
        {
            var raw = httpContext.Request.Headers["x-custom-user"].FirstOrDefault();
            return new CustomExtractedContext
            {
                UserId = Guid.TryParse(raw, out var id) ? id : Guid.Empty
            };
        }
    }

    [Fact]
    public async Task ValidationFailure_Returns400_WithStructuredErrors()
    {
        await using var app = await BuildAppAsync(services =>
        {
            services.AddTypedRequestContext();
            services.AddTypedRequestContext<ValidatedOrderContext>(b => b.EnableValidation());
        }, app =>
        {
            app.MapGet("/order", (ValidatedOrderContext ctx) => Results.Ok(ctx.Code))
                .WithRequestContext<ValidatedOrderContext>();
        });

        var request = new HttpRequestMessage(HttpMethod.Get, "/order");
        request.Headers.Add("x-order-code", "TOOLONGVALUE");

        var response = await app.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ValidationErrorPayload>();
        Assert.NotNull(body);
        Assert.Equal("Request context validation failed.", body.Message);
        Assert.True(body.Errors.ContainsKey("Code"));
    }

    [Fact]
    public async Task ValidationSuccess_ReturnsOk()
    {
        await using var app = await BuildAppAsync(services =>
        {
            services.AddTypedRequestContext();
            services.AddTypedRequestContext<ValidatedOrderContext>(b => b.EnableValidation());
        }, app =>
        {
            app.MapGet("/order", (ValidatedOrderContext ctx) => Results.Ok(ctx.Code))
                .WithRequestContext<ValidatedOrderContext>();
        });

        var request = new HttpRequestMessage(HttpMethod.Get, "/order");
        request.Headers.Add("x-order-code", "AB12");

        var response = await app.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task NoValidation_ExistingBehaviorUnchanged()
    {
        await using var app = await BuildAppAsync(services =>
        {
            services.AddTypedRequestContext();
            services.AddTypedRequestContext<ValidatedOrderContext>(); // no validation configured
        }, app =>
        {
            app.MapGet("/order", (ValidatedOrderContext ctx) => Results.Ok(ctx.Code))
                .WithRequestContext<ValidatedOrderContext>();
        });

        var request = new HttpRequestMessage(HttpMethod.Get, "/order");
        request.Headers.Add("x-order-code", "TOOLONGVALUE"); // would fail validation, but validation not enabled

        var response = await app.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public sealed class ValidatedOrderContext : ITypedRequestContext
    {
        [FromHeader("x-order-code"), MaxLength(4)]
        public string? Code { get; init; }
    }

    private sealed record ValidationErrorPayload(string Message, Dictionary<string, string[]> Errors);
}
