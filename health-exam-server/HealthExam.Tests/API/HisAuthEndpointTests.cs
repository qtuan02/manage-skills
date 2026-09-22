#nullable enable

using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HealthExam.API.Contracts;
using HealthExam.API.Controllers;
using HealthExam.Application.Auth;
using HealthExam.Infrastructure.Integrations.HisEmr;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HealthExam.Tests.API;

public class HisAuthEndpointTests : IClassFixture<AuthTestHost>
{
    private readonly AuthTestHost _host;

    public HisAuthEndpointTests(AuthTestHost host)
    {
        _host = host;
    }

    [Fact]
    public void Controller_defines_the_three_exact_auth_routes()
    {
        var type = typeof(AuthController);
        var route = type.GetCustomAttributes<RouteAttribute>(inherit: false).FirstOrDefault()?.Template
                    ?? type.GetCustomAttribute<RouteAttribute>()?.Template;
        Assert.Equal("v1/auth", route);
        Assert.Equal("login", type.GetMethod("Login")!.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("me", type.GetMethod("Me")!.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal(
            typeof(Task<ActionResult<ResultData<HisCurrentUser>>>),
            type.GetMethod("Me")!.ReturnType);
        Assert.Equal("logout", type.GetMethod("Logout")!.GetCustomAttribute<HttpPostAttribute>()!.Template);
    }

    [Fact]
    public async Task DI_resolves_the_HTTP_gateway_through_the_interface()
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var gateway = scope.ServiceProvider.GetRequiredService<IHisAuthGateway>();
        Assert.IsType<HisHttpAuthGateway>(gateway);

        var loginHandler = scope.ServiceProvider.GetRequiredService<ILoginHandler>();
        Assert.NotNull(loginHandler);
        var meHandler = scope.ServiceProvider.GetRequiredService<IGetCurrentUserHandler>();
        Assert.NotNull(meHandler);
        var logoutHandler = scope.ServiceProvider.GetRequiredService<ILogoutHandler>();
        Assert.NotNull(logoutHandler);
    }
}
