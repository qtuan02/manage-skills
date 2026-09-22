-- =====================================================================================
-- Seed tối thiểu cho màn Đăng ký KSK — health-exam-server, phase đăng ký (PROJ-2222/2223/2224)
--
-- Chạy:
--   PGPASSWORD=... psql -h <host> -p 13300 -U emr -d DEV_S2_HEALTHEXAM \
--       -f Deploy/seed/health-exam-seed.sql
--
-- ⚠️ DivisionID = 'DEV' — PHẢI KHỚP giá trị FE/curl gửi ở header X-Division-Id. Mọi truy vấn
--    đều lọc theo DivisionID; lệch một ký tự thì API trả mảng rỗng mà trông vẫn hoàn toàn
--    khoẻ mạnh, không có lỗi nào để lần ra. Đổi tenant thì sửa hằng số :division bên dưới.
--
-- Idempotent: UUID cố định + ON CONFLICT DO NOTHING, chạy lại bao nhiêu lần cũng ra một
-- trạng thái. Cố ý KHÔNG dùng DO UPDATE: seed không được phép ghi đè dữ liệu người dùng đã
-- sửa tay trên môi trường DEV.
-- =====================================================================================

\set division 'DEV'

-- Chốt chặn database: in ra để người chạy đối chiếu trước khi tin kết quả. Nhiều DB tenant
-- nằm CÙNG MỘT HOST, và một lần seed nhầm connection trông y hệt một lần seed đúng.
SELECT current_database() AS "Đang seed vào database", current_user AS "User";

BEGIN;

-- ---------------------------------------------------------------------------------
-- 1. Đơn vị ký hợp đồng khám (2 đơn vị)
-- ---------------------------------------------------------------------------------
INSERT INTO "HEX_Organization"
  ("OrganizationID", "DivisionID", "OrgCode", "OrgName", "ShortName", "TaxCode",
   "Address", "ContactName", "ContactPhone", "ContactEmail", "IsActive",
   "CreatedDate", "CreatedBy", "CreatedActorKind", "ModifiedDate", "ModifiedBy", "ModifiedActorKind")
VALUES
  ('a0000000-0000-4000-8000-000000000001', :'division', 'ORG_TNS',
   'Công ty TNHH Thương mại Nam Sơn', 'Nam Sơn', '0312345678',
   '15 Nguyễn Văn Cừ, Quận 5, TP.HCM', 'Trần Thị Hà', '0908111222', 'hattt@namson.example', true,
   now(), 0, 3, now(), 0, 3),
  ('a0000000-0000-4000-8000-000000000002', :'division', 'ORG_VTMB',
   'Công ty Cổ phần Vận tải Miền Bắc', 'Vận tải Miền Bắc', '0109876543',
   '221 Trần Duy Hưng, Cầu Giấy, Hà Nội', 'Lê Văn Bình', '0912333444', 'binhlv@vtmb.example', true,
   now(), 0, 3, now(), 0, 3)
ON CONFLICT ("OrganizationID") DO NOTHING;

-- ---------------------------------------------------------------------------------
-- 2. Gói khám (2 gói)
--    Gói cơ bản để VariantCode NULL = dùng chung mọi Nhóm khám.
--    Gói lái xe gắn DTK_06 để FE kiểm chứng được bộ lọc ?variantCode=.
-- ---------------------------------------------------------------------------------
INSERT INTO "HEX_ExamPackage"
  ("PackageID", "DivisionID", "PackageCode", "PackageName", "VariantCode", "Description", "IsActive",
   "CreatedDate", "CreatedBy", "CreatedActorKind", "ModifiedDate", "ModifiedBy", "ModifiedActorKind")
VALUES
  ('b0000000-0000-4000-8000-000000000001', :'division', 'PKG_CB',
   'Gói khám sức khoẻ cơ bản', NULL,
   'Khám lâm sàng tổng quát, xét nghiệm công thức máu, nước tiểu, X-quang ngực thẳng', true,
   now(), 0, 3, now(), 0, 3),
  ('b0000000-0000-4000-8000-000000000002', :'division', 'PKG_LX',
   'Gói khám sức khoẻ lái xe ô tô', 'DTK_06',
   'Theo Thông tư khám sức khoẻ người hành nghề lái xe ô tô, có xét nghiệm ma tuý và nồng độ cồn', true,
   now(), 0, 3, now(), 0, 3)
ON CONFLICT ("PackageID") DO NOTHING;

-- Dịch vụ trong gói. ServiceID/ServiceCode là CON TRỎ sang danh mục dịch vụ của HIS (DB khác,
-- không join được) — số ở đây là số mẫu cho DEV, không phải mã dịch vụ thật của bệnh viện nào.
INSERT INTO "HEX_ExamPackageService"
  ("PackageServiceID", "PackageID", "ServiceID", "ServiceCode", "ServiceName",
   "ParaclinicalKind", "ServiceGroupCode", "Quantity", "OrderNo", "IsActive",
   "CreatedDate", "CreatedBy", "CreatedActorKind", "ModifiedDate", "ModifiedBy", "ModifiedActorKind")
VALUES
  ('c0000000-0000-4000-8000-000000000001', 'b0000000-0000-4000-8000-000000000001',
   100001, 'XN_CTM', 'Tổng phân tích tế bào máu ngoại vi', 'XN', 'XN_HUYETHOC', 1, 1, true,
   now(), 0, 3, now(), 0, 3),
  ('c0000000-0000-4000-8000-000000000002', 'b0000000-0000-4000-8000-000000000001',
   100002, 'XN_NT', 'Tổng phân tích nước tiểu', 'XN', 'XN_SINHHOA', 1, 2, true,
   now(), 0, 3, now(), 0, 3),
  ('c0000000-0000-4000-8000-000000000003', 'b0000000-0000-4000-8000-000000000001',
   200001, 'CDHA_XQNT', 'Chụp X-quang ngực thẳng', 'CDHA', 'CDHA_XQUANG', 1, 3, true,
   now(), 0, 3, now(), 0, 3),
  ('c0000000-0000-4000-8000-000000000004', 'b0000000-0000-4000-8000-000000000002',
   100001, 'XN_CTM', 'Tổng phân tích tế bào máu ngoại vi', 'XN', 'XN_HUYETHOC', 1, 1, true,
   now(), 0, 3, now(), 0, 3),
  ('c0000000-0000-4000-8000-000000000005', 'b0000000-0000-4000-8000-000000000002',
   100003, 'XN_MT', 'Xét nghiệm ma tuý trong nước tiểu', 'XN', 'XN_SINHHOA', 1, 2, true,
   now(), 0, 3, now(), 0, 3),
  ('c0000000-0000-4000-8000-000000000006', 'b0000000-0000-4000-8000-000000000002',
   300001, 'TDCN_DTD', 'Điện tâm đồ thường', 'TDCN', 'TDCN_TIMMACH', 1, 3, true,
   now(), 0, 3, now(), 0, 3)
ON CONFLICT ("PackageServiceID") DO NOTHING;

-- ---------------------------------------------------------------------------------
-- 3. Đợt khám mẫu (1 đợt, trạng thái Đang mở = 1 để FE thêm hồ sơ được ngay)
--    OrganizationName/PackageName là ẢNH CHỤP — chép sẵn ở đây đúng như service sẽ làm.
-- ---------------------------------------------------------------------------------
INSERT INTO "HEX_ExamSession"
  ("SessionID", "DivisionID", "SessionCode", "SessionName",
   "OrganizationID", "OrganizationName", "ContractNo", "ContractDate",
   "ExamDate", "ExamDateTo", "ExamPlace", "DepartmentID",
   "PackageID", "PackageName", "VariantCode", "State", "ExpectedCount", "Note", "IsActive",
   "CreatedDate", "CreatedBy", "CreatedActorKind", "ModifiedDate", "ModifiedBy", "ModifiedActorKind")
VALUES
  ('d0000000-0000-4000-8000-000000000001', :'division', 'KSK2608-0001',
   'Khám sức khoẻ định kỳ 2026 — Công ty Nam Sơn',
   'a0000000-0000-4000-8000-000000000001', 'Công ty TNHH Thương mại Nam Sơn',
   'HD-2026/NS-01', DATE '2026-08-10',
   DATE '2026-08-26', DATE '2026-08-28', 'Tại đơn vị — 15 Nguyễn Văn Cừ, Quận 5', 0,
   'b0000000-0000-4000-8000-000000000001', 'Gói khám sức khoẻ cơ bản',
   'DTK_03', 1, 120, 'Đợt mẫu phục vụ ghép API ngày 26/8', true,
   now(), 0, 3, now(), 0, 3)
ON CONFLICT ("SessionID") DO NOTHING;

-- ---------------------------------------------------------------------------------
-- 4. Hồ sơ mẫu (3 người) — phủ 2 trạng thái đầu của máy trạng thái để FE dựng được bộ lọc:
--    2 hồ sơ Chưa đăng ký(0), 1 hồ sơ đã chốt đăng ký Chờ khám(1).
--    FormCode ghép theo quy ước KSK-V1-{VariantCode}; FormID/SubmissionID còn NULL vì
--    bước 2 (gọi form-server xin bản nháp) chưa làm ở phase này.
-- ---------------------------------------------------------------------------------
INSERT INTO "HEX_ExamRecord"
  ("RecordID", "DivisionID", "SessionID", "RecordCode",
   "PatientID", "PatientCode", "FullName", "Dob", "BirthYear", "GenderID",
   "IdentityNumber", "InsuranceNumber", "PhoneNumber", "Email", "Address",
   "StaffCode", "OrgDeptName", "JobTitle",
   "VariantCode", "PackageID", "PackageName", "FormID", "FormCode", "SubmissionID",
   "State", "RegisteredAt", "RegisteredBy", "CancelledBy", "CancelReason",
   "ProgressDone", "ProgressTotal", "HealthClassCode", "Note",
   "CreatedDate", "CreatedBy", "CreatedActorKind", "ModifiedDate", "ModifiedBy", "ModifiedActorKind")
VALUES
  ('e0000000-0000-4000-8000-000000000001', :'division', 'd0000000-0000-4000-8000-000000000001',
   'KSK2608-0001-0001',
   0, 'NB000001', 'Nguyễn Văn An', DATE '1988-03-12', 1988, 1,
   '079088001234', 'DN4790812345678', '0901234567', 'an.nv@namson.example',
   '12 Lý Thường Kiệt, Quận 10, TP.HCM',
   'NS-0125', 'Phòng Kinh doanh', 'Nhân viên kinh doanh',
   'DTK_03', 'b0000000-0000-4000-8000-000000000001', 'Gói khám sức khoẻ cơ bản',
   NULL, 'KSK-V1-DTK_03', NULL,
   0, NULL, 0, 0, '', 0, 0, '', NULL,
   now(), 0, 3, now(), 0, 3),
  ('e0000000-0000-4000-8000-000000000002', :'division', 'd0000000-0000-4000-8000-000000000001',
   'KSK2608-0001-0002',
   0, 'NB000002', 'Trần Thị Bích', DATE '1995-11-02', 1995, 2,
   '079095005678', '', '0912345678', 'bich.tt@namson.example',
   '48 Cách Mạng Tháng Tám, Quận 3, TP.HCM',
   'NS-0210', 'Phòng Kế toán', 'Kế toán viên',
   'DTK_03', 'b0000000-0000-4000-8000-000000000001', 'Gói khám sức khoẻ cơ bản',
   NULL, 'KSK-V1-DTK_03', NULL,
   1, now(), 0, 0, '', 0, 0, '', NULL,
   now(), 0, 3, now(), 0, 3),
  ('e0000000-0000-4000-8000-000000000003', :'division', 'd0000000-0000-4000-8000-000000000001',
   'KSK2608-0001-0003',
   0, 'NB000003', 'Lê Hoàng Cường', DATE '1979-06-25', 1979, 1,
   '079079009012', 'DN4790798765432', '0987654321', 'cuong.lh@namson.example',
   '9 Trường Chinh, Quận Tân Bình, TP.HCM',
   'NS-0033', 'Đội xe', 'Lái xe',
   'DTK_06', 'b0000000-0000-4000-8000-000000000002', 'Gói khám sức khoẻ lái xe ô tô',
   NULL, 'KSK-V1-DTK_06', NULL,
   0, NULL, 0, 0, '', 0, 0, '', 'Khám theo nhóm lái xe ô tô, khác nhóm mặc định của đợt',
   now(), 0, 3, now(), 0, 3)
ON CONFLICT ("RecordID") DO NOTHING;

-- ---------------------------------------------------------------------------------
-- 5. Danh mục mở rộng cho đăng ký KSK (HEX_MasterDataOption)
--    ⚠️ Dữ liệu địa giới hành chính (tỉnh/phường) ở đây là dữ liệu mẫu phục vụ phát triển/kiểm thử (mock),
--    KHÔNG phải danh mục quốc gia đầy đủ.
-- ---------------------------------------------------------------------------------
INSERT INTO "HEX_MasterDataOption"
  ("DivisionID", "Category", "Code", "Name", "ParentCode", "OrderNo", "IsActive",
   "CreatedDate", "CreatedBy", "CreatedActorKind", "ModifiedDate", "ModifiedBy", "ModifiedActorKind")
VALUES
  (:'division', 'ETHNICITY', 'KINH', 'Kinh', '', 1, true, now(), 0, 3, now(), 0, 3),
  (:'division', 'OCCUPATION', 'DRIVER', 'Lái xe', '', 1, true, now(), 0, 3, now(), 0, 3),
  (:'division', 'PROVINCE', '92', 'Cần Thơ', '', 1, true, now(), 0, 3, now(), 0, 3),
  (:'division', 'WARD', '26734', 'Phường An Cư', '92', 1, true, now(), 0, 3, now(), 0, 3),
  (:'division', 'IDENTITY_ISSUER', 'CAN_THO_POLICE', 'Công an Thành phố Cần Thơ', '', 1, true, now(), 0, 3, now(), 0, 3),
  (:'division', 'INSURANCE_OBJECT', 'FEE', 'Thu phí', '', 1, true, now(), 0, 3, now(), 0, 3),
  (:'division', 'INSURANCE_OBJECT', 'HI', 'Bảo hiểm y tế', '', 2, true, now(), 0, 3, now(), 0, 3),
  (:'division', 'PATIENT_TYPE', 'OUTPATIENT', 'Ngoại trú', '', 1, true, now(), 0, 3, now(), 0, 3),
  (:'division', 'PATIENT_SUBJECT', '01', 'Người lớn', '', 1, true, now(), 0, 3, now(), 0, 3),
  (:'division', 'PATIENT_SUBJECT', '02', 'Người cao tuổi', '', 2, true, now(), 0, 3, now(), 0, 3),
  (:'division', 'PATIENT_SUBJECT', '03', 'Trẻ em', '', 3, true, now(), 0, 3, now(), 0, 3),
  (:'division', 'REGISTRATION_PLACE', '01', 'Bệnh viện Bạch Mai', '', 1, true, now(), 0, 3, now(), 0, 3),
  (:'division', 'REGISTRATION_PLACE', '02', 'Bệnh viện Hữu nghị Việt Đức', '', 2, true, now(), 0, 3, now(), 0, 3),
  (:'division', 'REGISTRATION_PLACE', '03', 'Bệnh viện Đại học Y Hà Nội', '', 3, true, now(), 0, 3, now(), 0, 3),
  (:'division', 'REGISTRATION_PLACE', '04', 'Bệnh viện Đa khoa Xanh Pôn', '', 4, true, now(), 0, 3, now(), 0, 3),
  (:'division', 'REGISTRATION_PLACE', '05', 'Bệnh viện Thanh Nhàn', '', 5, true, now(), 0, 3, now(), 0, 3),
  (:'division', 'REGISTRATION_PLACE', '06', 'Bệnh viện Trung ương Huế', '', 6, true, now(), 0, 3, now(), 0, 3),
  (:'division', 'REGISTRATION_PLACE', '07', 'Bệnh viện Đà Nẵng', '', 7, true, now(), 0, 3, now(), 0, 3),
  (:'division', 'REGISTRATION_PLACE', '08', 'Bệnh viện Chợ Rẫy', '', 8, true, now(), 0, 3, now(), 0, 3),
  (:'division', 'REGISTRATION_PLACE', '09', 'Bệnh viện Nhân dân 115', '', 9, true, now(), 0, 3, now(), 0, 3),
  (:'division', 'REGISTRATION_PLACE', '10', 'Trung tâm Y tế quận/huyện', '', 10, true, now(), 0, 3, now(), 0, 3),
  (:'division', 'PAYMENT_SOURCE', 'STATE_CONTRACT', 'Ngân sách NN - Khám theo hợp đồng', '', 1, true, now(), 0, 3, now(), 0, 3),
  (:'division', 'PAYMENT_SOURCE', 'STATE_NON_LOCAL', 'Ngân sách NN - Khám phi địa giới', '', 2, true, now(), 0, 3, now(), 0, 3),
  (:'division', 'PAYMENT_SOURCE', 'SELF', 'Người dân tự chi trả', '', 3, true, now(), 0, 3, now(), 0, 3),
  (:'division', 'PAYMENT_SOURCE', 'OTHER', 'Nguồn khác', '', 4, true, now(), 0, 3, now(), 0, 3),
  (:'division', 'EXAM_LOCATION', 'CLINIC_3_F2', 'Phòng khám 3 - Tầng 2', '', 1, true, now(), 0, 3, now(), 0, 3)
ON CONFLICT ("DivisionID", "Category", "Code") DO UPDATE
SET "Name" = EXCLUDED."Name",
    "ParentCode" = EXCLUDED."ParentCode",
    "OrderNo" = EXCLUDED."OrderNo",
    "IsActive" = EXCLUDED."IsActive",
    "ModifiedDate" = now();

COMMIT;

-- Đối chiếu nhanh sau khi seed
SELECT 'HEX_Organization' AS "Bảng", count(*) FROM "HEX_Organization" WHERE "DivisionID" = :'division'
UNION ALL SELECT 'HEX_ExamPackage', count(*) FROM "HEX_ExamPackage" WHERE "DivisionID" = :'division'
UNION ALL SELECT 'HEX_ExamPackageService', count(*) FROM "HEX_ExamPackageService"
UNION ALL SELECT 'HEX_ExamSession', count(*) FROM "HEX_ExamSession" WHERE "DivisionID" = :'division'
UNION ALL SELECT 'HEX_ExamRecord', count(*) FROM "HEX_ExamRecord" WHERE "DivisionID" = :'division'
UNION ALL SELECT 'HEX_MasterDataOption', count(*) FROM "HEX_MasterDataOption" WHERE "DivisionID" = :'division';
