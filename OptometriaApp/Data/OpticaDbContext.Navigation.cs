using Microsoft.EntityFrameworkCore;
using OptometriaApp.Models;

namespace OptometriaApp.Data;

public partial class OpticaDbContext
{
    public virtual DbSet<ClientEntity> clients { get; set; } = null!;

    public virtual DbSet<EmisorEntity> emisor { get; set; } = null!;

    public virtual DbSet<tbl_menu_app> tbl_menu_apps { get; set; } = null!;

    public virtual DbSet<tbl_rol_menu_permiso> tbl_rol_menu_permisos { get; set; } = null!;

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        ConfigureSecurityEntities(modelBuilder);
        ConfigureNavigationEntities(modelBuilder);
        ConfigureElectronicBillingEntities(modelBuilder);
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

    internal static void ConfigureElectronicBillingEntities(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ClientEntity>(entity =>
        {
            entity.HasKey(e => e.cliente_id).HasName("PK_clients");

            entity.ToTable("clients");

            entity.HasIndex(e => e.estado, "IX_clients_estado");
            entity.HasIndex(e => e.numero_identificacion, "IX_clients_numero_identificacion");
            entity.HasIndex(e => e.razon_social, "IX_clients_razon_social");
            entity.HasIndex(e => new { e.id_usuario_creacion, e.tipo_identificacion, e.numero_identificacion }, "UQ_clients_usuario_identificacion").IsUnique();

            entity.Property(e => e.apellidos)
                .HasMaxLength(200)
                .IsUnicode(false);
            entity.Property(e => e.ciudad)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.codigo_postal)
                .HasMaxLength(10)
                .IsUnicode(false);
            entity.Property(e => e.condicion_pago)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.contacto_correo)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.contacto_nombre)
                .HasMaxLength(200)
                .IsUnicode(false);
            entity.Property(e => e.contacto_telefono)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.correo_electronico)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.direccion)
                .HasMaxLength(500)
                .IsUnicode(false);
            entity.Property(e => e.dias_plazo).HasDefaultValue(0);
            entity.Property(e => e.es_consumidor_final).HasDefaultValue(false);
            entity.Property(e => e.es_contribuyente_especial).HasDefaultValue(false);
            entity.Property(e => e.es_obligado_contabilidad).HasDefaultValue(false);
            entity.Property(e => e.es_residente_exterior).HasDefaultValue(false);
            entity.Property(e => e.estado).HasDefaultValue(true);
            entity.Property(e => e.fecha_actualizacion).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.fecha_creacion).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.limite_credito)
                .HasDefaultValue(0m)
                .HasColumnType("decimal(15, 2)");
            entity.Property(e => e.nombre_comercial)
                .HasMaxLength(300)
                .IsUnicode(false);
            entity.Property(e => e.nombres)
                .HasMaxLength(200)
                .IsUnicode(false);
            entity.Property(e => e.numero_contribuyente_especial)
                .HasMaxLength(10)
                .IsUnicode(false);
            entity.Property(e => e.numero_identificacion)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.observaciones)
                .HasMaxLength(500)
                .IsUnicode(false);
            entity.Property(e => e.pais_codigo)
                .HasMaxLength(2)
                .IsUnicode(false)
                .HasDefaultValue("EC");
            entity.Property(e => e.provincia)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.razon_social)
                .HasMaxLength(300)
                .IsUnicode(false);
            entity.Property(e => e.saldo_deudor)
                .HasDefaultValue(0m)
                .HasColumnType("decimal(15, 2)");
            entity.Property(e => e.telefono)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.tipo_cliente)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.tipo_identificacion)
                .HasMaxLength(2)
                .IsUnicode(false);

            entity.HasOne(d => d.id_usuario_creacionNavigation).WithMany()
                .HasForeignKey(d => d.id_usuario_creacion)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_clients_usuario_creacion");

            entity.HasOne(d => d.id_usuario_actualizacionNavigation).WithMany()
                .HasForeignKey(d => d.id_usuario_actualizacion)
                .HasConstraintName("FK_clients_usuario_actualizacion");
        });

        modelBuilder.Entity<EmisorEntity>(entity =>
        {
            entity.HasKey(e => e.emisor_id).HasName("PK_emisor");

            entity.ToTable("emisor");

            entity.HasIndex(e => e.estado, "IX_emisor_estado");
            entity.HasIndex(e => e.ruc, "UQ_emisor_ruc").IsUnique();
            entity.HasIndex(e => e.id_usuario_creacion, "UQ_emisor_usuario").IsUnique();

            entity.Property(e => e.cedula_representante)
                .HasMaxLength(10)
                .IsUnicode(false);
            entity.Property(e => e.ciudad)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.codigo_postal)
                .HasMaxLength(10)
                .IsUnicode(false);
            entity.Property(e => e.correo)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.direccion)
                .HasMaxLength(500)
                .IsUnicode(false);
            entity.Property(e => e.es_contribuyente_especial).HasDefaultValue(false);
            entity.Property(e => e.estado).HasDefaultValue(true);
            entity.Property(e => e.establecimiento_codigo)
                .HasMaxLength(3)
                .IsUnicode(false);
            entity.Property(e => e.fecha_actualizacion).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.fecha_creacion).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.nombre_comercial)
                .HasMaxLength(300)
                .IsUnicode(false);
            entity.Property(e => e.nombre_representante_legal)
                .HasMaxLength(300)
                .IsUnicode(false);
            entity.Property(e => e.numero_contribuyente_especial)
                .HasMaxLength(10)
                .IsUnicode(false);
            entity.Property(e => e.provincia)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.punto_emision_codigo)
                .HasMaxLength(3)
                .IsUnicode(false);
            entity.Property(e => e.razon_social)
                .HasMaxLength(300)
                .IsUnicode(false);
            entity.Property(e => e.ruc)
                .HasMaxLength(13)
                .IsUnicode(false);
            entity.Property(e => e.telefono)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.tipo_identificacion)
                .HasMaxLength(2)
                .IsUnicode(false);
            entity.Property(e => e.tipo_persona)
                .HasMaxLength(1)
                .IsUnicode(false);

            entity.HasOne(d => d.id_usuario_creacionNavigation).WithMany()
                .HasForeignKey(d => d.id_usuario_creacion)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_emisor_usuario_creacion");

            entity.HasOne(d => d.id_usuario_actualizacionNavigation).WithMany()
                .HasForeignKey(d => d.id_usuario_actualizacion)
                .HasConstraintName("FK_emisor_usuario_actualizacion");
        });
    }
}
