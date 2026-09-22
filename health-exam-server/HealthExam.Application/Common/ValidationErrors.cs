using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace HealthExam.Application.Common;

/// <summary>
/// Chi tiết lỗi validate.
/// </summary>
public class ValidationErrors : IEnumerable<ValidationError>
{
    public List<ValidationError> Errors { get; set; } = new();

    public void Add(string field, string reason, int? row = null)
    {
        Errors.Add(new ValidationError { Field = field, Reason = reason, Row = row });
    }

    public void Add(ValidationError error)
    {
        Errors.Add(error);
    }

    public string this[string field] => Errors.FirstOrDefault(e => e.Field == field)?.Reason;

    public IEnumerator<ValidationError> GetEnumerator() => Errors.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => Errors.GetEnumerator();

    public static ValidationErrors Of(string field, string reason, int? row = null)
        => new() { Errors = { new ValidationError { Field = field, Reason = reason, Row = row } } };
}

public class ValidationError
{
    public string Field { get; set; } = "";
    public int? Row { get; set; }
    public string Reason { get; set; } = "";
}
