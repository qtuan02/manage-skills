# GitNexus Engineering Plan

> Task: Persist KSK03 mapping and auto-create HIS medical process
> Evidence verified at commit 5d87cd0cd15ab63caafeed6673a7a2f3720a78be; GitNexus source-verified (working tree has unstaged edits).
> Evidence provenance schema 2; global dirty digest 9fd3b688b1be98a8351b78934d8d42e1f5d548a9f1de5b12a7e0890336312aa0; cited-path manifest 0 entries; exact generated plan path excluded.

## Objective (§1)

When a new record uses the mapped KSK adult form, resolve MedicalTypeCode=KSK03 from DB and create HIS M02_MedicalProcess after a valid Admission exists.

## Current Behaviour (§2–3)

[verified] HEX_ExamGroupFormMapping stores DivisionID, VariantCode, TemplateCode, activation and audit fields; CreateExamRecordHandler persists AdmissionID but does not create a medical process. [verified] EnsureHisAdmission creates/reuses the HIS Admission and saves its ID. [verified] IHisEmrClient has generic SendAsync plus patient/admission helpers; HIS route is POST api/M02F01500/CMedicalProcess.

## Findings (§4–5)

[graph] GitNexus query located CreateExamRecordHandler, EnsureHisAdmission, HisEmrClient and related tests; no executable process was indexed, so source is authoritative.
[verified] HIS CMedicalProcessHandler resolves FileMedicalType→FileDocType→Template and creates process/detail rows; repeated calls require read-back via RMedicalProcess.
[inferred] MedicalTypeCode belongs in health-exam mapping, not HIS template data: it is tenant/variant configuration while template/layout remain HIS-owned.

## Proposed Changes (§6)

[verified] Add required nullable-safe MedicalTypeCode varchar(50) to ExamGroupFormMapping; seed DHTESTING/DTK_03/KSK-TREN18TUOI/KSK03 and add migration.
[verified] Extend IHisEmrClient/HisEmrClient with CreateMedicalProcessAsync(MedicalProcessCreateRequest, HisCallContext) posting MedicalTypeCode, MedicalTypeCodeOld:null, AdmissionID.
[verified] In CreateExamRecordHandler, after Admission resolution, load active mapping by DivisionID+VariantCode; create process and read back RMedicalProcess. Existing KSK03 process counts as success; do not add runtime HIS IDs locally.

## Implementation Sequence (§7)

1. Add domain property, EF column, seed and migration.
2. Add HIS request DTO, route, client method and fake support.
3. Extend mapping repository read and validate active mapping configuration.
4. Wire process creation after Admission; preserve local record on remote failure and expose retryable error.
5. Add tests; run migration validation and full suite.

## Test Strategy (§8)

Update ExamRecordHandlerTests: Admission-present creates process; Admission-missing creates Admission then process; existing process is reused; missing code fails clearly; HIS failure maps to dependency error. Update HisEmrClientTests for POST route/body and unauthorized/error mapping. Run dotnet test HealthExamServer.sln.

## Risk and Impact (§9)

Mapping consumers must retain existing TemplateCode behavior. The HIS call follows local commit, so remote failure can leave a record without a process; add explicit status/error for retry. Do not store MedicalProcessID, TemplateID, FileDocTypeID or SWTID in HEX_ExamRecord.

## Files Expected to Change (§10)

| File | Responsibility |
| --- | --- |
| HealthExam.Domain/ExamForms/ExamGroupFormMapping.cs | MedicalTypeCode property |
| HealthExam.Infrastructure/Persistence/HealthExamDbContext.cs + new migration | schema and seed |
| HealthExam.Application/RegistrationForms/IRegistrationFormRepository.cs + implementation | mapping read |
| HealthExam.Application/Integrations/IHisEmrClient.cs | client contract |
| HealthExam.Infrastructure/Integrations/HisEmr/HisEmrClient.cs | HIS POST adapter |
| HealthExam.Application/ExamRecords/CreateExamRecord.cs | orchestration |
| HealthExam.Tests/Application/ExamRecordHandlerTests.cs and HealthExam.Tests/HisEmrClientTests.cs | regression tests |

## Implementation Context (§11)

CreateExamRecordHandler commits the local record before optional HIS admission resolution. EnsureHisAdmission is the existing Admission seam. Mapping lookup is IRegistrationFormRepository.GetActiveMappingAsync(divisionId, variantCode). Propagate HisCallContext credential/trace/division. Payload is exactly {MedicalTypeCode, MedicalTypeCodeOld:null, AdmissionID}.

evidence_provenance = {"schema_version":2,"head_commit":"5d87cd0cd15ab63caafeed6673a7a2f3720a78be","generated_plan_path":"docs/plans/2026-09-16-gitnexus-plan-auto-medical-process.md","global_dirty_digest":{"algorithm":"sha256","canonicalization":"gitnexus-evidence-provenance-v2 NUL-framed UTF-8 records","value":"9fd3b688b1be98a8351b78934d8d42e1f5d548a9f1de5b12a7e0890336312aa0"},"cited_path_manifest":[{"path":"HealthExam.Application/ExamRecords/CreateExamRecord.cs","object_kind":{"head":"regular","index":"regular","worktree":"regular","untracked":"absent"},"state":"unstaged","rename_from":null,"rename_to":null,"head_digest":"sha256:7a1970f631e70c9e8c45c04016abde6e4ff9ea18a046e87a0a326cea0e75f726","index_digest":"sha256:7a1970f631e70c9e8c45c04016abde6e4ff9ea18a046e87a0a326cea0e75f726","worktree_digest":"sha256:d451b202feddd35bec21243438fbf7ec067f648a3f1c780ba0d2f9d9f4530a3b","untracked_digest":"absent"},{"path":"HealthExam.Application/His/EnsureHisAdmission.cs","object_kind":{"head":"regular","index":"regular","worktree":"regular","untracked":"absent"},"state":"clean","rename_from":null,"rename_to":null,"head_digest":"sha256:657819fa5c85ceef79d1a7dca4606a75f3a634146097779f86a966f22726ab6d","index_digest":"sha256:657819fa5c85ceef79d1a7dca4606a75f3a634146097779f86a966f22726ab6d","worktree_digest":"sha256:657819fa5c85ceef79d1a7dca4606a75f3a634146097779f86a966f22726ab6d","untracked_digest":"absent"},{"path":"HealthExam.Application/Integrations/IHisEmrClient.cs","object_kind":{"head":"regular","index":"regular","worktree":"regular","untracked":"absent"},"state":"clean","rename_from":null,"rename_to":null,"head_digest":"sha256:dfdcb7abdb41139918a8044c18d7b3a5c3ae5335fa0dbcbc748e2b5f50b1af17","index_digest":"sha256:dfdcb7abdb41139918a8044c18d7b3a5c3ae5335fa0dbcbc748e2b5f50b1af17","worktree_digest":"sha256:dfdcb7abdb41139918a8044c18d7b3a5c3ae5335fa0dbcbc748e2b5f50b1af17","untracked_digest":"absent"},{"path":"HealthExam.Application/RegistrationForms/RegistrationFormModels.cs","object_kind":{"head":"regular","index":"regular","worktree":"regular","untracked":"absent"},"state":"clean","rename_from":null,"rename_to":null,"head_digest":"sha256:7f7b7153f64d2dcdfe665474772924cf17947aa7e46f9c814db652dbe2cf8939","index_digest":"sha256:7f7b7153f64d2dcdfe665474772924cf17947aa7e46f9c814db652dbe2cf8939","worktree_digest":"sha256:7f7b7153f64d2dcdfe665474772924cf17947aa7e46f9c814db652dbe2cf8939","untracked_digest":"absent"},{"path":"HealthExam.Domain/ExamForms/ExamGroupFormMapping.cs","object_kind":{"head":"regular","index":"regular","worktree":"regular","untracked":"absent"},"state":"clean","rename_from":null,"rename_to":null,"head_digest":"sha256:41f383af3703460aec0692d3a3d30e9d245a07eee4caa9584feb5688c647a5dc","index_digest":"sha256:41f383af3703460aec0692d3a3d30e9d245a07eee4caa9584feb5688c647a5dc","worktree_digest":"sha256:41f383af3703460aec0692d3a3d30e9d245a07eee4caa9584feb5688c647a5dc","untracked_digest":"absent"},{"path":"HealthExam.Infrastructure/Integrations/HisEmr/HisEmrClient.cs","object_kind":{"head":"regular","index":"regular","worktree":"regular","untracked":"absent"},"state":"clean","rename_from":null,"rename_to":null,"head_digest":"sha256:3cbcae4cf3a5d8627e3b213d8d0636cf3737805ac3e65b9d71ff079ae9377410","index_digest":"sha256:3cbcae4cf3a5d8627e3b213d8d0636cf3737805ac3e65b9d71ff079ae9377410","worktree_digest":"sha256:3cbcae4cf3a5d8627e3b213d8d0636cf3737805ac3e65b9d71ff079ae9377410","untracked_digest":"absent"},{"path":"HealthExam.Infrastructure/Persistence/HealthExamDbContext.cs","object_kind":{"head":"regular","index":"regular","worktree":"regular","untracked":"absent"},"state":"unstaged","rename_from":null,"rename_to":null,"head_digest":"sha256:25a63f21423fd64ae00e1a2e47f76de1c2d5a2ae5fc377006be1ef47f8630b27","index_digest":"sha256:25a63f21423fd64ae00e1a2e47f76de1c2d5a2ae5fc377006be1ef47f8630b27","worktree_digest":"sha256:26c2396456b709b0f29a69dff5b6b830c454a22f20f714d4457f1fe582222d15","untracked_digest":"absent"},{"path":"HealthExam.Tests/Application/ExamRecordHandlerTests.cs","object_kind":{"head":"regular","index":"regular","worktree":"regular","untracked":"absent"},"state":"unstaged","rename_from":null,"rename_to":null,"head_digest":"sha256:791927253b1bec2fccecac8d1c5bd9099326334895394f839f7a7c1bfbab300d","index_digest":"sha256:791927253b1bec2fccecac8d1c5bd9099326334895394f839f7a7c1bfbab300d","worktree_digest":"sha256:610901c666ef38595b4be36f9838f49f9ac0ce2e84bf929b337cc1781ced747c","untracked_digest":"absent"}]}

## Assumptions and Open Questions (§12)

- [assumed] Auto-create runs only when an active mapping has MedicalTypeCode; other variants remain unchanged.
- [assumed] Remote failure is retryable and does not delete/rollback local registration.
- Deferred: durable outbox/retry worker, add when operational recovery is required.

## Definition of Done (§13)

- Mapping returns KSK03 for DHTESTING/DTK_03.
- Mapped record creation yields exactly one HIS KSK03 process.
- Existing process is reused; duplicates are avoided.
- Configuration and HIS errors are explicit; tests pass.
