using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace HealthExam.Tests.Architecture;

public class DependencyRuleTests
{
    private static readonly string Root = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../"));

    [Theory]
    [InlineData("HealthExam.Domain/HealthExam.Domain.csproj")]
    [InlineData("HealthExam.Application/HealthExam.Application.csproj")]
    public void Inner_projects_have_no_package_references(string relativePath)
    {
        var xml = File.ReadAllText(Path.Combine(Root, relativePath));
        Assert.DoesNotContain("<PackageReference", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Domain_has_no_project_references()
    {
        var xml = File.ReadAllText(Path.Combine(
            Root, "HealthExam.Domain/HealthExam.Domain.csproj"));
        Assert.DoesNotContain("<ProjectReference", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Application_references_only_Domain()
    {
        var xml = File.ReadAllText(Path.Combine(
            Root, "HealthExam.Application/HealthExam.Application.csproj"));
        Assert.Contains("HealthExam.Domain.csproj", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("HealthExam.Infrastructure.csproj", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("HealthExam.API.csproj", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("HealthExam.Core.csproj", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("HealthExam.Server.csproj", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("HealthExam.Utils.csproj", xml, StringComparison.Ordinal);

        var matches = Regex.Matches(xml, @"<ProjectReference\b");
        Assert.Single(matches);
    }

    [Fact]
    public void Legacy_projects_are_absent_from_solution()
    {
        var solution = File.ReadAllText(Path.Combine(Root, "HealthExamServer.sln"));
        Assert.DoesNotContain("HealthExam.Core", solution, StringComparison.Ordinal);
        Assert.DoesNotContain("HealthExam.Server", solution, StringComparison.Ordinal);
        Assert.DoesNotContain("HealthExam.Utils", solution, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("HealthExam.API/HealthExam.API.csproj")]
    [InlineData("HealthExam.Tests/HealthExam.Tests.csproj")]
    public void Legacy_projects_are_absent_from_api_and_tests(string relativePath)
    {
        var xml = File.ReadAllText(Path.Combine(Root, relativePath));
        Assert.DoesNotContain("HealthExam.Core", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("HealthExam.Server", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("HealthExam.Utils", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Application_interfaces_do_not_expose_iqueryable()
    {
        var appAssembly = typeof(HealthExam.Application.Common.IUnitOfWork).Assembly;
        var interfaceMethods = appAssembly.GetTypes()
            .Where(t => t.IsInterface && t.IsPublic)
            .SelectMany(t => t.GetMethods());

        foreach (var method in interfaceMethods)
        {
            var returnType = method.ReturnType;
            Assert.False(
                IsOrContainsIQueryable(returnType),
                $"Interface method {method.DeclaringType?.Name}.{method.Name} returns or contains IQueryable");

            foreach (var parameter in method.GetParameters())
            {
                Assert.False(
                    IsOrContainsIQueryable(parameter.ParameterType),
                    $"Interface method {method.DeclaringType?.Name}.{method.Name} has IQueryable parameter {parameter.Name}");
            }
        }
    }

    private static bool IsOrContainsIQueryable(Type type)
    {
        if (type == null) return false;
        if (typeof(System.Linq.IQueryable).IsAssignableFrom(type)) return true;
        if (type.IsGenericType)
        {
            return type.GetGenericArguments().Any(IsOrContainsIQueryable);
        }
        return false;
    }

    [Theory]
    [InlineData("HealthExam.Domain")]
    [InlineData("HealthExam.Application")]
    [InlineData("HealthExam.Infrastructure")]
    public void Production_layers_do_not_reference_legacy_projects(string projectFolder)
    {
        var dir = Path.Combine(Root, projectFolder);
        var files = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains("/obj/") && !f.Contains("/bin/"))
            .ToList();

        Assert.NotEmpty(files);

        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            Assert.False(
                source.Contains("HealthExam.Core", StringComparison.Ordinal),
                $"File {file} references legacy HealthExam.Core");
            Assert.False(
                source.Contains("HealthExam.Server", StringComparison.Ordinal),
                $"File {file} references legacy HealthExam.Server");
        }
    }

    /// <summary>
    /// Luồng ký phải sạch bóng SWT của HIS. Còn sót một lời gọi là còn một đường tạo hồ sơ ký
    /// chờ vĩnh viễn trên HIS mà không ai theo dõi.
    /// </summary>
    [Fact]
    public void Khong_con_route_ky_nao_cua_his_server()
    {
        var banned = new[] { "M02F30000/SubmitFile", "M02F30000/GetFileSign", "M02F30000/SignFiles",
                             "M02F01500/SubmitEMR", "M02F01500/SignEMR", "M02F01500/CancleEMR",
                             "M02F01500/RSWByDocTypeID", "api/Util/CertInfo", "api/Sign/ViewFile" };

        var files = Directory.GetFiles(Path.Combine(Root, "HealthExam.Application"), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.Combine(Root, "HealthExam.Infrastructure"), "*.cs", SearchOption.AllDirectories))
            .Concat(Directory.GetFiles(Path.Combine(Root, "HealthExam.API"), "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains("/Migrations/") && !f.Contains("/obj/") && !f.Contains("/bin/"))
            .ToList();

        Assert.NotEmpty(files);

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var route in banned)
                Assert.False(text.Contains(route, StringComparison.Ordinal),
                    $"{Path.GetFileName(file)} còn tham chiếu {route}");
        }
    }

    [Fact]
    public void Api_does_not_reference_infrastructure_outside_program()
    {
        var dir = Path.Combine(Root, "HealthExam.API");
        var files = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains("/obj/") && !f.Contains("/bin/") && !f.EndsWith("Program.cs"))
            .ToList();

        Assert.NotEmpty(files);

        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            Assert.False(
                source.Contains("HealthExam.Infrastructure", StringComparison.Ordinal),
                $"API file {file} references HealthExam.Infrastructure directly");
            Assert.False(
                source.Contains("HealthExam.Core", StringComparison.Ordinal),
                $"API file {file} references legacy HealthExam.Core");
            Assert.False(
                source.Contains("HealthExam.Server", StringComparison.Ordinal),
                $"API file {file} references legacy HealthExam.Server");
        }
    }
}
