using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore;
using HealthExam.Infrastructure.Persistence;

#nullable disable

namespace HealthExam.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(HealthExamDbContext))]
    [Migration("20260917094500_AddExamRecordSignSteps")]
    partial class AddExamRecordSignSteps
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
            // The migration operations are defined in the companion migration file.
        }
    }
}
