using HealthExam.API.Controllers;
using Swashbuckle.AspNetCore.Annotations;
using Xunit;

namespace HealthExam.Tests;

public sealed class SwaggerBusinessDocumentationTests
{
    [Fact]
    public void Main_business_endpoints_have_a_summary_and_description_for_swagger()
    {
        var controllers = new[]
        {
            typeof(CatalogController),
            typeof(ServiceCatalogController),
            typeof(MasterDataController),
            typeof(ExamSessionController),
            typeof(ExamRecordController),
            typeof(ExamImportController),
            typeof(ParaclinicalOrderController),
            typeof(HisFormController),
            typeof(ExamRecordHisFormController),
            typeof(AuthController)
        };

        var missing = controllers
            .SelectMany(controller => controller.GetMethods(
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.DeclaredOnly)
                .Where(IsHttpAction)
                .Select(method => (controller, method,
                    operation: method.GetCustomAttributes(typeof(SwaggerOperationAttribute), false)
                        .Cast<SwaggerOperationAttribute>().SingleOrDefault())))
            .Where(x => string.IsNullOrWhiteSpace(x.operation?.Summary)
                     || string.IsNullOrWhiteSpace(x.operation?.Description))
            .Select(x => $"{x.controller.Name}.{x.method.Name}")
            .ToArray();

        Assert.Empty(missing);
    }

    private static bool IsHttpAction(System.Reflection.MethodInfo method)
        => method.GetCustomAttributes(inherit: true)
            .Any(attribute => attribute.GetType().Name.StartsWith("Http", StringComparison.Ordinal));
}
