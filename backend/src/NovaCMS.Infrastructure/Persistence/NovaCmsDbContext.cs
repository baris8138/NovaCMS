using Microsoft.EntityFrameworkCore;

namespace NovaCMS.Infrastructure.Persistence;

public sealed class NovaCmsDbContext(DbContextOptions<NovaCmsDbContext> options)
    : DbContext(options);
