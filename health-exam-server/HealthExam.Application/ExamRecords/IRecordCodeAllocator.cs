using System;
using System.Threading;
using System.Threading.Tasks;

namespace HealthExam.Application.ExamRecords;

public interface IRecordCodeAllocator
{
    Task<string> NextAsync(string divisionId, Guid sessionId, string sessionCode, CancellationToken ct = default);
}
