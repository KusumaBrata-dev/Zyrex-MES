using Microsoft.EntityFrameworkCore;

namespace ZyrexMES.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    // Entities are added in subsequent tasks.
}
