-- CHƯA ĐIỀN GIÁ TRỊ. Trước khi chạy, tra ItemGroupID và RoleID bằng hai lệnh sau (xem task-1-brief.md Step 8b):
-- 1) curl -s -H "Authorization: Bearer $HIS_TOKEN" "https://his.dhtesting.dhsc.vn/api/M03F00030/GetTreeTemplateByID?templateID=7f50a56d-c94b-4e0d-b403-37405d01d71a" | jq '.. | objects | select(has("ItemGroupID") and has("ItemGroupName")) | {ItemGroupID, ItemGroupName}'
-- 2) SWRoleID: cột Quyền của màn WFMF00030 (KSK02: "Bác sĩ Khám sức khỏe") -> tra RoleID của vai trò đó trong sAM_Roles
-- Điền các <...> bên dưới bằng giá trị thật rồi mới chạy tay sau `dotnet ef database update`.

INSERT INTO "HEX_SignStepMap"
  ("DivisionID","VariantCode","SWStep","ItemGroupID","StepName","SignTitle","SWRoleID",
   "SignType","SLType","SearchPattern","SLPage","SLX","SLY","IsConclusionStep","IsActive")
VALUES
  ('DIV01','KSK06-18T',2,<ItemGroupID thể lực>,'Bác sĩ khám thể lực','Bác sĩ khám thể lực',<RoleID>,1,2,'##{S2}##',0,0,0,false,true),
  ('DIV01','KSK06-18T',4,<ItemGroupID mắt>,'Bác sĩ khám mắt','Bác sĩ khám mắt',<RoleID>,1,2,'##{S4}##',0,0,0,false,true),
  ('DIV01','KSK06-18T',5,<ItemGroupID TMH>,'Bác sĩ khám tai-mũi-họng','Bác sĩ khám tai-mũi-họng',<RoleID>,1,2,'##{S5}##',0,0,0,false,true),
  ('DIV01','KSK06-18T',6,<ItemGroupID RHM>,'Bác sĩ khám răng-hàm-mặt','Bác sĩ khám răng-hàm-mặt',<RoleID>,1,2,'##{S6}##',0,0,0,false,true),
  ('DIV01','KSK06-18T',8,NULL,'Người kết luận','Người kết luận',<RoleID>,1,2,'##{S8}##',0,0,0,true,true)
ON CONFLICT ("DivisionID","VariantCode","SWStep") DO UPDATE SET
  "ItemGroupID" = EXCLUDED."ItemGroupID",
  "StepName" = EXCLUDED."StepName",
  "SignTitle" = EXCLUDED."SignTitle",
  "SWRoleID" = EXCLUDED."SWRoleID",
  "SearchPattern" = EXCLUDED."SearchPattern",
  "IsConclusionStep" = EXCLUDED."IsConclusionStep",
  "IsActive" = EXCLUDED."IsActive";
