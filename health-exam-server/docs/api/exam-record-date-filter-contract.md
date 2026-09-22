# Exam Record Date Filter Contract

**Tickets:** PROJ2365, PROJ2367

## 1. Objective & Design
The exam record search and listing API filters records by creation date (`CreatedDate`), aligning with the Vietnam local calendar day (UTC+7, `Asia/Ho_Chi_Minh`), regardless of server host timezone.

## 2. API Contract
- Endpoint: `GET /api/v1/exam-records`
- Parameters:
  - `fromDate`: `YYYY-MM-DD` (DateOnly) - start date in Vietnam calendar (inclusive).
  - `toDate`: `YYYY-MM-DD` (DateOnly) - end date in Vietnam calendar (inclusive).

## 3. Boundary Calculation
The server translates DateOnly filters into UTC bounds as follows:
- `fromDate`: `YYYY-MM-DD 00:00:00` in UTC+7 $\rightarrow$ converted to UTC.
  - E.g. `2026-09-19` corresponds to `2026-09-18T17:00:00.000Z`.
- `toDate`: `(toDate + 1 day) 00:00:00` in UTC+7 $\rightarrow$ converted to UTC (exclusive upper bound).
  - E.g. `2026-09-19` corresponds to `2026-09-19T17:00:00.000Z`.
- For single day `2026-09-19`:
  - Range: `[2026-09-18T17:00:00.000Z, 2026-09-19T17:00:00.000Z)`.

## 4. UI Alignment
- Both the search/filter screen and the list table display must present dates in Vietnam time (UTC+7).
- `ExamRecordItem.CreatedDate` is in UTC timestamp; client formats using local VN time.
- `ExamRecordItem.ExamDate` is the session date (`Session.ExamDate`), separate from `CreatedDate`.
