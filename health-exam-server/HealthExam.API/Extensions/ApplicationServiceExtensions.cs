using HealthExam.Application.Auth;
using HealthExam.Application.Catalogs;
using HealthExam.Application.Common;
using HealthExam.Application.ExamRecords;
using HealthExam.Application.ExamSessions;
using HealthExam.Application.His;
using HealthExam.Application.Imports;
using HealthExam.Application.Paraclinical;
using HealthExam.Application.Patients;
using HealthExam.Application.RegistrationForms;
using HealthExam.Application.Signing;
using HealthExam.Application.Webhooks;
using Microsoft.Extensions.DependencyInjection;

namespace HealthExam.API.Extensions;

public static class ApplicationServiceExtensions
{
    public static IServiceCollection AddHealthExamApplication(this IServiceCollection services)
    {
        // Common & Utilities
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<IHisProcessValidator, HisProcessValidator>();

        // Catalogs
        services.AddScoped<IListOrganizationsHandler, ListOrganizationsHandler>();
        services.AddScoped<IListExamPackagesHandler, ListExamPackagesHandler>();
        services.AddScoped<IListExamGroupsHandler, ListExamGroupsHandler>();
        services.AddScoped<IGetRegistrationOptionsHandler, GetRegistrationOptionsHandler>();
        services.AddScoped<IListProvincesHandler, ListProvincesHandler>();
        services.AddScoped<IListWardsHandler, ListWardsHandler>();
        services.AddScoped<IListServiceCategoriesHandler, ListServiceCategoriesHandler>();
        services.AddScoped<IListServiceGroupsHandler, ListServiceGroupsHandler>();
        services.AddScoped<IListServicesHandler, ListServicesHandler>();

        // Exam Sessions
        services.AddScoped<IListExamSessionsHandler, ListExamSessionsHandler>();
        services.AddScoped<IGetExamSessionHandler, GetExamSessionHandler>();
        services.AddScoped<ICreateExamSessionHandler, CreateExamSessionHandler>();
        services.AddScoped<IUpdateExamSessionHandler, UpdateExamSessionHandler>();
        services.AddScoped<ICloseExamSessionHandler, CloseExamSessionHandler>();
        services.AddScoped<IReopenExamSessionHandler, ReopenExamSessionHandler>();

        // Exam Records
        services.AddScoped<PatientRegistrationWriter>();
        services.AddScoped<ISearchPatientsHandler, SearchPatientsHandler>();
        services.AddScoped<IGetPatientProfileHandler, GetPatientProfileHandler>();
        services.AddScoped<IMatchPatientProfileHandler, MatchPatientProfileHandler>();
        services.AddScoped<IGetPatientExamHistoryHandler, GetPatientExamHistoryHandler>();
        services.AddScoped<IListExamRecordsHandler, ListExamRecordsHandler>();
        services.AddScoped<IGetExamRecordHandler, GetExamRecordHandler>();
        services.AddScoped<ICreateExamRecordHandler, CreateExamRecordHandler>();
        services.AddScoped<IUpdateExamRecordHandler, UpdateExamRecordHandler>();
        services.AddScoped<IConfirmExamRecordHandler, ConfirmExamRecordHandler>();
        services.AddScoped<ICancelExamRecordHandler, CancelExamRecordHandler>();
        services.AddScoped<IVerifyPortalCredentialsHandler, VerifyPortalCredentialsHandler>();
        services.AddScoped<IGetExamSessionProgressHandler, GetExamSessionProgressHandler>();
        services.AddScoped<IGetExamFormDraftHandler, GetExamFormDraftHandler>();

        // Imports
        services.AddScoped<IDownloadImportTemplateHandler, DownloadImportTemplateHandler>();
        services.AddScoped<IUploadImportHandler, UploadImportHandler>();
        services.AddScoped<IGetImportHandler, GetImportHandler>();
        services.AddScoped<IListImportErrorsHandler, ListImportErrorsHandler>();
        services.AddScoped<ICommitImportHandler, CommitImportHandler>();
        services.AddScoped<IDiscardImportHandler, DiscardImportHandler>();

        // Paraclinical
        services.AddScoped<IGetRecordOrdersHandler, GetRecordOrdersHandler>();
        services.AddScoped<ICreateOrdersHandler, CreateOrdersHandler>();
        services.AddScoped<ICreateOrdersFromPackageHandler, CreateOrdersFromPackageHandler>();
        services.AddScoped<IGetOrderHandler, GetOrderHandler>();
        services.AddScoped<IGetOrderResultsHandler, GetOrderResultsHandler>();
        services.AddScoped<IGetResultHandler, GetResultHandler>();
        services.AddScoped<ICancelOrderHandler, CancelOrderHandler>();
        services.AddScoped<IChangeOrderStateHandler, ChangeOrderStateHandler>();
        services.AddScoped<IGetConclusionEligibilityHandler, GetConclusionEligibilityHandler>();
        services.AddScoped<ISignConclusionHandler, SignConclusionHandler>();
        services.AddScoped<ICancelConclusionSignHandler, CancelConclusionSignHandler>();
        services.AddScoped<IDispatchOrderHandler, DispatchOrderHandler>();
        services.AddScoped<IUpdateVendorStatusHandler, UpdateVendorStatusHandler>();
        services.AddScoped<IProcessOutboxBatchHandler, ProcessOutboxBatchHandler>();

        // Webhooks
        services.AddScoped<IIngestWebhookHandler, IngestWebhookHandler>();
        services.AddScoped<IProcessWebhookBatchHandler, ProcessWebhookBatchHandler>();
        services.AddScoped<IRequeueWebhooksHandler, RequeueWebhooksHandler>();
        services.AddScoped<IBackfillScanResultsHandler, BackfillScanResultsHandler>();
        services.AddScoped<IGetWebhookMetricsHandler, GetWebhookMetricsHandler>();
        services.AddScoped<IReconcileProgressHandler, ReconcileProgressHandler>();

        // HIS EMR
        services.AddScoped<IGetHisFormDefinitionHandler, GetHisFormDefinitionHandler>();
        services.AddScoped<IGetHisRecordSectionHandler, GetHisRecordSectionHandler>();
        services.AddScoped<IGetExamRecordHisFormHandler, GetExamRecordHisFormHandler>();
        services.AddScoped<IListHisProcessesHandler, ListHisProcessesHandler>();
        services.AddScoped<IGetHisProcessHandler, GetHisProcessHandler>();
        services.AddScoped<IEnsureHisPatient, EnsureHisPatient>();
        services.AddScoped<EnsureHisPatient>();
        services.AddScoped<IEnsureHisAdmission, EnsureHisAdmission>();
        services.AddScoped<EnsureHisAdmission>();
        services.AddScoped<IGetIcd10ChoicesHandler, GetIcd10ChoicesHandler>();
        services.AddScoped<IListEmployeeDepartmentsHandler, ListEmployeeDepartmentsHandler>();
        services.AddScoped<ISaveHisFormSectionHandler, SaveHisFormSectionHandler>();
        services.AddScoped<IResetHisFormSectionHandler, ResetHisFormSectionHandler>();

        // Registration Forms
        services.AddScoped<IGetExamGroupRegistrationFormsHandler, GetExamGroupRegistrationFormsHandler>();
        services.AddScoped<IListAvailableExamGroupsHandler, ListAvailableExamGroupsHandler>();
        services.AddScoped<IGetRegistrationFormHandler, GetRegistrationFormHandler>();
        services.AddScoped<ISaveRegistrationFormSectionHandler, SaveRegistrationFormSectionHandler>();
        services.AddScoped<IPreviewRegistrationFormPdfHandler, PreviewRegistrationFormPdfHandler>();

        // Auth
        services.AddScoped<ILoginHandler, LoginHandler>();
        services.AddScoped<IGetCurrentUserHandler, GetCurrentUserHandler>();
        services.AddScoped<ILogoutHandler, LogoutHandler>();

        // Signing
        services.AddScoped<ISignExamSectionHandler, SignExamSectionHandler>();
        services.AddScoped<ICancelExamSectionSignHandler, CancelExamSectionSignHandler>();

        return services;
    }
}
