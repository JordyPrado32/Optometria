using Microsoft.EntityFrameworkCore;
using OptometriaApp.Models;

namespace OptometriaApp.Data;

public partial class OpticaDbContext
{
    public virtual DbSet<tbl_usuario_seguridad> tbl_usuario_seguridad { get; set; } = null!;

    internal static void ConfigureSecurityEntities(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<tbl_usuario_seguridad>(entity =>
        {
            entity.HasKey(e => e.id_usuario).HasName("PK_tbl_usuario_seguridad");

            entity.ToTable("tbl_usuario_seguridad");

            entity.Property(e => e.authenticator_secret)
                .HasMaxLength(128)
                .IsUnicode(false);
            entity.Property(e => e.created_at).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.must_change_password).HasDefaultValue(false);
            entity.Property(e => e.recovery_password_expires_at).HasColumnType("datetime");
            entity.Property(e => e.recovery_password_hash)
                .HasMaxLength(255)
                .IsUnicode(false);
            entity.Property(e => e.two_factor_enabled).HasDefaultValue(false);
            entity.Property(e => e.updated_at).HasDefaultValueSql("(getdate())");

            entity.HasOne(d => d.id_usuarioNavigation).WithOne(p => p.tbl_usuario_seguridad)
                .HasForeignKey<tbl_usuario_seguridad>(d => d.id_usuario)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_tbl_usuario_seguridad_tbl_usuario");
        });
    }
}
