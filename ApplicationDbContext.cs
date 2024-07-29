using apiFrankfurter.Entidades;
using Microsoft.EntityFrameworkCore;

namespace apiFrankfurter
{
        public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
        {
           

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                base.OnModelCreating(modelBuilder);

                modelBuilder.Entity<Currency>().HasKey(c => c.Id);
                modelBuilder.Entity<ExchangeRate>().HasKey(e => e.Id);
                modelBuilder.Entity<ExchangeRate>().Property(e => e.Rate).HasColumnType("decimal(18,6)");
            }

        public DbSet<Currency> Currencies { get; set; }
        public DbSet<ExchangeRate> ExchangeRates { get; set; }
        public DbSet<User> Users { get; set; }
    }
    }

