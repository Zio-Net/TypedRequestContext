using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TypedRequestContext.Infrastructure;

namespace TypedRequestContext.UnitTests;

public class RequestContextAccessorTests
{
    [Fact]
    public void GetRequired_ReturnsCurrentContext_WhenTypeMatches()
    {
        var accessor = new RequestContextAccessor();
        var context = new AccessorContext
        {
            UserId = Guid.NewGuid(),
            TenantId = Guid.NewGuid()
        };

        accessor.Current = context;

        var resolved = accessor.GetRequired<AccessorContext>();

        Assert.Same(context, resolved);
    }

    [Fact]
    public void GetRequired_Throws_WhenContextMissing()
    {
        var accessor = new RequestContextAccessor();

        var action = () => accessor.GetRequired<AccessorContext>();

        Assert.Throws<InvalidOperationException>(action);
    }

    public sealed class AccessorContext : ITypedRequestContext
    {
        [FromClaim("sub"), RequiredContextValue]
        public Guid UserId { get; init; }

        [FromHeader("x-tenant-id"), RequiredContextValue]
        public Guid TenantId { get; init; }
    }
}

public class AttributeBasedRequestContextExtractorTests
{
    [Fact]
    public void Extract_MapsClaimAndHeaderValues_WhenPresent()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var http = BuildHttpContext(
            new Dictionary<string, string> { ["sub"] = userId.ToString() },
            new Dictionary<string, string> { ["x-tenant-id"] = tenantId.ToString(), ["x-role"] = "Manager" });

        var extractor = new AttributeBasedRequestContextExtractor<TestRequestContext>();

        var context = extractor.Extract(http);

        Assert.Equal(userId, context.UserId);
        Assert.Equal(tenantId, context.TenantId);
        Assert.Equal(Role.Manager, context.Role);
    }

    [Fact]
    public void Extract_Throws401_WhenRequiredClaimIsMissing()
    {
        var tenantId = Guid.NewGuid();
        var http = BuildHttpContext(
            claims: null,
            headers: new Dictionary<string, string> { ["x-tenant-id"] = tenantId.ToString() });

        var extractor = new AttributeBasedRequestContextExtractor<TestRequestContext>();

        var ex = Assert.Throws<RequestContextCreationException>(() => extractor.Extract(http));

        Assert.Equal(401, ex.StatusCode);
        Assert.Contains("UserId", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Extract_Throws403_WhenRequiredHeaderIsMissing()
    {
        var userId = Guid.NewGuid();
        var http = BuildHttpContext(
            claims: new Dictionary<string, string> { ["sub"] = userId.ToString() },
            headers: null);

        var extractor = new AttributeBasedRequestContextExtractor<TestRequestContext>();

        var ex = Assert.Throws<RequestContextCreationException>(() => extractor.Extract(http));

        Assert.Equal(403, ex.StatusCode);
        Assert.Contains("TenantId", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Extract_Throws401_WhenRequiredClaimHasInvalidFormat()
    {
        var tenantId = Guid.NewGuid();
        var http = BuildHttpContext(
            claims: new Dictionary<string, string> { ["sub"] = "not-a-guid" },
            headers: new Dictionary<string, string> { ["x-tenant-id"] = tenantId.ToString() });

        var extractor = new AttributeBasedRequestContextExtractor<TestRequestContext>();

        var ex = Assert.Throws<RequestContextCreationException>(() => extractor.Extract(http));

        Assert.Equal(401, ex.StatusCode);
        Assert.Contains("UserId", ex.Message, StringComparison.Ordinal);
    }

    private static DefaultHttpContext BuildHttpContext(
        IReadOnlyDictionary<string, string>? claims,
        IReadOnlyDictionary<string, string>? headers)
    {
        var context = new DefaultHttpContext();

        if (headers is not null)
        {
            foreach (var (key, value) in headers)
                context.Request.Headers[key] = value;
        }

        if (claims is not null)
        {
            var claimList = claims.Select(kvp => new Claim(kvp.Key, kvp.Value));
            context.User = new ClaimsPrincipal(new ClaimsIdentity(claimList, authenticationType: "test"));
        }

        return context;
    }

    public enum Role
    {
        User,
        Manager,
        Admin
    }

    public sealed class TestRequestContext : ITypedRequestContext
    {
        [FromClaim("sub"), RequiredContextValue]
        public Guid UserId { get; init; }

        [FromHeader("x-tenant-id"), RequiredContextValue]
        public Guid TenantId { get; init; }

        [FromHeader("x-role")]
        public Role? Role { get; init; }
    }
}

public class RequestContextRegistrationTests
{
    [Fact]
    public void AddTypedRequestContext_UseExtractor_RegistersCustomExtractor()
    {
        var services = new ServiceCollection();
        services.AddTypedRequestContext();
        services.AddTypedRequestContext<CustomContext>(b => b.UseExtractor<CustomContextExtractor>());

        var provider = services.BuildServiceProvider();

        var extractor = provider.GetRequiredService<IRequestContextExtractor<CustomContext>>();

        Assert.IsType<CustomContextExtractor>(extractor);
    }

    [Fact]
    public void AddTypedRequestContext_UseSerializerAndDeserializer_StoresTypesInOptions()
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddTypedRequestContext();
        services.AddTypedRequestContext<CustomContext>(b =>
            b.UseSerializer(typeof(CustomSerializer))
             .UseDeserializer(typeof(CustomDeserializer)));

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<RequestContextOptions>>().Value;

        Assert.Equal(typeof(CustomSerializer), options.SerializerTypes[typeof(CustomContext)]);
        Assert.Equal(typeof(CustomDeserializer), options.DeserializerTypes[typeof(CustomContext)]);
    }

    public sealed class CustomContext : ITypedRequestContext
    {
        [FromHeader("x-custom")]
        public string? Value { get; init; }
    }

    public sealed class CustomContextExtractor : IRequestContextExtractor<CustomContext>
    {
        public CustomContext Extract(HttpContext httpContext)
            => new() { Value = httpContext.Request.Headers["x-custom"].FirstOrDefault() };
    }

    public sealed class CustomSerializer;

    public sealed class CustomDeserializer;
}
