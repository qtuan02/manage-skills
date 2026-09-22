using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HealthExam.Infrastructure.Integrations.FormServer;

public class FormServerEnvelope<T>
{
    public int ErrorCode { get; set; }
    public string Message { get; set; } = "";
    public T Data { get; set; }
    public string TraceID { get; set; } = "";
}

public class FormResolveDto
{
    public Guid FormID { get; set; }
    public string FormCode { get; set; } = "";
    public string FormName { get; set; } = "";
    public string VersionCode { get; set; } = "";
    public string VariantCode { get; set; } = "";
    public string VariantName { get; set; }
    public int DocTypeID { get; set; }
    public string ModuleCode { get; set; } = "";
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
    public int MatchCount { get; set; }
}

public class SubmissionDraftDto
{
    public Guid FormID { get; set; }
    public string HostRefType { get; set; } = "";
    public string HostRefID { get; set; } = "";
    public JToken Layout { get; set; }
    public JArray PrefillValues { get; set; }

    [JsonProperty("UnresolvedSources")]
    public JArray MissingContext { get; set; }
}

public class SubmissionDraftRequest
{
    public Dictionary<string, Dictionary<string, string>> ContextValues { get; set; } = new();
}

public class SectionSignRequest
{
    public long ActorID { get; set; }
    public short ActorKind { get; set; }
    public string ActorName { get; set; } = "";
    public object SignatoryFlows { get; set; }
}

public class SectionSignDto
{
    public long SectionID { get; set; }
    public short? State { get; set; }
    public short? SignStatus { get; set; }
}
