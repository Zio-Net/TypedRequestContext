using Microsoft.Extensions.DependencyInjection;
using TypedRequestContext.Propagation.Infrastructure;

namespace TypedRequestContext.Propagation.UnitTests;

public class AttributeBasedRequestContextSerializerTests
{
    [Fact]
    public void Serialize_EmitsOnlyPropagationKeys_AndOmitsNullValues()
    {
        var serializer = new AttributeBasedRequestContextSerializer<TestPropagationContext>();
        var context = new TestPropagationContext
        {
            UserId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            TenantId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Role = null,
            LocalOnly = "local"
        };

        var headers = serializer.Serialize(context);

        Assert.Equal("11111111-1111-1111-1111-111111111111", headers["x-user-id"]);
        Assert.Equal("22222222-2222-2222-2222-222222222222", headers["x-tenant-id"]);
        Assert.DoesNotContain("x-role", headers.Keys);
        Assert.DoesNotContain("LocalOnly", headers.Keys);
    }
}

public class AttributeBasedRequestContextDeserializerTests
{
    [Fact]
    public void Deserialize_MapsValidMetadata()
    {
        var deserializer = new AttributeBasedRequestContextDeserializer<TestPropagationContext>();
        var metadata = new Dictionary<string, string>
        {
            ["x-user-id"] = "11111111-1111-1111-1111-111111111111",
            ["x-tenant-id"] = "22222222-2222-2222-2222-222222222222",
            ["x-role"] = "admin"
        };

        var context = deserializer.Deserialize(metadata);

        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), context.UserId);
        Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), context.TenantId);
        Assert.Equal("admin", context.Role);
    }

    [Fact]
    public void Deserialize_Throws_WhenRequiredMetadataIsMissing()
    {
        var deserializer = new AttributeBasedRequestContextDeserializer<TestPropagationContext>();
        var metadata = new Dictionary<string, string>
        {
            ["x-user-id"] = "11111111-1111-1111-1111-111111111111"
        };

        var ex = Assert.Throws<RequestContextDeserializationException>(() => deserializer.Deserialize(metadata));

        Assert.Contains("TenantId", ex.Message, StringComparison.Ordinal);
    }
}

public class RequestContextPropagatorTests
{
    [Fact]
    public void Propagate_SetsContextOnAccessor()
    {
        var services = new ServiceCollection();
        services.AddTypedRequestContext();
        services.AddTypedRequestContextPropagation();
        services.AddTypedRequestContext<TestPropagationContext>();

        var provider = services.BuildServiceProvider();
        var accessor = provider.GetRequiredService<IRequestContextAccessor>();
        var propagator = provider.GetRequiredService<IRequestContextPropagator<TestPropagationContext>>();

        var metadata = new Dictionary<string, string>
        {
            ["x-user-id"] = "11111111-1111-1111-1111-111111111111",
            ["x-tenant-id"] = "22222222-2222-2222-2222-222222222222"
        };

        using (propagator.Propagate(metadata))
        {
            var ctx = accessor.GetRequired<TestPropagationContext>();
            Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), ctx.UserId);
            Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), ctx.TenantId);
        }
    }

    [Fact]
    public void Propagate_ClearsContextFromAccessorAfterDispose()
    {
        var services = new ServiceCollection();
        services.AddTypedRequestContext();
        services.AddTypedRequestContextPropagation();
        services.AddTypedRequestContext<TestPropagationContext>();

        var provider = services.BuildServiceProvider();
        var accessor = provider.GetRequiredService<IRequestContextAccessor>();
        var propagator = provider.GetRequiredService<IRequestContextPropagator<TestPropagationContext>>();

        var metadata = new Dictionary<string, string>
        {
            ["x-user-id"] = "11111111-1111-1111-1111-111111111111",
            ["x-tenant-id"] = "22222222-2222-2222-2222-222222222222"
        };

        using (propagator.Propagate(metadata))
        {
            Assert.NotNull(accessor.Current);
        }

        Assert.Null(accessor.Current);
    }

    [Fact]
    public void Propagate_ClearsContextEvenWhenHandlerThrows()
    {
        var services = new ServiceCollection();
        services.AddTypedRequestContext();
        services.AddTypedRequestContextPropagation();
        services.AddTypedRequestContext<TestPropagationContext>();

        var provider = services.BuildServiceProvider();
        var accessor = provider.GetRequiredService<IRequestContextAccessor>();
        var propagator = provider.GetRequiredService<IRequestContextPropagator<TestPropagationContext>>();

        var metadata = new Dictionary<string, string>
        {
            ["x-user-id"] = "11111111-1111-1111-1111-111111111111",
            ["x-tenant-id"] = "22222222-2222-2222-2222-222222222222"
        };

        try
        {
            using (propagator.Propagate(metadata))
            {
                throw new InvalidOperationException("handler failed");
            }
        }
        catch (InvalidOperationException) { }

        Assert.Null(accessor.Current);
    }

    [Fact]
    public void Propagate_Throws_WhenRequiredMetadataIsMissing()
    {
        var services = new ServiceCollection();
        services.AddTypedRequestContext();
        services.AddTypedRequestContextPropagation();
        services.AddTypedRequestContext<TestPropagationContext>();

        var provider = services.BuildServiceProvider();
        var propagator = provider.GetRequiredService<IRequestContextPropagator<TestPropagationContext>>();

        var metadata = new Dictionary<string, string>
        {
            ["x-user-id"] = "11111111-1111-1111-1111-111111111111"
            // x-tenant-id missing — required
        };

        Assert.Throws<RequestContextDeserializationException>(() => propagator.Propagate(metadata));
    }
}

public class PropagationHeadersProviderTests
{
    [Fact]
    public void GetCurrentHeaders_IncludesCorrelationAndSerializedContext()
    {
        var services = new ServiceCollection();
        services.AddTypedRequestContext();
        services.AddTypedRequestContextPropagation();
        services.AddCorrelationId();
        services.AddTypedRequestContext<TestPropagationContext>();

        var provider = services.BuildServiceProvider();
        var accessor = provider.GetRequiredService<IRequestContextAccessor>();
        accessor.Current = new TestPropagationContext
        {
            UserId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            TenantId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Role = "ops"
        };

        var headersProvider = provider.GetRequiredService<IPropagationHeadersProvider>();

        var headers = headersProvider.GetCurrentHeaders();

        Assert.Equal("11111111-1111-1111-1111-111111111111", headers["x-user-id"]);
        Assert.Equal("22222222-2222-2222-2222-222222222222", headers["x-tenant-id"]);
        Assert.Equal("ops", headers["x-role"]);
        Assert.Equal("unknown", headers["x-correlation-id"]);
    }
}

public sealed class TestPropagationContext : ITypedRequestContext
{
    [FromClaim("sub"), RequiredContextValue, PropagationKey("x-user-id")]
    public Guid UserId { get; init; }

    [FromHeader("x-tenant-id"), RequiredContextValue, PropagationKey("x-tenant-id")]
    public Guid TenantId { get; init; }

    [FromHeader("x-role"), PropagationKey("x-role")]
    public string? Role { get; init; }

    [FromHeader("x-local")]
    public string? LocalOnly { get; init; }
}
