namespace HealthExam.Application.Integrations;

public interface IHisCredentialOptions
{
    string CredentialHeaderName { get; }
    int? KskDepartmentId => null;
    string KskDepartmentCode => null;
}
