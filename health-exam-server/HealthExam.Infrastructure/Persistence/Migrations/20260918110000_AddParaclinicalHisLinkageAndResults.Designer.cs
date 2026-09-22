using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore;
using HealthExam.Infrastructure.Persistence;

#nullable disable

namespace HealthExam.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(HealthExamDbContext))]
    [Migration("20260918110000_AddParaclinicalHisLinkageAndResults")]
    partial class AddParaclinicalHisLinkageAndResults
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
            // The migration operations are defined in the companion migration file.
        }
    }
}
