using Microsoft.EntityFrameworkCore;

namespace ZyrexMES.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    // Entitas ditambahkan pada task berikutnya.
}
