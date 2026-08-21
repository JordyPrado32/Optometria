using Microsoft.EntityFrameworkCore;
using OptometriaApp.Models;

namespace OptometriaApp.Data;

public partial class OpticaDbContext
{
    public virtual DbSet<tbl_notificacion> tbl_notificaciones { get; set; } = null!;

    public virtual DbSet<tbl_cobro_transferencia> tbl_cobro_transferencias { get; set; } = null!;

    public virtual DbSet<tbl_cobro_transferencia_detalle> tbl_cobro_transferencia_detalles { get; set; } = null!;

    internal static void ConfigureNotificationEntities(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<tbl_notificacion>(entity =>
        {
            entity.HasKey(e => e.id_notificacion).HasName("PK_tbl_notificacion");

            entity.ToTable("tbl_notificacion");

            entity.HasIndex(e => new { e.id_usuario_destino, e.leida, e.fecha_creacion }, "IX_tbl_notificacion_usuario_estado");

            entity.Property(e => e.entidad_tipo)
                .HasMaxLength(80)
                .IsUnicode(false);
            entity.Property(e => e.fecha_creacion).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.leida).HasDefaultValue(false);
            entity.Property(e => e.modulo_origen)
                .HasMaxLength(80)
                .IsUnicode(false);
            entity.Property(e => e.mensaje).IsUnicode(false);
            entity.Property(e => e.ruta_destino)
                .HasMaxLength(200)
                .IsUnicode(false);
            entity.Property(e => e.tipo)
                .HasMaxLength(40)
                .IsUnicode(false);
            entity.Property(e => e.titulo)
                .HasMaxLength(200)
                .IsUnicode(false);

            entity.HasOne(d => d.id_usuario_destinoNavigation).WithMany()
                .HasForeignKey(d => d.id_usuario_destino)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_tbl_notificacion_usuario_destino");

            entity.HasOne(d => d.id_usuario_origenNavigation).WithMany()
                .HasForeignKey(d => d.id_usuario_origen)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_tbl_notificacion_usuario_origen");
        });

        modelBuilder.Entity<tbl_cobro_transferencia>(entity =>
        {
            entity.HasKey(e => e.id_cobro_transferencia).HasName("PK_tbl_cobro_transferencia");

            entity.ToTable("tbl_cobro_transferencia", tableBuilder =>
            {
                tableBuilder.UseSqlOutputClause(false);
            });

            entity.HasIndex(e => new { e.estado, e.fecha_solicitud }, "IX_tbl_cobro_transferencia_estado");

            entity.Property(e => e.estado)
                .HasMaxLength(30)
                .IsUnicode(false)
                .HasDefaultValue("Pendiente");
            entity.Property(e => e.banco_origen)
                .HasMaxLength(120)
                .IsUnicode(false);
            entity.Property(e => e.cedula_titular)
                .HasMaxLength(40)
                .IsUnicode(false);
            entity.Property(e => e.fecha_solicitud).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.monto).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.nombre_titular)
                .HasMaxLength(180)
                .IsUnicode(false);
            entity.Property(e => e.mensaje_retiro).IsUnicode(false);
            entity.Property(e => e.observacion_resolucion).IsUnicode(false);
            entity.Property(e => e.observaciones).IsUnicode(false);
            entity.Property(e => e.referencia)
                .HasMaxLength(120)
                .IsUnicode(false);
            entity.Property(e => e.ruta_comprobante)
                .HasMaxLength(300)
                .IsUnicode(false);

            entity.HasOne(d => d.id_cta_cobrarNavigation).WithMany()
                .HasForeignKey(d => d.id_cta_cobrar)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_tbl_cobro_transferencia_cta_cobrar");

            entity.HasOne(d => d.id_comprobanteNavigation).WithMany()
                .HasForeignKey(d => d.id_comprobante)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_tbl_cobro_transferencia_comprobante");

            entity.HasOne(d => d.id_usuario_solicitaNavigation).WithMany()
                .HasForeignKey(d => d.id_usuario_solicita)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_tbl_cobro_transferencia_usuario_solicita");

            entity.HasOne(d => d.id_usuario_apruebaNavigation).WithMany()
                .HasForeignKey(d => d.id_usuario_aprueba)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_tbl_cobro_transferencia_usuario_aprueba");
        });

        modelBuilder.Entity<tbl_cobro_transferencia_detalle>(entity =>
        {
            entity.HasKey(e => e.id_cobro_transferencia_detalle).HasName("PK_tbl_cobro_transferencia_detalle");

            entity.ToTable("tbl_cobro_transferencia_detalle", tableBuilder =>
            {
                tableBuilder.UseSqlOutputClause(false);
            });

            entity.HasIndex(e => e.id_cobro_transferencia, "IX_tbl_cobro_transferencia_detalle_transferencia");
            entity.HasIndex(e => e.id_producto, "IX_tbl_cobro_transferencia_detalle_producto");

            entity.Property(e => e.fecha_creacion).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.nombre_producto_snapshot)
                .HasMaxLength(200)
                .IsUnicode(false);
            entity.Property(e => e.precio_unitario).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.total_item).HasColumnType("decimal(18, 2)");

            entity.HasOne(d => d.id_cobro_transferenciaNavigation).WithMany(p => p.tbl_cobro_transferencia_detalles)
                .HasForeignKey(d => d.id_cobro_transferencia)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_tbl_cobro_transferencia_detalle_transferencia");

            entity.HasOne(d => d.id_productoNavigation).WithMany(p => p.tbl_cobro_transferencia_detalles)
                .HasForeignKey(d => d.id_producto)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_tbl_cobro_transferencia_detalle_producto");
        });
    }
}
