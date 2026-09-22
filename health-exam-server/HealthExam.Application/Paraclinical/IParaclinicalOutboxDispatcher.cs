using System;
using System.Collections.Generic;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.Paraclinical;

namespace HealthExam.Application.Paraclinical;

public interface IParaclinicalOutboxDispatcher
{
    void EnqueueCancel(ParaclinicalOrder order, ExamRecord record, IReadOnlyList<ParaclinicalOrderItem> cancelledLines, DateTime nowUtc);
}
