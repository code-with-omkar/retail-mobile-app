using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using QuickCommerce.Api.Security;
using QuickCommerce.Infrastructure;
using QuickCommerce.Infrastructure.Security;
using Xunit;

namespace QuickCommerce.Tests;

public sealed class SecurityFoundationTests
{
    [Fact]
    public void Current_user_reads_identity_roles_and_permissions_without_exposing_http_types()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "customer-1"),
                new Claim(ClaimTypes.Role, "Customer"),
                new Claim("permission", "orders:read")
            ], "Test"))
        };
        var currentUser = new HttpContextCurrentUser(new HttpContextAccessor { HttpContext = context });

        Assert.True(currentUser.IsAuthenticated);
        Assert.Equal("customer-1", currentUser.UserId);
        Assert.Contains("Customer", currentUser.Roles);
        Assert.Contains("orders:read", currentUser.Permissions);
    }

    [Fact]
    public void Current_user_is_anonymous_without_an_http_context()
    {
        var currentUser = new HttpContextCurrentUser(new HttpContextAccessor());

        Assert.False(currentUser.IsAuthenticated);
        Assert.Null(currentUser.UserId);
        Assert.Empty(currentUser.Roles);
        Assert.Empty(currentUser.Permissions);
    }

    [Fact]
    public async Task Current_user_context_resolves_organization_and_store_scope()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var organizationId = data.Organizations.Single().Id;
        var user = data.Users.Single();
        var store = data.Stores.First();
        user.StoreId = store.Id;
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.ExternalSubject),
                new Claim("organization_id", organizationId.ToString()),
                new Claim("store_id", store.Id.ToString())
            ], "Test"))
        };

        var resolver = new CurrentUserContextResolver(
            new HttpContextCurrentUser(new HttpContextAccessor { HttpContext = context }),
            data);

        var resolved = await resolver.ResolveAsync();

        Assert.NotNull(resolved);
        Assert.Equal(user.Id, resolved.UserId);
        Assert.Equal(organizationId, resolved.OrganizationId);
        Assert.Equal(store.Id, resolved.StoreId);
    }

    [Fact]
    public async Task Current_user_context_rejects_a_mismatched_organization_claim()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var user = data.Users.Single();
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.ExternalSubject),
                new Claim("organization_id", Guid.NewGuid().ToString())
            ], "Test"))
        };

        var resolver = new CurrentUserContextResolver(
            new HttpContextCurrentUser(new HttpContextAccessor { HttpContext = context }),
            data);

        Assert.Null(await resolver.ResolveAsync());
    }
}