using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore;
using HealthExam.Infrastructure.Persistence;

#nullable disable

namespace HealthExam.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(HealthExamDbContext))]
    [Migration("20260919031041_AddPatientAddressMasterCodes")]
    partial class AddPatientAddressMasterCodes
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
        }
    }
}
