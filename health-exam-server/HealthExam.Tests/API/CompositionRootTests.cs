#nullable enable

using System;
using System.Linq;
using HealthExam.API;
using HealthExam.Application.Catalogs;
using HealthExam.Application.Common;
using HealthExam.Application.ExamRecords;
using HealthExam.Application.ExamSessions;
using HealthExam.Application.His;
using HealthExam.Application.Imports;
using HealthExam.Application.Integrations;
using HealthExam.Application.Paraclinical;
using HealthExam.Application.Webhooks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HealthExam.Tests.API;

public class CompositionRootTests : IClassFixture<AuthTestHost>
{
    private readonly AuthTestHost _factory;

    public CompositionRootTests(AuthTestHost factory)
    {
        _factory = factory;
    }

    [Fact]
    public void Api_resolves_every_controller()
    {
        using var scope = _factory.Services.CreateScope();
        var controllers = typeof(Program).Assembly.GetTypes()
            .Where(x => !x.IsAbstract && typeof(ControllerBase).IsAssignableFrom(x))
            .ToList();

        Assert.NotEmpty(controllers);
        foreach (var controller in controllers)
        {
            var instance = ActivatorUtilities.GetServiceOrCreateInstance(scope.ServiceProvider, controller);
            Assert.NotNull(instance);
        }
    }

    [Fact]
    public void Di_resolves_all_application_handlers()
    {
        using var scope = _factory.Services.CreateScope();
        var handlerInterfaces = typeof(IListOrganizationsHandler).Assembly.GetTypes()
            .Where(x => x.IsInterface && x.Name.EndsWith("Handler"))
            .ToList();

        Assert.True(handlerInterfaces.Count >= 50, $"Expected at least 50 application handlers, found {handlerInterfaces.Count}");
        foreach (var handlerType in handlerInterfaces)
        {
            var instance = scope.ServiceProvider.GetService(handlerType);
            Assert.NotNull(instance);
        }
    }

    [Theory]
    [InlineData(typeof(HealthExam.Application.Common.IUnitOfWork))]
    [InlineData(typeof(HealthExam.Application.Common.IAuditRepository))]
    [InlineData(typeof(HealthExam.Application.Catalogs.ICatalogRepository))]
    [InlineData(typeof(HealthExam.Application.ExamSessions.IExamSessionRepository))]
    [InlineData(typeof(HealthExam.Application.ExamRecords.IExamRecordRepository))]
    [InlineData(typeof(HealthExam.Application.Imports.IImportRepository))]
    [InlineData(typeof(HealthExam.Application.Paraclinical.IParaclinicalRepository))]
    [InlineData(typeof(HealthExam.Application.Paraclinical.IIntegrationOutboxRepository))]
    [InlineData(typeof(HealthExam.Application.Webhooks.IWebhookInboxRepository))]
    [InlineData(typeof(HealthExam.Application.ExamRecords.IRecordCodeAllocator))]
    [InlineData(typeof(HealthExam.Application.Paraclinical.IOrderNoAllocator))]
    [InlineData(typeof(HealthExam.Application.Paraclinical.IVendorLineNoAllocator))]
    [InlineData(typeof(HealthExam.Application.Imports.IExamWorkbookReader))]
    [InlineData(typeof(HealthExam.Application.Integrations.IHisFormDefinitionCache))]
    [InlineData(typeof(HealthExam.Application.Integrations.IHisEmrClient))]
    [InlineData(typeof(HealthExam.Application.Integrations.IRisClient))]
    [InlineData(typeof(HealthExam.Application.Integrations.IRisPayloadBuilder))]
    [InlineData(typeof(HealthExam.Application.Integrations.IFormServerClient))]
    [InlineData(typeof(HealthExam.Application.Paraclinical.IFormServerClient))]
    [InlineData(typeof(HealthExam.Application.Webhooks.IWebhookMetricsTracker))]
    [InlineData(typeof(HealthExam.Application.Paraclinical.IParaclinicalOutboxDispatcher))]
    [InlineData(typeof(HealthExam.Application.Common.IClock))]
    [InlineData(typeof(HealthExam.Application.His.IHisProcessValidator))]
    [InlineData(typeof(HealthExam.Application.RegistrationForms.IRegistrationFormRepository))]
    public void Di_resolves_core_infrastructure_and_application_dependencies(Type serviceType)
    {
        using var scope = _factory.Services.CreateScope();
        var instance = scope.ServiceProvider.GetService(serviceType);
        Assert.NotNull(instance);
    }
}
