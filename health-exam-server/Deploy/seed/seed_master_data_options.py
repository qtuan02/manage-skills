#!/usr/bin/env python3
"""
Seed đầy đủ các danh mục Master Data còn thiếu cho màn hình Đăng ký KSK
(Dân tộc, Nghề nghiệp, Nơi cấp CCCD, Đối tượng bệnh nhân, Đối tượng BHYT, Địa điểm khám)
vào database emr_health_exam.
"""

import os
import psycopg2
from psycopg2.extras import execute_values
from datetime import datetime, timezone

BACKEND_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), "../../../"))
ENV_LOCAL_PATH = os.path.join(BACKEND_DIR, "env.local")

def load_env():
    env = {}
    if os.path.exists(ENV_LOCAL_PATH):
        with open(ENV_LOCAL_PATH, "r", encoding="utf-8") as f:
            for line in f:
                line = line.strip()
                if not line or line.startswith("#"):
                    continue
                if "=" in line:
                    k, v = line.split("=", 1)
                    env[k.strip()] = v.strip().strip("\"'")
    return env

# 1. 54 Dân tộc Việt Nam chuẩn Tổng cục Thống kê
ETHNICITIES = [
    ("KINH", "Kinh", 1),
    ("TAY", "Tày", 2),
    ("THAI", "Thái", 3),
    ("HOA", "Hoa", 4),
    ("KHMER", "Khmer", 5),
    ("MUONG", "Mường", 6),
    ("NUNG", "Nùng", 7),
    ("HMONG", "H'Mông (Mông)", 8),
    ("DAO", "Dao", 9),
    ("GIA_RAI", "Gia Rai", 10),
    ("NGAI", "Ngái", 11),
    ("E_DE", "Ê Đê", 12),
    ("BA_NA", "Ba Na", 13),
    ("XO_DANG", "Xơ Đăng", 14),
    ("SAN_CHAY", "Sán Chay", 15),
    ("CO_HO", "Cơ Ho", 16),
    ("CHAM", "Chăm", 17),
    ("SAN_DIU", "Sán Dìu", 18),
    ("HRE", "Hrê", 19),
    ("RA_GLAI", "Ra Glai", 20),
    ("MNONG", "M'Nông", 21),
    ("XTIENG", "X'Tiêng", 22),
    ("BRU_VAN_KIEU", "Bru-Vân Kiều", 23),
    ("THO", "Thổ", 24),
    ("GIAY", "Giáy", 25),
    ("CO_TU", "Cơ Tu", 26),
    ("GIE_TRIENG", "Giẻ Triêng", 27),
    ("MA", "Mạ", 28),
    ("KHO_MU", "Khơ Mú", 29),
    ("CO", "Co", 30),
    ("TA_OI", "Tà Ôi", 31),
    ("CHO_RO", "Chơ Ro", 32),
    ("KHANG", "Kháng", 33),
    ("XINH_MUN", "Xinh Mun", 34),
    ("HA_NHI", "Hà Nhì", 35),
    ("CHU_RU", "Chu Ru", 36),
    ("LAO", "Lào", 37),
    ("LA_CHI", "La Chí", 38),
    ("LA_HA", "La Ha", 39),
    ("PHU_LA", "Phù Lá", 40),
    ("LA_HU", "La Hủ", 41),
    ("LU", "Lự", 42),
    ("LO_LO", "Lô Lô", 43),
    ("CHUT", "Chứt", 44),
    ("MANG", "Mảng", 45),
    ("PA_THEN", "Pà Thẻn", 46),
    ("CO_LAO", "Cờ Lao", 47),
    ("CONG", "Cống", 48),
    ("BO_Y", "Bố Y", 49),
    ("SI_LA", "Si La", 50),
    ("PU_PEO", "Pu Péo", 51),
    ("RO_MAM", "Rơ Măm", 52),
    ("BRAU", "Brâu", 53),
    ("O_DU", "Ơ Đu", 54),
    ("OTHER", "Dân tộc khác", 55)
]

# 2. Nghề nghiệp phổ biến
OCCUPATIONS = [
    ("OFFICE", "Nhân viên văn phòng / Doanh nghiệp", 1),
    ("CIVIL_SERVANT", "Cán bộ / Công chức / Viên chức", 2),
    ("WORKER", "Công nhân / Lao động phổ thông", 3),
    ("FARMER", "Nông dân / Nông nghiệp", 4),
    ("BUSINESS", "Kinh doanh / Buôn bán", 5),
    ("STUDENT", "Học sinh / Sinh viên", 6),
    ("PUPIL", "Trẻ em (chưa đi học)", 7),
    ("RETIRED", "Hưu trí / Nghỉ hưu", 8),
    ("HOMEMAKER", "Nội trợ", 9),
    ("DRIVER", "Lái xe (Ô tô, Xe máy, Vận tải)", 10),
    ("SEAMAN", "Thuyền viên / Thủy thủ tàu biển", 11),
    ("TEACHER", "Giáo viên / Giảng viên", 12),
    ("HEALTHCARE", "Bác sĩ / Cán bộ Y tế", 13),
    ("ARMED_FORCES", "Lực lượng vũ trang (Quân đội, Công an)", 14),
    ("ENGINEER", "Kỹ sư / Kỹ thuật / Công nghệ", 15),
    ("FREELANCER", "Lao động tự do", 16),
    ("OTHER", "Nghề nghiệp khác", 17)
]

# 3. Nơi cấp CCCD / CMND
IDENTITY_ISSUERS = [
    ("CCS_QLHC_TTXH", "Cục Cảnh sát QLHC về TTXH (C06 - Bộ Công an)", 1),
    ("BOCONGAN", "Bộ Công an", 2),
    ("CAN_THO_POLICE", "Công an Thành phố Cần Thơ", 3),
    ("HA_NOI_POLICE", "Công an Thành phố Hà Nội", 4),
    ("HCM_POLICE", "Công an Thành phố Hồ Chí Minh", 5),
    ("DA_NANG_POLICE", "Công an Thành phố Đà Nẵng", 6),
    ("HAI_PHONG_POLICE", "Công an Thành phố Hải Phòng", 7),
    ("OTHER", "Công an tỉnh / thành phố khác", 8)
]

# 4. Loại đối tượng người bệnh
PATIENT_TYPES = [
    ("OUTPATIENT", "Ngoại trú", 1),
    ("INPATIENT", "Nội trú", 2),
    ("CONTRACT_GROUP", "Khám sức khỏe đoàn / Hợp đồng", 3),
    ("INDIVIDUAL", "Khám sức khỏe cá nhân", 4),
    ("EMERGENCY", "Cấp cứu", 5)
]

PATIENT_SUBJECTS = [
    ("01", "Người lớn", 1),
    ("02", "Người cao tuổi", 2),
    ("03", "Trẻ em", 3),
]

REGISTRATION_PLACES = [
    ("01", "Bệnh viện Bạch Mai", 1),
    ("02", "Bệnh viện Hữu nghị Việt Đức", 2),
    ("03", "Bệnh viện Đại học Y Hà Nội", 3),
    ("04", "Bệnh viện Đa khoa Xanh Pôn", 4),
    ("05", "Bệnh viện Thanh Nhàn", 5),
    ("06", "Bệnh viện Trung ương Huế", 6),
    ("07", "Bệnh viện Đà Nẵng", 7),
    ("08", "Bệnh viện Chợ Rẫy", 8),
    ("09", "Bệnh viện Nhân dân 115", 9),
    ("10", "Trung tâm Y tế quận/huyện", 10),
]

# 5. Đối tượng BHYT / Thanh toán
INSURANCE_OBJECTS = [
    ("FEE", "Thu phí (Viện phí)", 1),
    ("HI", "Bảo hiểm y tế", 2),
    ("FREE", "Miễn phí", 3),
    ("POLICY", "Chính sách / Người có công", 4),
    ("CHILD_UNDER_6", "Trẻ em dưới 6 tuổi", 5)
]

# 6. Địa điểm / Phòng khám
EXAM_LOCATIONS = [
    ("CLINIC_1_F1", "Phòng khám 1 — Tầng 1", 1),
    ("CLINIC_2_F1", "Phòng khám 2 — Tầng 1", 2),
    ("CLINIC_3_F2", "Phòng khám 3 — Tầng 2", 3),
    ("CLINIC_4_F2", "Phòng khám 4 — Tầng 2", 4),
    ("CLINIC_5_F3", "Phòng khám 5 — Tầng 3", 5),
    ("ONSITE", "Khám ngoại viện / Tại cơ quan, doanh nghiệp", 6)
]

def main():
    env = load_env()
    db_host = os.environ.get("DB_HOST") or env.get("DB_HOST", "10.43.163.85")
    db_port = int(os.environ.get("DB_PORT") or env.get("DB_PORT", 5432))
    db_user = os.environ.get("DB_USER") or env.get("DB_USER", "dhtesting_user")
    db_pass = os.environ.get("DB_PASSWORD") or env.get("DB_PASSWORD", "")
    db_name = os.environ.get("HEALTHEXAM_DB_NAME") or env.get("HEALTHEXAM_DB_NAME", "emr_health_exam")
    division = os.environ.get("DIVISION") or env.get("DIVISION", "DHTESTING")

    target_divisions = [division]
    if "DEV" not in target_divisions:
        target_divisions.append("DEV")

    print(f"-> Kết nối tới database '{db_name}' ({db_user}@{db_host}:{db_port})...")
    conn = psycopg2.connect(
        host=db_host,
        port=db_port,
        user=db_user,
        password=db_pass,
        dbname=db_name,
        connect_timeout=15
    )
    print("-> Kết nối thành công.")

    now = datetime.now(timezone.utc)
    upsert_sql = """
        INSERT INTO "HEX_MasterDataOption" (
            "DivisionID", "Category", "Code", "Name",
            "ParentCode", "OrderNo", "IsActive",
            "CreatedDate", "CreatedBy", "CreatedActorKind",
            "ModifiedDate", "ModifiedBy", "ModifiedActorKind"
        ) VALUES %s
        ON CONFLICT ("DivisionID", "Category", "Code") DO UPDATE
        SET "Name" = EXCLUDED."Name",
            "OrderNo" = EXCLUDED."OrderNo",
            "IsActive" = TRUE,
            "ModifiedDate" = EXCLUDED."ModifiedDate";
    """

    categories_to_seed = [
        ("ETHNICITY", ETHNICITIES),
        ("OCCUPATION", OCCUPATIONS),
        ("IDENTITY_ISSUER", IDENTITY_ISSUERS),
        ("PATIENT_TYPE", PATIENT_TYPES),
        ("PATIENT_SUBJECT", PATIENT_SUBJECTS),
        ("REGISTRATION_PLACE", REGISTRATION_PLACES),
        ("INSURANCE_OBJECT", INSURANCE_OBJECTS),
        ("EXAM_LOCATION", EXAM_LOCATIONS),
    ]

    for div in target_divisions:
        print(f"\n==================================================")
        print(f"-> Bổ sung Master Data cho DivisionID: '{div}'")
        print(f"==================================================")
        total_rows = 0
        for cat_name, items in categories_to_seed:
            rows = []
            for code, name, order_no in items:
                rows.append((
                    div, cat_name, code, name,
                    "", order_no, True,
                    now, 0, 3,
                    now, 0, 3
                ))
            with conn.cursor() as cur:
                execute_values(cur, upsert_sql, rows)
            total_rows += len(rows)
            print(f"   + Đã nạp {len(rows):>2} option cho danh mục: {cat_name}")
        conn.commit()
        print(f"-> Tổng cộng {total_rows} bản ghi đã cập nhật cho '{div}'.")

    print("\n==================================================")
    print("-> THỐNG KÊ TOÀN BỘ DANH MỤC TRONG DB (emr_health_exam):")
    print("==================================================")
    with conn.cursor() as cur:
        cur.execute("""
            SELECT "DivisionID", "Category", count(*)
            FROM "HEX_MasterDataOption"
            GROUP BY "DivisionID", "Category"
            ORDER BY "DivisionID", "Category";
        """)
        for div, cat, cnt in cur.fetchall():
            print(f"   [{div}] Category: {cat:<18} -> {cnt:>5} bản ghi")

    conn.close()
    print("\n-> Hoàn tất!")

if __name__ == "__main__":
    main()
