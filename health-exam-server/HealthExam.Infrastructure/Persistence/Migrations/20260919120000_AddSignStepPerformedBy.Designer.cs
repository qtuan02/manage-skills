using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore;
using HealthExam.Infrastructure.Persistence;

#nullable disable

namespace HealthExam.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(HealthExamDbContext))]
    [Migration("20260919120000_AddSignStepPerformedBy")]
    partial class AddSignStepPerformedBy
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
            // Để trống như tiền lệ 20260918110000 — `migrations add` tiếp theo vẫn đúng (đọc
            // HealthExamDbContextModelSnapshot), nhưng `migrations remove` và
            // `migrations script --idempotent` sẽ suy giảm với migration này.
        }
    }
}
