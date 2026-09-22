# Dynamic Step 2 registration form from HIS

## Purpose and scope

Step 2 of the health-exam frontend must show the **Tiền sử bệnh** and **Thông tin bổ sung** structures belonging to the selected Nhóm khám.  HIS M03 is the source of truth for the template tree.  This change is display-only: users do not enter or save values to HIS in this scope.

The initial supported group is:

| VariantCode | Group name | HIS TemplateCode |
| --- | --- | --- |
| `DTK_03` | Người từ đủ 18 tuổi trở lên | `KSK-TREN18TUOI` |

No other group is selectable by Step 2 until its mapping has been configured.

## Source of truth and boundaries

`health-exam-server` remains the only API consumed by the frontend.  It calls HIS M03 (`GetListTemplateByEMR`, `GetTemplateByID`, and `GetTreeTemplateByID`) through the existing HIS gateway.  The frontend never calls `his-server` and never interprets M03's raw JSON tree.

The existing `GET /v1/his-forms/{templateCode}` is a low-level, template-code-based bridge.  Step 2 receives a separate group-based contract so template codes and M03 wire details stay server-side.

## Persistent configuration

Create `HEX_ExamGroupFormMapping`:

| Column | Meaning |
| --- | --- |
| `MappingID` | Primary key |
| `DivisionID` | Tenant/organisation scope |
| `VariantCode` | Health-exam group, for example `DTK_03` |
| `TemplateCode` | Active HIS template code |
| `IsActive` | Whether Step 2 may use this mapping |
| audit columns | Standard created/modified attribution and timestamps |

Enforce one active mapping per `(DivisionID, VariantCode)`.

Create `HEX_ExamGroupFormSectionMapping`:

| Column | Meaning |
| --- | --- |
| `MappingID` | Parent group/template mapping |
| `SectionKind` | `HISTORY` or `EXTRA_INFO` |
| `ItemGroupCode` | Stable HIS M03 item-group code |

Enforce one mapping per `(MappingID, SectionKind)`.  HIS exposes `ItemGroupCode` and `ItemGroupName`, but it does not expose a universal semantic flag for these two sections.  Persisting the stable code avoids classifying sections by a mutable Vietnamese label.

The initial migration seeds the `DTK_03` mapping and its two real M03 item-group codes for `KSK-TREN18TUOI`.  Adding a future group is configuration-only: seed one template mapping plus its `HISTORY` and `EXTRA_INFO` section mappings.  No administrator UI or mapping-write API is included in this scope.

## Public API

Keep `GET /v1/master-data/registration-options` unchanged; its full set of ten statutory groups is still needed by filters, imports, and old records.

Add:

```http
GET /v1/exam-groups/available
```

It returns only active mappings for the caller's `DivisionID`, enriched with the statutory group code and name.  Step 2 uses this API for the Nhóm khám select, which therefore initially shows only `DTK_03`.

Add:

```http
GET /v1/exam-groups/{variantCode}/registration-form
```

The handler resolves the caller-tenant's active mapping, obtains the selected template from HIS, and returns a normalized, display-only contract:

- group and template metadata;
- exactly the configured `HISTORY` and `EXTRA_INFO` sections;
- ordered tree nodes containing stable identifiers, parent relationships/order, label, control/data type, required/read-only flags, and any choices supplied by HIS.

The contract must not leak raw M03 JSON or require the client to know `TemplateCode` or `ItemGroupCode`.

An unmapped/inactive group returns a domain error saying the group has no configured form.  A template missing or inactive in HIS, or a mapped section absent from its tree, returns an explicit load error; no hard-coded frontend fallback is allowed.

## Runtime flow

```text
Step 2 loads available groups
  -> user selects DTK_03
  -> frontend requests registration-form/DTK_03
  -> health-exam-server reads tenant mapping
  -> health-exam-server resolves KSK-TREN18TUOI from HIS M03
  -> server filters and normalizes HISTORY + EXTRA_INFO
  -> frontend renders the two read-only section trees
```

The existing HIS definition cache becomes keyed by `DivisionID + TemplateCode`, not by the current single hard-coded template.  A changed selection discards the previous view state; the client renders only a response that belongs to the currently selected group.

## Frontend behavior

Replace the current fixed Tiền sử popup and `ADDITIONAL_INFO_REGISTRY` in Step 2 with one generic, display-only tree renderer.  It uses the normalized section contract, not a local registry.  Controls are non-editable in this phase.

States are:

- no group selected: prompt the user to choose a group;
- loading: skeleton for both sections;
- loaded: render both HIS sections in their server-defined hierarchy/order;
- failed: error and Retry action; never render old hard-coded fields.

## Verification

Backend tests cover tenant mapping lookup, inactive/missing mappings, section completeness, the selected template code sent to HIS, and template-cache isolation by tenant/template.

Frontend tests cover the available-group select, load/error/retry states, correct rendering of both sections, and removal of a previous group form on a selection change.

No value-entry, form submission, HIS mutation, or mapping-management UI/API is part of this design.
