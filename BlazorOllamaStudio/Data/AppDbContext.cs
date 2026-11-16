using Microsoft.EntityFrameworkCore;

namespace BlazorOllamaStudio.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<DocumentoFragmento> Fragmentos => Set<DocumentoFragmento>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DocumentoFragmento>(entity =>
        {
            entity.ToTable("DocumentoFragmentos");
            entity.HasIndex(e => e.NombreArchivo);
        });
    }
}
