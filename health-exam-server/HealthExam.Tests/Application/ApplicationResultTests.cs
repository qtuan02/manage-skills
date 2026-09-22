using System.Collections.Generic;
using HealthExam.Application.Common;
using HealthExam.Domain.Common;
using Xunit;

namespace HealthExam.Tests.Application;

public class ApplicationResultTests
{
    [Fact]
    public void Failure_carries_semantic_code_without_numeric_api_code()
    {
        var result = ApplicationResult<string>.Fail(
            ApplicationFailureCode.InvalidState, "Đợt khám đã đóng");

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.InvalidState, result.Failure.Code);
        Assert.Equal("Đợt khám đã đóng", result.Failure.Message);
    }

    [Fact]
    public void Success_carries_value()
    {
        var result = ApplicationResult<string>.Success("ok");

        Assert.True(result.IsSuccess);
        Assert.Equal("ok", result.Value);
        Assert.Null(result.Failure);
    }

    [Fact]
    public void Failure_with_payload()
    {
        var payload = new { Detail = "test" };
        var result = ApplicationResult<int>.Fail(ApplicationFailureCode.BadRequest, "Bad", payload);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.BadRequest, result.Failure.Code);
        Assert.Equal("Bad", result.Failure.Message);
        Assert.Same(payload, result.Failure.Payload);
    }

    [Fact]
    public void PageResult_holds_items_and_pagination_metadata()
    {
        var items = new List<string> { "item1", "item2" };
        var page = new PageResult<string>(items, 1, 10, 2);

        Assert.Equal(items, page.Items);
        Assert.Equal(1, page.Page);
        Assert.Equal(10, page.Size);
        Assert.Equal(2, page.Total);
    }

    [Fact]
    public void DomainResult_apply_noop_and_reject()
    {
        var apply = DomainResult.Apply();
        Assert.True(apply.IsSuccess);
        Assert.True(apply.Applied);
        Assert.Null(apply.Failure);

        var noop = DomainResult.NoOp();
        Assert.True(noop.IsSuccess);
        Assert.False(noop.Applied);
        Assert.Null(noop.Failure);

        var reject = DomainResult.Reject(DomainFailureCode.Conflict, "Xung đột");
        Assert.False(reject.IsSuccess);
        Assert.False(reject.Applied);
        Assert.NotNull(reject.Failure);
        Assert.Equal(DomainFailureCode.Conflict, reject.Failure.Code);
        Assert.Equal("Xung đột", reject.Failure.Message);
    }
}
