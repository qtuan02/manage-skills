# Patient Profile Address & Catalog Round-Trip Contract

**Tickets:** PROJ2363, PROJ2364

## 1. Context & Rationale
When registering or updating a patient (or creating an exam record from an existing profile), the patient's address consists of:
- `Address`: Freeform text (street number, building, etc.).
- `ProvinceCode`: Canonical master data code of the province (`HEX_MasterDataOption` category `PROVINCE`).
- `WardCode`: Canonical master data code of the ward (`HEX_MasterDataOption` category `WARD`).

In addition, catalog options such as `EthnicityOptionID` and `IdentityIssuerOptionID` are resolved from their respective master data codes or carried through as GUIDs.

## 2. API Contracts

### GET `v1/patients/{patientRefId}/profile`
Response JSON (`PatientProfileResult`):
```json
{
  "patientRefID": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
  "divisionID": "D01",
  "hisPatientID": 125001,
  "patientCode": "BN000125001",
  "fullName": "Nguyễn Văn An",
  "dob": "1990-05-20",
  "birthYear": 1990,
  "genderID": 1,
  "identityNumber": "079123456789",
  "identityIssuedDate": "2021-06-15",
  "identityIssuerOptionID": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "identityIssuerCode": "CCS",
  "identityIssuerName": "Cục Cảnh sát QLHC về TTXH",
  "phoneNumber": "0901234567",
  "email": "an@example.com",
  "address": "12 Nguyễn Huệ",
  "provinceCode": "79",
  "wardCode": "26734",
  "ethnicityOptionID": "e1f2a3b4-c5d6-7890-1234-567890abcdef",
  "ethnicityCode": "KINH",
  "ethnicityName": "Kinh",
  "bloodAboCode": "A",
  "bloodRhCode": "+",
  "isActive": true
}
```

### POST `v1/patients/registration` (or via ExamRecord Create/Update)
Request JSON (`PatientWriteRequest`):
```json
{
  "patientRefID": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
  "fullName": "Nguyễn Văn An",
  "dob": "1990-05-20",
  "birthYear": 1990,
  "genderID": 1,
  "identityNumber": "079123456789",
  "identityIssuedDate": "2021-06-15",
  "identityIssuerOptionID": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "phoneNumber": "0901234567",
  "email": "an@example.com",
  "address": "12 Nguyễn Huệ",
  "provinceCode": "79",
  "wardCode": "26734",
  "ethnicityOptionID": "e1f2a3b4-c5d6-7890-1234-567890abcdef",
  "bloodAboCode": "A",
  "bloodRhCode": "+",
  "setAsActiveProfile": null
}
```

## 3. Profile Comparison Rules
- `ProvinceCode` and `WardCode` participate in `PatientProfileComparer.Compare`.
- If `ProvinceCode` or `WardCode` changes (e.g. from `"79"` to `"01"`), and `SetAsActiveProfile` is `null`, writer returns `PatientProfileChanged` failure with `"ProvinceCode"` and/or `"WardCode"` in `ChangedFields`.
- Unchanged `ProvinceCode` and `WardCode` do not produce false changes and are not silently overwritten on unchanged branches.
- Master data GUIDs (`EthnicityOptionID`, `IdentityIssuerOptionID`) and codes are preserved across round-trips without mutating historical versions.
