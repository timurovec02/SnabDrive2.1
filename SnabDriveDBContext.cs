using Microsoft.EntityFrameworkCore;
using SnabDrive2._0.Models;
namespace SnabDrive2._0
{
    public partial class SnabDriveDBContext : DbContext
    {
        public DbSet<Regedit> Regedit { get; set; }
        public DbSet<User> User { get; set; }
        public DbSet<ExecutionStatus> ExecutionStatus { get; set; }
        public DbSet<B2BStatus> B2BStatus { get; set; }
        public DbSet<TypeOfPurchase> TypeOfPurchase { get; set; }
        public DbSet<ArchiveRegedit> ArchiveRegedit { get; set; }
        public DbSet<CellColors> CellColors { get; set; }
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlServer("Data Source = 192.168.23.163,1433; Initial Catalog = SnabDriveDB; User ID = Admin; Password = 1234; Integrated Security = False; Encrypt = True; Trust Server Certificate = True");
        }
    }
}
