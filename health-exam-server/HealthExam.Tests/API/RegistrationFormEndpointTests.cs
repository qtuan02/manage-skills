using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.API.Contracts;
using HealthExam.Application.Common;
using HealthExam.Application.RegistrationForms;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace HealthExam.Tests.API;

public class RegistrationFormEndpointTests : IClassFixture<AuthTestHost>
{
    private readonly AuthTestHost _host;

    public RegistrationFormEndpointTests(AuthTestHost host)
    {
        _host = host;
    }

    [Fact]
    public async Task Get_preview_returns_pdf_stream()
    {
        var recordId = Guid.NewGuid();
        var pdfBytes = Encoding.ASCII.GetBytes("%PDF-1.7 mock preview data");

        var fakeHandler = new FakePreviewHandler(ApplicationResult<byte[]>.Success(pdfBytes));

        await using var factory = _host.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.Replace(ServiceDescriptor.Singleton<IPreviewRegistrationFormPdfHandler>(fakeHandler))));

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Division-Id", "DEV");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", AuthTestHost.EmployeeToken());

        using var response = await client.GetAsync($"/v1/exam-records/{recordId}/registration-form/preview");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(pdfBytes, bytes);
    }

    [Fact]
    public async Task Get_available_returns_ok()
    {
        var groups = new List<AvailableExamGroupItem>
        {
            new() { VariantCode = "DTK_03", Name = "Nguoi tren 18 tuoi", OrderNo = 1 }
        };
        var fakeHandler = new FakeListAvailableHandler(ApplicationResult<IReadOnlyList<AvailableExamGroupItem>>.Success(groups));

        await using var factory = _host.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.Replace(ServiceDescriptor.Singleton<IListAvailableExamGroupsHandler>(fakeHandler))));

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Division-Id", "DEV");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", AuthTestHost.EmployeeToken());

        using var response = await client.GetAsync("/v1/exam-groups/available");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ResultData<List<AvailableExamGroupItem>>>();
        Assert.NotNull(body);
        Assert.Equal(0, body.ErrorCode);
        Assert.NotEmpty(body.Data);
    }

    [Fact]
    public async Task Get_group_registration_form_returns_ok()
    {
        var def = new RegistrationFormDefinition
        {
            VariantCode = "DTK_03",
            TemplateCode = "KSK-TREN18TUOI",
            TemplateName = "KSK tren 18"
        };
        var fakeHandler = new FakeGetGroupFormHandler(ApplicationResult<RegistrationFormDefinition>.Success(def));

        await using var factory = _host.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.Replace(ServiceDescriptor.Singleton<IGetExamGroupRegistrationFormsHandler>(fakeHandler))));

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Division-Id", "DEV");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", AuthTestHost.EmployeeToken());

        using var response = await client.GetAsync("/v1/exam-groups/DTK_03/registration-form");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ResultData<RegistrationFormDefinition>>();
        Assert.NotNull(body);
        Assert.Equal("DTK_03", body.Data.VariantCode);
    }

    [Fact]
    public async Task Get_record_form_returns_ok()
    {
        var recordId = Guid.NewGuid();
        var form = new RegistrationRecordForm
        {
            RecordID = recordId,
            VariantCode = "DTK_03"
        };
        var fakeHandler = new FakeGetRecordFormHandler(ApplicationResult<RegistrationRecordForm>.Success(form));

        await using var factory = _host.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.Replace(ServiceDescriptor.Singleton<IGetRegistrationFormHandler>(fakeHandler))));

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Division-Id", "DEV");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", AuthTestHost.EmployeeToken());

        using var response = await client.GetAsync($"/v1/exam-records/{recordId}/registration-form");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ResultData<RegistrationRecordForm>>();
        Assert.NotNull(body);
        Assert.Equal(recordId, body.Data.RecordID);
    }

    [Fact]
    public async Task Save_section_returns_ok()
    {
        var recordId = Guid.NewGuid();
        var form = new RegistrationRecordForm
        {
            RecordID = recordId,
            VariantCode = "DTK_03"
        };
        var fakeHandler = new FakeSaveSectionHandler(ApplicationResult<RegistrationRecordForm>.Success(form));

        await using var factory = _host.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.Replace(ServiceDescriptor.Singleton<ISaveRegistrationFormSectionHandler>(fakeHandler))));

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Division-Id", "DEV");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", AuthTestHost.EmployeeToken());

        var req = new RegistrationSectionSaveRequest(new List<RegistrationFormFieldValue>(), isDraft: true);
        using var response = await client.PutAsJsonAsync($"/v1/exam-records/{recordId}/registration-form/sections/HISTORY", req);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ResultData<RegistrationRecordForm>>();
        Assert.NotNull(body);
        Assert.Equal(recordId, body.Data.RecordID);
    }

    private sealed class FakePreviewHandler : IPreviewRegistrationFormPdfHandler
    {
        private readonly ApplicationResult<byte[]> _result;
        public FakePreviewHandler(ApplicationResult<byte[]> result) => _result = result;
        public Task<ApplicationResult<byte[]>> HandleAsync(PreviewRegistrationFormPdfQuery query, CancellationToken ct = default)
            => Task.FromResult(_result);
    }

    private sealed class FakeListAvailableHandler : IListAvailableExamGroupsHandler
    {
        private readonly ApplicationResult<IReadOnlyList<AvailableExamGroupItem>> _result;
        public FakeListAvailableHandler(ApplicationResult<IReadOnlyList<AvailableExamGroupItem>> result) => _result = result;
        public Task<ApplicationResult<IReadOnlyList<AvailableExamGroupItem>>> HandleAsync(ListAvailableExamGroupsQuery query, CancellationToken ct = default)
            => Task.FromResult(_result);
    }

    private sealed class FakeGetGroupFormHandler : IGetExamGroupRegistrationFormsHandler
    {
        private readonly ApplicationResult<RegistrationFormDefinition> _result;
        public FakeGetGroupFormHandler(ApplicationResult<RegistrationFormDefinition> result) => _result = result;
        public Task<ApplicationResult<RegistrationFormDefinition>> HandleAsync(GetExamGroupRegistrationFormsQuery query, CancellationToken ct = default)
            => Task.FromResult(_result);
    }

    private sealed class FakeGetRecordFormHandler : IGetRegistrationFormHandler
    {
        private readonly ApplicationResult<RegistrationRecordForm> _result;
        public FakeGetRecordFormHandler(ApplicationResult<RegistrationRecordForm> result) => _result = result;
        public Task<ApplicationResult<RegistrationRecordForm>> HandleAsync(GetRegistrationFormQuery query, CancellationToken ct = default)
            => Task.FromResult(_result);
    }

    private sealed class FakeSaveSectionHandler : ISaveRegistrationFormSectionHandler
    {
        private readonly ApplicationResult<RegistrationRecordForm> _result;
        public FakeSaveSectionHandler(ApplicationResult<RegistrationRecordForm> result) => _result = result;
        public Task<ApplicationResult<RegistrationRecordForm>> HandleAsync(SaveRegistrationFormSectionCommand command, CancellationToken ct = default)
            => Task.FromResult(_result);
    }
}
