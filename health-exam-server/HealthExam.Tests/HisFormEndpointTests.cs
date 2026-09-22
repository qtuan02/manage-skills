#nullable enable

using System;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using HealthExam.API.Controllers;
using HealthExam.API.Contracts;
using HealthExam.API.Middlewares;
using HealthExam.Application.Common;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamSessions;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.Imports;
using HealthExam.Domain.Paraclinical;
using HealthExam.Domain.Webhooks;
using HealthExam.Domain.Integrations;
using HealthExam.Domain.Catalogs;
using HealthExam.Server.Service;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HealthExam.Tests;

public class HisFormEndpointTests : IClassFixture<AuthTestHost>
{
    private readonly AuthTestHost _host;

    public HisFormEndpointTests(AuthTestHost host)
    {
        _host = host;
    }

    [Fact]
    public void HisFormController_defines_exact_route_and_return_type()
    {
        var method = typeof(HisFormController).GetMethod(nameof(HisFormController.Get));
        Assert.NotNull(method);

        var routeAttr = typeof(HisFormController).GetCustomAttributes<RouteAttribute>(inherit: false).FirstOrDefault();
        Assert.Equal("v1/his-forms", routeAttr?.Template);

        var httpGet = method.GetCustomAttribute<HttpGetAttribute>();
        Assert.Equal("{templateCode}", httpGet?.Template);

        Assert.True(typeof(Task).IsAssignableFrom(method.ReturnType));
        var returnType = method.ReturnType.GenericTypeArguments[0];
        Assert.Equal(typeof(ActionResult<>), returnType.GetGenericTypeDefinition());
        var resultDataType = returnType.GenericTypeArguments[0];
        Assert.Equal(typeof(ResultData<>), resultDataType.GetGenericTypeDefinition());
    }

    [Fact]
    public void ExamRecordHisFormController_defines_all_exact_routes()
    {
        var controllerType = typeof(ExamRecordHisFormController);
        var baseRoute = controllerType.GetCustomAttributes<RouteAttribute>(inherit: false).FirstOrDefault()?.Template;
        Assert.Equal("v1/exam-records/{recordId:guid}/his-form", baseRoute);

        var methods = controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        // Verify each action returns ResultData<T>
        foreach (var m in methods)
        {
            Assert.True(typeof(Task).IsAssignableFrom(m.ReturnType), $"{m.Name} should return Task");
            var taskType = m.ReturnType.GenericTypeArguments[0];
            Assert.Equal(typeof(ActionResult<>), taskType.GetGenericTypeDefinition());
            var resultDataType = taskType.GenericTypeArguments[0];
            Assert.Equal(typeof(ResultData<>), resultDataType.GetGenericTypeDefinition());
        }

        // Verify route attributes
        var getForm = methods.Single(m => m.Name == nameof(ExamRecordHisFormController.GetForm));
        Assert.Null(getForm.GetCustomAttribute<HttpGetAttribute>()?.Template);

        var getProcesses = methods.Single(m => m.Name == nameof(ExamRecordHisFormController.GetProcesses));
        Assert.Equal("processes", getProcesses.GetCustomAttribute<HttpGetAttribute>()?.Template);

        var getProcess = methods.Single(m => m.Name == nameof(ExamRecordHisFormController.GetProcess));
        Assert.Equal("processes/{processId:guid}", getProcess.GetCustomAttribute<HttpGetAttribute>()?.Template);
        Assert.Equal(typeof(Guid), getProcess.GetParameters().Single(p => p.Name == "processId").ParameterType);

        var saveSection = methods.Single(m => m.Name == nameof(ExamRecordHisFormController.SaveSection));
        Assert.Equal("sections/{itemGroupId:int}", saveSection.GetCustomAttribute<HttpPutAttribute>()?.Template);
        Assert.Equal(typeof(int), saveSection.GetParameters().Single(p => p.Name == "itemGroupId").ParameterType);
    }

    [Fact]
    public void Forbidden_patterns_do_not_exist_on_controllers()
    {
        var controllers = new[] { typeof(HisFormController), typeof(ExamRecordHisFormController) };
        foreach (var c in controllers)
        {
            var methods = c.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            foreach (var m in methods)
            {
                var name = m.Name.ToUpperInvariant();
                Assert.DoesNotContain("CUEMR", name);
                Assert.DoesNotContain("BATCH", name);
                Assert.DoesNotContain("SAVEVALUE", name);

                var httpAttr = m.GetCustomAttribute<HttpMethodAttribute>();
                var template = (httpAttr?.Template ?? "").ToUpperInvariant();
                Assert.DoesNotContain("CUEMR", template);
                Assert.DoesNotContain("M03F10010", template);
                Assert.DoesNotContain("BATCH", template);
            }
        }
    }

    [Fact]
    public async Task Dependency_injection_resolves_all_his_services()
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var options = sp.GetRequiredService<HealthExam.Infrastructure.Integrations.HisEmr.HisEmrOptions>();
        Assert.NotNull(options);

        var cache = sp.GetRequiredService<IMemoryCache>();
        Assert.NotNull(cache);

        var formDefCache = sp.GetRequiredService<HealthExam.Application.Integrations.IHisFormDefinitionCache>();
        Assert.NotNull(formDefCache);

        var hisClient = sp.GetRequiredService<HealthExam.Application.Integrations.IHisEmrClient>();
        Assert.NotNull(hisClient);

        var client = sp.GetRequiredService<HealthExam.Infrastructure.Integrations.HisEmr.HisEmrClient>();
        Assert.NotNull(client);

        var defHandler = sp.GetRequiredService<HealthExam.Application.His.IGetHisFormDefinitionHandler>();
        Assert.NotNull(defHandler);

        var examFormHandler = sp.GetRequiredService<HealthExam.Application.His.IGetExamRecordHisFormHandler>();
        Assert.NotNull(examFormHandler);

        var listProcessesHandler = sp.GetRequiredService<HealthExam.Application.His.IListHisProcessesHandler>();
        Assert.NotNull(listProcessesHandler);

        var getProcessHandler = sp.GetRequiredService<HealthExam.Application.His.IGetHisProcessHandler>();
        Assert.NotNull(getProcessHandler);

        var icd10Handler = sp.GetRequiredService<HealthExam.Application.His.IGetIcd10ChoicesHandler>();
        Assert.NotNull(icd10Handler);

        var departmentsHandler = sp.GetRequiredService<HealthExam.Application.His.IListEmployeeDepartmentsHandler>();
        Assert.NotNull(departmentsHandler);

        var saveHisSectionHandler = sp.GetRequiredService<HealthExam.Application.His.ISaveHisFormSectionHandler>();
        Assert.NotNull(saveHisSectionHandler);

        var recordController = ActivatorUtilities.CreateInstance<ExamRecordController>(sp);
        Assert.NotNull(recordController);

        var factory = sp.GetRequiredService<IHttpClientFactory>();
        var httpClient = factory.CreateClient(typeof(HealthExam.Infrastructure.Integrations.HisEmr.HisEmrClient).Name);
        Assert.Equal(options.Timeout, httpClient.Timeout);
    }
}
