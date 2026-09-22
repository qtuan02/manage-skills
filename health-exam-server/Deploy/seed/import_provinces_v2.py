#!/usr/bin/env python3
"""
Import danh sách Tỉnh/Thành phố và Phường/Xã từ provinces.open-api.vn (v2)
vào bảng HEX_MasterDataOption của health-exam-server.

Nguồn dữ liệu:
  - Tỉnh/Thành phố: https://provinces.open-api.vn/api/v2/p/
  - Phường/Xã:     https://provinces.open-api.vn/api/v2/w/
"""

import json
import os
import sys
import time
import urllib.request
import uuid
from datetime import datetime, timezone
import psycopg2
from psycopg2.extras import execute_values

# Tìm file env.local
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


def fetch_json(url, timeout=60):
    req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0 (compatible; MedVietSync/1.0)"})
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        return json.loads(resp.read().decode("utf-8"))


def main():
    env = load_env()
    db_host = os.environ.get("DB_HOST") or env.get("DB_HOST", "10.43.163.85")
    db_port = int(os.environ.get("DB_PORT") or env.get("DB_PORT", 5432))
    db_user = os.environ.get("DB_USER") or env.get("DB_USER", "dhtesting_user")
    db_pass = os.environ.get("DB_PASSWORD") or env.get("DB_PASSWORD", "")
    db_name = os.environ.get("HEALTHEXAM_DB_NAME") or env.get("HEALTHEXAM_DB_NAME", "emr_health_exam")
    division = os.environ.get("DIVISION") or env.get("DIVISION", "DHTESTING")

    # Mặc định import cho division hiện tại và DEV (nếu khác)
    target_divisions = [division]
    if "DEV" not in target_divisions:
        target_divisions.append("DEV")

    print(f"-> Đang kết nối tới database '{db_name}' ({db_user}@{db_host}:{db_port})...")
    conn = None
    for attempt in range(1, 4):
        try:
            conn = psycopg2.connect(
                host=db_host,
                port=db_port,
                user=db_user,
                password=db_pass,
                dbname=db_name,
                connect_timeout=15,
            )
            print("-> Kết nối database thành công.")
            break
        except Exception as e:
            print(f"   [Thử lần {attempt}] Lỗi kết nối: {e}")
            if attempt == 3:
                raise
            time.sleep(2)

    print("-> Đang tải danh sách tỉnh/thành phố từ https://provinces.open-api.vn/api/v2/p/ ...")
    t0 = time.time()
    provinces = fetch_json("https://provinces.open-api.vn/api/v2/p/")
    print(f"   Đã tải {len(provinces)} tỉnh/thành ({time.time() - t0:.2f}s)")

    print("-> Đang tải danh sách phường/xã từ https://provinces.open-api.vn/api/v2/w/ ...")
    t0 = time.time()
    wards = fetch_json("https://provinces.open-api.vn/api/v2/w/")
    print(f"   Đã tải {len(wards)} phường/xã ({time.time() - t0:.2f}s)")

    now = datetime.now(timezone.utc)
    upsert_sql = """
        INSERT INTO "HEX_MasterDataOption" (
            "OptionID", "DivisionID", "Category", "Code", "Name",
            "ParentCode", "OrderNo", "IsActive",
            "CreatedDate", "CreatedBy", "CreatedActorKind",
            "ModifiedDate", "ModifiedBy", "ModifiedActorKind"
        ) VALUES %s
        ON CONFLICT ("DivisionID", "Category", "Code") DO UPDATE
        SET "Name" = EXCLUDED."Name",
            "ParentCode" = EXCLUDED."ParentCode",
            "OrderNo" = EXCLUDED."OrderNo",
            "IsActive" = TRUE,
            "ModifiedDate" = EXCLUDED."ModifiedDate";
    """

    for div in target_divisions:
        print(f"\n==================================================")
        print(f"-> Đang import dữ liệu cho DivisionID: '{div}'")
        print(f"==================================================")

        rows = []
        # Tỉnh/thành
        for p in provinces:
            p_code = str(p["code"])
            p_name = p["name"].strip()
            order_no = int(p["code"])
            rows.append((
                str(uuid.uuid4()), div, "PROVINCE", p_code, p_name,
                "", order_no, True,
                now, 0, 3,
                now, 0, 3
            ))

        # Phường/xã
        for w in wards:
            w_code = str(w["code"])
            w_name = w["name"].strip()
            prov_code = str(w.get("province_code", ""))
            order_no = int(w["code"])
            rows.append((
                str(uuid.uuid4()), div, "WARD", w_code, w_name,
                prov_code, order_no, True,
                now, 0, 3,
                now, 0, 3
            ))

        print(f"-> Đang ghi {len(rows)} bản ghi ({len(provinces)} tỉnh + {len(wards)} xã) vào database...")
        t_start = time.time()
        with conn.cursor() as cur:
            execute_values(cur, upsert_sql, rows, page_size=1000)
        conn.commit()
        print(f"-> Đã ghi thành công cho tenant '{div}' trong {time.time() - t_start:.2f}s!")

    print("\n==================================================")
    print("-> TỔNG KẾT DỮ LIỆU SAU IMPORT:")
    print("==================================================")
    with conn.cursor() as cur:
        cur.execute("""
            SELECT "DivisionID", "Category", count(*)
            FROM "HEX_MasterDataOption"
            WHERE "Category" IN ('PROVINCE', 'WARD')
            GROUP BY "DivisionID", "Category"
            ORDER BY "DivisionID", "Category";
        """)
        for div, cat, cnt in cur.fetchall():
            print(f"   [{div}] Category: {cat:<10} -> {cnt:>5} bản ghi")

    conn.close()
    print("\n-> Hoàn tất import!")


if __name__ == "__main__":
    main()
