using Microsoft.EntityFrameworkCore;

namespace Atrium.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
}
