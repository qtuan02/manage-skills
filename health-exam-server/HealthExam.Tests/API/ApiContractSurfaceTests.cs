#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using HealthExam.API;
using HealthExam.API.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.Swagger;
using Xunit;

namespace HealthExam.Tests.API;

public class ApiContractSurfaceTests : IClassFixture<AuthTestHost>
{
    private readonly AuthTestHost _factory;

    public ApiContractSurfaceTests(AuthTestHost factory)
    {
        _factory = factory;
    }

    [Fact]
    public void Swagger_document_contains_all_expected_routes_and_methods()
    {
        using var scope = _factory.Services.CreateScope();
        var swaggerProvider = scope.ServiceProvider.GetRequiredService<ISwaggerProvider>();
        var doc = swaggerProvider.GetSwagger("v1");

        Assert.NotNull(doc);
        Assert.NotNull(doc.Paths);
        Assert.NotEmpty(doc.Paths);

        var provider = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Mvc.Infrastructure.IActionDescriptorCollectionProvider>();
        var actionDescriptors = provider.ActionDescriptors.Items
            .OfType<Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor>()
            .Where(x => x.ControllerTypeInfo.Assembly == typeof(Program).Assembly)
            .ToList();

        var controllerTypes = actionDescriptors.Select(x => x.ControllerTypeInfo).Distinct().ToList();
        Assert.Equal(19, controllerTypes.Count);

        var missingEndpoints = new List<string>();
        int totalEndpoints = 0;

        foreach (var action in actionDescriptors)
        {
            var template = action.AttributeRouteInfo?.Template;
            if (string.IsNullOrEmpty(template)) continue;

            var httpMethodMetadata = action.EndpointMetadata.OfType<Microsoft.AspNetCore.Routing.IHttpMethodMetadata>().FirstOrDefault();
            var httpMethods = httpMethodMetadata?.HttpMethods ?? new[] { "GET" };

            var fullRoute = template.StartsWith("/") ? template : "/" + template;
            var normalizedRoute = Regex.Replace(fullRoute, @":[^}]+", "");

            totalEndpoints++;

            if (!doc.Paths.TryGetValue(normalizedRoute, out var pathItem))
            {
                missingEndpoints.Add($"Missing path: {string.Join(",", httpMethods)} {normalizedRoute} ({action.ControllerName}.{action.ActionName})");
                continue;
            }

            foreach (var verb in httpMethods)
            {
                var opType = Enum.Parse<OperationType>(verb, ignoreCase: true);
                if (!pathItem.Operations.ContainsKey(opType))
                {
                    missingEndpoints.Add($"Missing operation: {verb} {normalizedRoute} ({action.ControllerName}.{action.ActionName})");
                }
            }
        }

        Assert.True(totalEndpoints >= 50, $"Expected at least 50 API endpoints, found {totalEndpoints}");
        Assert.Empty(missingEndpoints);

        // Explicit check for registration form routes
        Assert.True(doc.Paths.ContainsKey("/v1/exam-groups/available"));
        Assert.True(doc.Paths.ContainsKey("/v1/exam-groups/{variantCode}/registration-form"));
        Assert.True(doc.Paths.ContainsKey("/v1/exam-records/{recordId}/registration-form"));
        Assert.True(doc.Paths.ContainsKey("/v1/exam-records/{recordId}/registration-form/sections/{sectionKind}"));
        Assert.True(doc.Paths.ContainsKey("/v1/exam-records/{recordId}/registration-form/preview"));

        var previewOp = doc.Paths["/v1/exam-records/{recordId}/registration-form/preview"].Operations[OperationType.Get];
        Assert.NotNull(previewOp);
        Assert.True(previewOp.Responses.ContainsKey("200"));
        Assert.True(previewOp.Responses["200"].Content.ContainsKey("application/pdf"));

        // Explicit check for HIS form section route
        Assert.True(doc.Paths.ContainsKey("/v1/exam-records/{recordId}/his-form/sections/{itemGroupId}"));
        Assert.True(doc.Paths["/v1/exam-records/{recordId}/his-form/sections/{itemGroupId}"].Operations.ContainsKey(OperationType.Get));
        Assert.True(doc.Paths["/v1/exam-records/{recordId}/his-form/sections/{itemGroupId}"].Operations.ContainsKey(OperationType.Put));

        // Explicit check for catalog ICD-10 and departments routes
        Assert.True(doc.Paths.ContainsKey("/v1/catalogs/icd10"));
        Assert.True(doc.Paths["/v1/catalogs/icd10"].Operations.ContainsKey(OperationType.Get));
        Assert.True(doc.Paths.ContainsKey("/v1/departments"));
        Assert.True(doc.Paths["/v1/departments"].Operations.ContainsKey(OperationType.Get));

        // Explicit check for auth routes
        Assert.True(doc.Paths.ContainsKey("/v1/auth/login"));
        Assert.True(doc.Paths["/v1/auth/login"].Operations.ContainsKey(OperationType.Post));
        Assert.True(doc.Paths.ContainsKey("/v1/auth/me"));
        Assert.True(doc.Paths["/v1/auth/me"].Operations.ContainsKey(OperationType.Get));
        Assert.True(doc.Paths.ContainsKey("/v1/auth/logout"));
        Assert.True(doc.Paths["/v1/auth/logout"].Operations.ContainsKey(OperationType.Post));
    }

    [Fact]
    public void Catalog_and_His_contracts_expose_required_properties()
    {
        var icdProps = typeof(HealthExam.API.Contracts.Icd10ChoiceItem).GetProperties().Select(p => p.Name).ToList();
        Assert.Contains("Code", icdProps);
        Assert.Contains("Label", icdProps);
        Assert.Contains("CodeName", icdProps);
        Assert.Contains("DisplayName", icdProps);

        var deptProps = typeof(HealthExam.API.Contracts.DepartmentCatalogItem).GetProperties().Select(p => p.Name).ToList();
        Assert.Contains("DepartmentID", deptProps);
        Assert.Contains("DepartmentCode", deptProps);
        Assert.Contains("DepartmentName", deptProps);
        Assert.Contains("ParentDepartmentID", deptProps);
        Assert.Contains("IsTraditional", deptProps);

        var sectionProps = typeof(HealthExam.API.Contracts.ExamFormSection).GetProperties().Select(p => p.Name).ToList();
        Assert.Contains("RecordId", sectionProps);
        Assert.Contains("AdmissionId", sectionProps);
        Assert.Contains("ItemGroupId", sectionProps);
        Assert.Contains("Layout", sectionProps);
        Assert.Contains("RecordState", sectionProps);
        Assert.Contains("CurrentStepStatus", sectionProps);
        Assert.Contains("SigningProgressDone", sectionProps);
        Assert.Contains("SigningProgressTotal", sectionProps);
        Assert.Contains("CurrentStepSignedByEmployeeName", sectionProps);
        Assert.Contains("Signers", sectionProps);
        var signerProps = typeof(HealthExam.API.Contracts.SignerChoice).GetProperties().Select(p => p.Name).ToList();
        Assert.Equal(new[] { "EmployeeID", "EmployeeCode", "EmployeeName" }, signerProps);

        var signReqProps = typeof(HealthExam.API.Contracts.ExamSectionSignRequest).GetProperties().Select(p => p.Name).ToList();
        Assert.Contains("ConfirmedByEmployeeID", signReqProps);
        Assert.Contains("SignedAt", signReqProps);
        var signResProps = typeof(HealthExam.Application.Signing.ExamSectionSignResult).GetProperties().Select(p => p.Name).ToList();
        Assert.Contains("SignedByEmployeeName", signResProps);
        Assert.Contains("PerformedByEmployeeID", signResProps);
        Assert.Contains("PerformedByEmployeeName", signResProps);

        // Spec §6.4: POST conclusion/sign có body RỖNG — không có DTO body, không tham số [FromBody].
        // Có [FromBody] là [ApiController] trả 415 cho request không Content-Type trước khi vào handler.
        var signAction = typeof(HealthExam.API.Controllers.ExamRecordController)
            .GetMethod(nameof(HealthExam.API.Controllers.ExamRecordController.SignConclusion))!;
        Assert.DoesNotContain(signAction.GetParameters(), p =>
            p.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.FromBodyAttribute), inherit: true).Length > 0);
        Assert.Null(typeof(HealthExam.API.Contracts.ExamRecordItem).Assembly.GetType("HealthExam.API.Contracts.ConclusionSignRequest"));
    }
}
