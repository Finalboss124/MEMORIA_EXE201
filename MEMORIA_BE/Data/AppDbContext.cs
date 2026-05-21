using Microsoft.EntityFrameworkCore;

namespace MEMORIA_BE.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }
}
