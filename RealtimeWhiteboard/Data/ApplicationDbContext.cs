using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using RealtimeWhiteboard.Models;

namespace RealtimeWhiteboard.Data
{
    // Updated to inherit from IdentityDbContext for ASP.NET Core Identity
    public class ApplicationDbContext : IdentityDbContext<IdentityUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<DrawingElement> DrawingElements { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder); // Important: Call base.OnModelCreating for Identity schema

            // Configure DrawingElement entity
            // Adding a composite index to potentially speed up loading session history
            modelBuilder.Entity<DrawingElement>()
                .HasIndex(de => new { de.SessionId, de.IsActive, de.CreatedAt })
                .HasDatabaseName("IX_DrawingElements_Session_Active_Created"); 
        }
    }
}
