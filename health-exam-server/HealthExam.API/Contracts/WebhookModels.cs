using System;
using System.Collections.Generic;

namespace HealthExam.API.Contracts;

public class ExamSessionProgressItem
{
    public Guid SessionID { get; set; }
    public string SessionCode { get; set; } = "";
    public string State { get; set; } = "";
    public ExamSessionProgressCounts Profiles { get; set; } = new();
    public int ConclusionReady { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class ExamSessionProgressCounts
{
    public int Total { get; set; }
    public int NotRegistered { get; set; }
    public int Waiting { get; set; }
    public int InProgress { get; set; }
    public int Completed { get; set; }
    public int Cancelled { get; set; }
    public int RegistrationCancelled { get; set; }
    public int ExamCancelled { get; set; }
}
