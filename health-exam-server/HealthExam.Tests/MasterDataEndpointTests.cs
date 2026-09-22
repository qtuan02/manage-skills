using System.Net;
using System.Net.Http.Headers;
using HealthExam.Infrastructure.Persistence;
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
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HealthExam.Tests;

public class MasterDataEndpointTests : IClassFixture<AuthTestHost>
{
    private readonly WebApplicationFactory<Program> _factory;

    public MasterDataEndpointTests(AuthTestHost host)
    {
        var databaseName = $"master-data-endpoint-{Guid.NewGuid():N}";
        _factory = host.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptors = services.Where(d =>
                    d.ServiceType == typeof(DbContextOptions<HealthExamDbContext>) ||
                    d.ServiceType == typeof(DbContextOptions) ||
                    d.ServiceType == typeof(HealthExamDbContext)).ToList();
                foreach (var d in descriptors)
                    services.Remove(d);

                services.AddDbContext<HealthExamDbContext>(options =>
                {
                    options.UseInMemoryDatabase(databaseName);
                    options.ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning));
                });
            });
        });
    }

    private HttpClient CreateEmployeeClient(string divisionId = "DEV")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestHeaders.Division, divisionId);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", AuthTestHost.EmployeeToken());
        return client;
    }

    private HttpClient CreateAnonymousClient(string divisionId = "DEV")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestHeaders.Division, divisionId);
        return client;
    }

    [Fact]
    public async Task Anonymous_request_is_rejected()
    {
        var client = CreateAnonymousClient();
        var response = await client.GetAsync("/v1/master-data/registration-options");
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ErrorCodes.Unauthorized, body["ErrorCode"]!.Value<int>());
        Assert.False(string.IsNullOrWhiteSpace(body["TraceID"]!.Value<string>()));
    }

    [Fact]
    public async Task Authenticated_registration_options_returns_ok_envelope()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HealthExamDbContext>();
            db.MasterDataOptions.AddRange(
                new MasterDataOption { OptionID = Guid.NewGuid(), DivisionID = "DEV", Category = "PATIENT_SUBJECT", Code = "01", Name = "Người lớn", IsActive = true, OrderNo = 1 },
                new MasterDataOption { OptionID = Guid.NewGuid(), DivisionID = "DEV", Category = "PATIENT_SUBJECT", Code = "02", Name = "Người cao tuổi", IsActive = true, OrderNo = 2 },
                new MasterDataOption { OptionID = Guid.NewGuid(), DivisionID = "DEV", Category = "PATIENT_SUBJECT", Code = "03", Name = "Trẻ em", IsActive = true, OrderNo = 3 });
            await db.SaveChangesAsync();
            Assert.Equal(3, await db.MasterDataOptions.CountAsync(x => x.DivisionID == "DEV" && x.Category == "PATIENT_SUBJECT"));
        }

        var client = CreateEmployeeClient();
        var response = await client.GetAsync("/v1/master-data/registration-options");
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, body["ErrorCode"]!.Value<int>());
        Assert.NotNull(body["Data"]);
        Assert.NotNull(body["Data"]!["BloodAbos"]);
        Assert.NotNull(body["Data"]!["RegistrationPlaces"]);
        Assert.NotNull(body["Data"]!["PatientTypes"]);
        Assert.Equal(new[] { "01", "02", "03" }, body["Data"]!["PatientSubjects"]!.Select(x => x!["Code"]!.Value<string>()));
        Assert.NotNull(body["Data"]!["ExamRecordStates"]);
    }

    [Fact]
    public async Task Provinces_returns_ok_envelope()
    {
        var client = CreateEmployeeClient();
        var response = await client.GetAsync("/v1/master-data/provinces");
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, body["ErrorCode"]!.Value<int>());
        Assert.NotNull(body["Data"]);
    }

    [Fact]
    public async Task Wards_without_province_returns_400_validation_error()
    {
        var client = CreateEmployeeClient();
        var response = await client.GetAsync("/v1/master-data/wards");
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.BadRequest, body["ErrorCode"]!.Value<int>());
    }
}
