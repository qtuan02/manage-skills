using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Domain.Common;
using HealthExam.Domain.Webhooks;

namespace HealthExam.Application.Webhooks;

public interface IIngestWebhookHandler
{
    Task<ApplicationResult<WebhookAckResult>> HandleAsync(
        IngestWebhookCommand command, CancellationToken ct = default);
}

public class IngestWebhookHandler : IIngestWebhookHandler
{
    private readonly IWebhookInboxRepository _repository;
    private readonly IUnitOfWork _uow;
    private readonly IWebhookMetricsTracker _tracker;

    public IngestWebhookHandler(
        IWebhookInboxRepository repository,
        IUnitOfWork uow,
        IWebhookMetricsTracker tracker)
    {
        _repository = repository;
        _uow = uow;
        _tracker = tracker;
    }

    public async Task<ApplicationResult<WebhookAckResult>> HandleAsync(
        IngestWebhookCommand command, CancellationToken ct = default)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(command.EventID))
            errors[nameof(command.EventID)] = new[] { "Bỏ trống (bắt buộc)" };
        else if (command.EventID.Trim().Length > WebhookFieldLengths.EventID)
            errors[nameof(command.EventID)] = new[] { $"Dài {command.EventID.Trim().Length} ký tự, vượt trần {WebhookFieldLengths.EventID}" };

        if (string.IsNullOrWhiteSpace(command.EventType))
            errors[nameof(command.EventType)] = new[] { "Bỏ trống (bắt buộc)" };
        else if (command.EventType.Trim().Length > WebhookFieldLengths.EventType)
            errors[nameof(command.EventType)] = new[] { $"Dài {command.EventType.Trim().Length} ký tự, vượt trần {WebhookFieldLengths.EventType}" };

        var bodyDivision = (command.DivisionID ?? "").Trim();
        if (bodyDivision.Length == 0)
            errors[nameof(command.DivisionID)] = new[] { "Bỏ trống (bắt buộc)" };
        else if (!string.IsNullOrEmpty(command.HeaderDivisionId)
                 && !string.Equals(bodyDivision, command.HeaderDivisionId, StringComparison.Ordinal))
            errors[nameof(command.DivisionID)] = new[] { "Không khớp X-Division-Id" };

        if (errors.Count > 0)
            return ApplicationResult<WebhookAckResult>.Fail(
                ApplicationFailureCode.BadRequest, "Dữ liệu webhook không hợp lệ", errors);

        var eventId = command.EventID.Trim();
        var eventType = command.EventType.Trim();
        var known = FormEventNames.IsKnown(eventType);

        var nowUtc = DateTime.UtcNow;

        var row = new WebhookInbox
        {
            EventID = eventId,
            EventType = eventType,
            DivisionID = bodyDivision,
            SubmissionID = command.SubmissionID,
            Payload = string.IsNullOrWhiteSpace(command.RawPayload) ? "{}" : command.RawPayload,
            OccurredAt = command.OccurredAt?.UtcDateTime ?? nowUtc,
            ReceivedAt = nowUtc,
            TraceID = Truncate(command.TraceId, WebhookFieldLengths.TraceID),
            ProcessState = known ? WebhookProcessState.New : WebhookProcessState.Skipped,
            ProcessedAt = known ? null : nowUtc,
            LastError = known ? "" : "Tên sự kiện không thuộc bảng 02-api-spec §5.2"
        };

        if (!known)
        {
            if (FormEventNames.IsKnownIgnored(eventType))
            {
                _tracker.CountIgnoredEvent();
                row.LastError = "Sự kiện thuộc danh mục form-server nhưng KSK không tiêu thụ";
            }
            else
            {
                _tracker.CountUnknownEvent();
            }
        }

        if (await _repository.ExistsAsync(bodyDivision, eventId, ct))
        {
            _tracker.CountDuplicated();
            return ApplicationResult<WebhookAckResult>.Success(new WebhookAckResult
            {
                EventID = eventId,
                Duplicated = true,
                Accepted = known
            });
        }

        _repository.Add(row);

        try
        {
            await _uow.SaveChangesAsync(ct);
        }
        catch (Exception)
        {
            _uow.DiscardPendingChanges();
            // If unique constraint violation or concurrent insert occurred
            if (await _repository.ExistsAsync(bodyDivision, eventId, ct))
            {
                _tracker.CountDuplicated();
                return ApplicationResult<WebhookAckResult>.Success(new WebhookAckResult
                {
                    EventID = eventId,
                    Duplicated = true,
                    Accepted = known
                });
            }
            throw;
        }

        _tracker.CountReceived();

        return ApplicationResult<WebhookAckResult>.Success(new WebhookAckResult
        {
            EventID = eventId,
            Duplicated = false,
            Accepted = known
        });
    }

    private static string Truncate(string value, int max)
    {
        value ??= "";
        return value.Length <= max ? value : value[..max];
    }
}
