namespace HealthExam.Application.Integrations;

public interface IVendorCallbackAuthOptions
{
    bool IsCallbackConfigured { get; }
    string CallbackUser { get; }
    string CallbackPassword { get; }
    string CallbackUserEnv { get; }
    string CallbackPasswordEnv { get; }
}
