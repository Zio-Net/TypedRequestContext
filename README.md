# TypedRequestContext

Typed request context middleware for ASP.NET Core.

Define *per-endpoint* (or per-route-group) strongly-typed “request context” objects, extract them from claims/headers, and access them anywhere in the request pipeline via an `AsyncLocal` accessor.

This repo produces two NuGet packages:

- **`TypedRequestContext`** — core middleware, attributes, accessor, correlation ID
- **`TypedRequestContext.Propagation`** — optional propagation (serialize/deserialize + header provider)

---

## Why

In many services you end up needing the same business identifiers everywhere (tenant, user, role, operation id, etc.). Passing them manually through every method is noisy, and extracting them ad-hoc in each endpoint is easy to get wrong.

`TypedRequestContext` gives you:

- A **typed model** for the “ambient” request context (per endpoint/group)
- **One extraction pipeline** (claims/headers) with clear “required vs optional” semantics
- **Multiple context shapes in the same service** — register many context types and choose per endpoint / route group
- **Ambient access** for deep services (inject the context type directly *or* use `IRequestContextAccessor`)
- Optional **propagation across services and async boundaries** (HTTP headers, queue/event metadata)
- **Correlation ID propagation** via `x-correlation-id` when the propagation package is enabled

---

## Requirements

- `TypedRequestContext`: ASP.NET Core on **.NET 8+**
- `TypedRequestContext.Propagation`: **.NET 8+**

---

## Install

```powershell
dotnet add package TypedRequestContext
```

Optional propagation support:

```powershell
dotnet add package TypedRequestContext.Propagation
```

---

## Quick start (Minimal APIs)

### 1) Define one (or more) context types

```csharp
using TypedRequestContext;

public sealed class CustomerRequestContext : ITypedRequestContext
{
    [FromClaim("sub"), RequiredContextValue]
    public Guid UserId { get; init; }

    [FromClaim("tenant_id"), RequiredContextValue]
    public Guid TenantId { get; init; }

    [FromClaim("role")]
    public string? Role { get; init; }

    [FromHeader("x-operation-id")]
    public Guid? OperationId { get; init; }
}

public sealed class InternalServiceRequestContext : ITypedRequestContext
{
    [FromHeader("x-service-name"), RequiredContextValue]
    public string ServiceName { get; init; } = default!;

    [FromHeader("x-operation-id")]
    public Guid? OperationId { get; init; }
}
```

Notes:

- `[FromClaim]` and `[FromHeader]` declare the *source*.
- `[RequiredContextValue]` makes the request fail early when missing.

### 2) Register services

```csharp
using TypedRequestContext;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddTypedRequestContext()
    .AddCorrelationId() // optional
    .AddTypedRequestContext<CustomerRequestContext>()
    .AddTypedRequestContext<InternalServiceRequestContext>();

var app = builder.Build();
```

### 3) Add middleware

```csharp
using TypedRequestContext;

app.UseAuthentication();
app.UseAuthorization();

app.UseTypedRequestContext();
```

Middleware order matters: it must run **after** auth so claims are available.

### 4) Declare the context required by an endpoint (or group)

```csharp
using TypedRequestContext;

var api = app.MapGroup("/api")
     .WithRequestContext<CustomerRequestContext>();

api.MapGet("/orders", GetOrders);

// Endpoint override: same app, different context shape
api.MapPost("/internal/sync", SyncData)
    .WithRequestContext<InternalServiceRequestContext>();
```

If both group and endpoint specify a context, **last wins** (endpoint overrides group).

Note: one context type is active per request. Different endpoints/groups can require different types.

### 5) Consume the context

In endpoint handlers, resolve the context from DI:

```csharp
using Microsoft.AspNetCore.Mvc;

static IResult GetOrders([FromServices] CustomerRequestContext ctx)
{
    return Results.Ok(new { ctx.TenantId, ctx.UserId, ctx.Role });
}
```

In deeper services you have two options:

1) Inject the typed context directly (it’s registered as scoped when you call `AddTypedRequestContext<T>()`):

```csharp
public sealed class BillingService(CustomerRequestContext ctx)
{
    public Guid TenantId => ctx.TenantId;
}
```

2) Use `IRequestContextAccessor` when you need a looser coupling (e.g., interface-based access, optional access, libraries that shouldn’t depend on a specific context type):

```csharp
using TypedRequestContext;

public sealed class AuditService(IRequestContextAccessor accessor)
{
    public void Write(string message)
    {
        var ctx = accessor.GetRequired<CustomerRequestContext>();
        // Use ctx.TenantId / ctx.UserId...
    }
}
```

---

## How extraction works

- You register context types via `AddTypedRequestContext<TContext>()`.
- You attach the desired context type to endpoints/groups via `WithRequestContext<TContext>()`.
- For requests hitting such endpoints, the middleware:
  - Creates the context via an extractor (`IRequestContextExtractor<TContext>`)
  - Stores it into `IRequestContextAccessor.Current` for the request lifetime

The default extractor uses cached reflection and the attributes on your context properties.

### Missing required values

If a `[RequiredContextValue]` property is missing:

- For **claims**: the middleware returns **401**
- For **headers**: the middleware returns **403**

The response body is JSON:

```json
{ "message": "Required context value 'TenantId' is missing." }
```

---

## Correlation ID (optional)

Register once:

```csharp
builder.Services.AddCorrelationId();
```

When enabled:

- Reads `x-correlation-id` from the inbound request if present
- Otherwise falls back to `Activity.Current?.TraceId` (when available)
- Otherwise generates a new ID
- Echoes the value back on the response header `x-correlation-id`

Consume it via `ICorrelationContext`:

```csharp
using TypedRequestContext;

public sealed class LoggingService(ICorrelationContext correlation)
{
    public string CurrentId => correlation.CorrelationId;
}
```

---

## Propagation (optional package)

Install the package and register propagation services:

```csharp
using TypedRequestContext;
using TypedRequestContext.Propagation;

builder.Services
    .AddTypedRequestContext()
    .AddTypedRequestContextPropagation();
```

What you get:

- `IPropagationHeadersProvider.GetCurrentHeaders()` returns the **current typed context serialized** as key/value pairs.
- If correlation is enabled, the provider also includes `x-correlation-id` so it naturally flows to downstream services.

### Mark propagated values

Propagation is opt-in per property using `[PropagationKey]`.

`PropagationKeyAttribute` lives in the propagation package namespace:

```csharp
using TypedRequestContext;
using TypedRequestContext.Propagation;

public sealed class CustomerRequestContext : ITypedRequestContext
{
    [FromClaim("sub"), RequiredContextValue, PropagationKey("x-user-id")]
    public Guid UserId { get; init; }

    [FromClaim("tenant_id"), RequiredContextValue, PropagationKey("x-tenant-id")]
    public Guid TenantId { get; init; }

    [FromClaim("role"), PropagationKey("x-role")]
    public string? Role { get; init; }

    // No PropagationKey => stays local; not serialized
    [FromClaim("email")]
    public string? Email { get; init; }
}
```

### Outbound HTTP calls

At outbound call time, use `IPropagationHeadersProvider` to get the current headers:

```csharp
using TypedRequestContext.Propagation;

public sealed class DownstreamClient(
    HttpClient http,
    IPropagationHeadersProvider headers)
{
    public async Task<HttpResponseMessage> CallAsync(CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/data");

        foreach (var (key, value) in headers.GetCurrentHeaders())
            request.Headers.TryAddWithoutValidation(key, value);

        return await http.SendAsync(request, ct);
    }
}
```

Because `GetCurrentHeaders()` includes `x-correlation-id` (when correlation is enabled), a downstream ASP.NET Core service using `AddCorrelationId()` will automatically pick it up on inbound requests.

### Non-HTTP: deserialize from metadata

For queues/events/background jobs, you can serialize and carry the same headers dictionary as message metadata.

Producer side (create metadata):

```csharp
using TypedRequestContext.Propagation;

public sealed class Producer(IPropagationHeadersProvider headers)
{
    public IReadOnlyDictionary<string, string> CreateMetadata()
        => headers.GetCurrentHeaders();
}
```

Consumer side (restore context):

```csharp
using TypedRequestContext;
using TypedRequestContext.Propagation;

public sealed class Handler(
    IRequestContextDeserializer<CustomerRequestContext> deserializer,
    IRequestContextAccessor accessor)
{
    public async Task HandleAsync(IReadOnlyDictionary<string, string> metadata, CancellationToken ct)
    {
        var ctx = deserializer.Deserialize(metadata);

        accessor.Current = ctx;
        try
        {
            // Your handler/service code can now inject CustomerRequestContext
            // or use IRequestContextAccessor.GetRequired<CustomerRequestContext>().
            await Task.CompletedTask;
        }
        finally
        {
            accessor.Current = null;
        }
    }
}
```

The same metadata can include `x-correlation-id` (from `GetCurrentHeaders()`). How you apply correlation in a non-HTTP consumer depends on your hosting model and logging setup.

---

## Customization

### Custom extractor per context type

Why would you need a custom extractor?

- Your context values come from **route values**, **query string**, cookies, or a custom header format.
- You need **custom validation** / normalization (e.g., accept multiple claim names, parse composite identifiers).
- You want a different error behavior (still returning 401/403, but with different rules).
- You want to avoid reflection entirely for a hot path and build the context manually.

```csharp
builder.Services.AddTypedRequestContext<MyContext>(b =>
    b.UseExtractor<MyCustomExtractor>());
```

Your extractor must implement:

```csharp
using TypedRequestContext;

public interface IRequestContextExtractor<out T>
    where T : class, ITypedRequestContext
{
    T Extract(HttpContext httpContext);
}
```

### Custom serializer / deserializer (propagation)

You can override per-context propagation behavior during registration:

```csharp
builder.Services.AddTypedRequestContext<MyContext>(b =>
    b.UseSerializer<MySerializer>()
     .UseDeserializer<MyDeserializer>());
```

---

## Troubleshooting

- **My handler can’t resolve `CustomerRequestContext` from DI**
  - Ensure `AddTypedRequestContext<CustomerRequestContext>()` was called.
  - Ensure the endpoint/group has `.WithRequestContext<CustomerRequestContext>()`.

- **Claims are missing**
  - Ensure `app.UseTypedRequestContext()` runs **after** auth middleware.

- **I get `No extractor registered for context type ...`**
  - You attached `.WithRequestContext<T>()` but didn’t register `AddTypedRequestContext<T>()`.


## Contributing

Issues and PRs are welcome.


## License

MIT. See `LICENSE`.
