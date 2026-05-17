using Microsoft.EntityFrameworkCore;
using OptometriaApp.Models;

namespace OptometriaApp.Data;

public partial class OpticaDbContext
{
    public virtual DbSet<tbl_menu_app> tbl_menu_apps { get; set; } = null!;

    public virtual DbSet<tbl_rol_menu_permiso> tbl_rol_menu_permisos { get; set; } = null!;

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        ConfigureSecurityEntities(modelBuilder);
        ConfigureNavigationEntities(modelBuilder);
    }

    internal static void ConfigureNavigationEntities(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<tbl_menu_app>(entity =>
        {
            entity.HasKey(e => e.id_menu).HasName("PK_tbl_menu_app");

            entity.ToTable("tbl_menu_app");

            entity.HasIndex(e => e.ruta, "UQ_tbl_menu_app_ruta").IsUnique();

            entity.Property(e => e.activo).HasDefaultValue(true);
            entity.Property(e => e.fecha_creacion).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.icono)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.nombre)
                .HasMaxLength(150)
                .IsUnicode(false);
            entity.Property(e => e.orden).HasDefaultValue(0);
            entity.Property(e => e.ruta)
                .HasMaxLength(200)
                .IsUnicode(false);
        });

        modelBuilder.Entity<tbl_rol_menu_permiso>(entity =>
        {
            entity.HasKey(e => e.id_rol_menu_permiso).HasName("PK_tbl_rol_menu_permiso");

            entity.ToTable("tbl_rol_menu_permiso");

            entity.HasIndex(e => new { e.id_rol, e.id_menu }, "UQ_tbl_rol_menu_permiso_rol_menu").IsUnique();

            entity.Property(e => e.fecha_creacion).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.puede_crear).HasDefaultValue(false);
            entity.Property(e => e.puede_editar).HasDefaultValue(false);
            entity.Property(e => e.puede_eliminar).HasDefaultValue(false);
            entity.Property(e => e.puede_ver).HasDefaultValue(false);

            entity.HasOne(d => d.id_menuNavigation).WithMany(p => p.tbl_rol_menu_permisos)
                .HasForeignKey(d => d.id_menu)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_tbl_rol_menu_permiso_tbl_menu_app");

            entity.HasOne(d => d.id_rolNavigation).WithMany(p => p.tbl_rol_menu_permisos)
                .HasForeignKey(d => d.id_rol)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_tbl_rol_menu_permiso_tbl_rol");
        });
    }
}
