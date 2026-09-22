using System.Collections.Generic;

namespace HealthExam.Application.Common;

public sealed class ApplicationResult<T>
{
    public bool IsSuccess { get; }
    public T Value { get; }
    public ApplicationFailure Failure { get; }

    private ApplicationResult(bool success, T value, ApplicationFailure failure)
        => (IsSuccess, Value, Failure) = (success, value, failure);

    public static ApplicationResult<T> Success(T value) => new(true, value, null);
    public static ApplicationResult<T> Fail(
        ApplicationFailureCode code, string message, object payload = null)
        => new(false, default, new ApplicationFailure(code, message, payload));
}

public sealed record PageResult<T>(
    IReadOnlyList<T> Items, int Page, int Size, int Total);
