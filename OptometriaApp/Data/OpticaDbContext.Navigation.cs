using Microsoft.EntityFrameworkCore;
using OptometriaApp.Models;

namespace OptometriaApp.Data;

public partial class OpticaDbContext
{
    public virtual DbSet<ClientEntity> clients { get; set; } = null!;

    public virtual DbSet<EmisorEntity> emisor { get; set; } = null!;

    public virtual DbSet<tbl_lote_producto> tbl_lote_producto { get; set; } = null!;

    public virtual DbSet<tbl_orden_compra> tbl_orden_compra { get; set; } = null!;

    public virtual DbSet<tbl_detalle_orden_compra> tbl_detalle_orden_compra { get; set; } = null!;

    public virtual DbSet<tbl_recepcion_compra> tbl_recepcion_compra { get; set; } = null!;

    public virtual DbSet<tbl_kardex> tbl_kardex { get; set; } = null!;

    public virtual DbSet<tbl_liquidacion_compra> tbl_liquidacion_compra { get; set; } = null!;

    public virtual DbSet<tbl_menu_app> tbl_menu_apps { get; set; } = null!;

    public virtual DbSet<tbl_rol_menu_permiso> tbl_rol_menu_permisos { get; set; } = null!;

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        ConfigureSecurityEntities(modelBuilder);
        ConfigureNavigationEntities(modelBuilder);
        ConfigureElectronicBillingEntities(modelBuilder);
        ConfigureProcurementEntities(modelBuilder);
        ConfigureAppointmentEntities(modelBuilder);
    }

    internal static void ConfigureNavigationEntities(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<tbl_menu_app>(entity =>
        {
            entity.HasKey(e => e.id_menu).HasName("PK_tbl_menu_app");

            entity.ToTable("tbl_menu_app");

            entity.HasIndex(e => e.ruta, "IX_tbl_menu_app_ruta_not_null")
                .IsUnique()
                .HasFilter("[ruta] IS NOT NULL AND [ruta] <> ''");

            entity.Property(e => e.activo).HasDefaultValue(true);
            entity.Property(e => e.fecha_creacion).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.icono)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.nombre)
                .HasMaxLength(150)
                .IsUnicode(false);
            entity.Property(e => e.id_menu_padre);
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
            entity.Property(e => e.ambiente_codigo)
                .HasMaxLength(1)
                .IsUnicode(false)
                .HasDefaultValue("1");
            entity.Property(e => e.certificado_digital_clave)
                .HasMaxLength(255)
                .IsUnicode(false);
            entity.Property(e => e.certificado_digital_ruta)
                .HasMaxLength(500)
                .IsUnicode(false);
            entity.Property(e => e.codigo_postal)
                .HasMaxLength(10)
                .IsUnicode(false);
            entity.Property(e => e.correo)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.direccion_establecimiento)
                .HasMaxLength(500)
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
            entity.Property(e => e.regimen_rimpe)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.ruc)
                .HasMaxLength(13)
                .IsUnicode(false);
            entity.Property(e => e.telefono)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.tipo_emision_codigo)
                .HasMaxLength(1)
                .IsUnicode(false)
                .HasDefaultValue("1");
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

    internal static void ConfigureProcurementEntities(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<tbl_lote_producto>(entity =>
        {
            entity.HasKey(e => e.id_lote).HasName("PK_tbl_lote_producto");
            entity.ToTable("tbl_lote_producto");
            entity.HasIndex(e => e.fecha_vencimiento, "IX_lote_vencimiento");
            entity.HasIndex(e => e.estado_lote, "IX_lote_estado");
            entity.HasIndex(e => e.id_producto, "IX_lote_producto");
            entity.HasIndex(e => new { e.numero_lote, e.id_producto }, "UQ_lote_numero").IsUnique();
            entity.Property(e => e.numero_lote).HasMaxLength(50).IsUnicode(false);
            entity.Property(e => e.numero_serie).HasMaxLength(50).IsUnicode(false);
            entity.Property(e => e.cantidad_vendida).HasDefaultValue(0);
            entity.Property(e => e.cantidad_devuelta).HasDefaultValue(0);
            entity.Property(e => e.cantidad_merma).HasDefaultValue(0);
            entity.Property(e => e.costo_unitario).HasColumnType("decimal(15, 2)");
            entity.Property(e => e.precio_venta_unitario).HasColumnType("decimal(15, 2)");
            entity.Property(e => e.valor_total_costo).HasColumnType("decimal(15, 2)");
            entity.Property(e => e.estado_lote).HasMaxLength(30).IsUnicode(false).HasDefaultValue("Disponible");
            entity.Property(e => e.almacen).HasMaxLength(50).IsUnicode(false);
            entity.Property(e => e.pasillo).HasMaxLength(10).IsUnicode(false);
            entity.Property(e => e.estante).HasMaxLength(10).IsUnicode(false);
            entity.Property(e => e.nivel).HasMaxLength(10).IsUnicode(false);
            entity.Property(e => e.fecha_ingreso).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.observaciones).HasMaxLength(500).IsUnicode(false);

            entity.HasOne(d => d.id_productoNavigation).WithMany(p => p.tbl_lote_producto)
                .HasForeignKey(d => d.id_producto)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_lote_producto");

            entity.HasOne(d => d.id_orden_compraNavigation).WithMany(p => p.tbl_lote_producto)
                .HasForeignKey(d => d.id_orden_compra)
                .HasConstraintName("FK_lote_orden_compra");

            entity.HasOne(d => d.id_usuario_ingresoNavigation).WithMany()
                .HasForeignKey(d => d.id_usuario_ingreso)
                .HasConstraintName("FK_lote_usuario_ingreso");
        });

        modelBuilder.Entity<tbl_orden_compra>(entity =>
        {
            entity.HasKey(e => e.id_orden_compra).HasName("PK_tbl_orden_compra");
            entity.ToTable("tbl_orden_compra");
            entity.HasIndex(e => e.numero_orden, "IX_orden_numero");
            entity.HasIndex(e => e.estado_orden, "IX_orden_estado");
            entity.HasIndex(e => e.id_proveedor, "IX_orden_proveedor");
            entity.HasIndex(e => e.fecha_orden, "IX_orden_fecha");
            entity.HasIndex(e => e.numero_orden, "UQ_tbl_orden_compra_numero").IsUnique();
            entity.Property(e => e.numero_orden).HasMaxLength(20).IsUnicode(false);
            entity.Property(e => e.fecha_orden).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.subtotal).HasDefaultValue(0m).HasColumnType("decimal(15, 2)");
            entity.Property(e => e.descuento_general).HasDefaultValue(0m).HasColumnType("decimal(15, 2)");
            entity.Property(e => e.impuesto_total).HasDefaultValue(0m).HasColumnType("decimal(15, 2)");
            entity.Property(e => e.total).HasDefaultValue(0m).HasColumnType("decimal(15, 2)");
            entity.Property(e => e.condicion_pago).HasMaxLength(50).IsUnicode(false);
            entity.Property(e => e.moneda).HasMaxLength(3).IsUnicode(false).HasDefaultValue("USD");
            entity.Property(e => e.tasa_cambio).HasColumnType("decimal(10, 6)");
            entity.Property(e => e.estado_orden).HasMaxLength(30).IsUnicode(false).HasDefaultValue("Pendiente");
            entity.Property(e => e.tipo_orden).HasMaxLength(20).IsUnicode(false).HasDefaultValue("Compra");
            entity.Property(e => e.referencia_externa).HasMaxLength(100).IsUnicode(false);
            entity.Property(e => e.observaciones).IsUnicode(false);
            entity.Property(e => e.activo).HasDefaultValue(true);
            entity.Property(e => e.fecha_creacion).HasDefaultValueSql("(getdate())");

            entity.HasOne(d => d.id_proveedorNavigation).WithMany(p => p.tbl_orden_compra)
                .HasForeignKey(d => d.id_proveedor)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_orden_proveedor");

            entity.HasOne(d => d.id_usuario_solicitaNavigation).WithMany()
                .HasForeignKey(d => d.id_usuario_solicita)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_orden_usuario_solicita");

            entity.HasOne(d => d.id_usuario_autorizaNavigation).WithMany()
                .HasForeignKey(d => d.id_usuario_autoriza)
                .HasConstraintName("FK_orden_usuario_autoriza");
        });

        modelBuilder.Entity<tbl_detalle_orden_compra>(entity =>
        {
            entity.HasKey(e => e.id_detalle_orden_compra).HasName("PK_tbl_detalle_orden_compra");
            entity.ToTable("tbl_detalle_orden_compra");
            entity.HasIndex(e => e.id_orden_compra, "IX_detalle_orden");
            entity.HasIndex(e => e.id_producto, "IX_detalle_producto");
            entity.Property(e => e.precio_unitario).HasColumnType("decimal(15, 2)");
            entity.Property(e => e.precio_total_linea).HasColumnType("decimal(15, 2)");
            entity.Property(e => e.descuento_linea).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.impuesto_linea).HasColumnType("decimal(15, 2)");
            entity.Property(e => e.codigo_fiscal_fe).HasMaxLength(10).IsUnicode(false);
            entity.Property(e => e.unidad_medida_fe).HasMaxLength(10).IsUnicode(false);
            entity.Property(e => e.estado_linea).HasMaxLength(30).IsUnicode(false).HasDefaultValue("Pendiente");
            entity.Property(e => e.observaciones).HasMaxLength(500).IsUnicode(false);

            entity.HasOne(d => d.id_orden_compraNavigation).WithMany(p => p.tbl_detalle_orden_compra)
                .HasForeignKey(d => d.id_orden_compra)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_detalle_orden");

            entity.HasOne(d => d.id_productoNavigation).WithMany(p => p.tbl_detalle_orden_compra)
                .HasForeignKey(d => d.id_producto)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_detalle_producto");

            entity.HasOne(d => d.id_loteNavigation).WithMany(p => p.tbl_detalle_orden_compra)
                .HasForeignKey(d => d.id_lote)
                .HasConstraintName("FK_detalle_lote");
        });

        modelBuilder.Entity<tbl_recepcion_compra>(entity =>
        {
            entity.HasKey(e => e.id_recepcion).HasName("PK_tbl_recepcion_compra");
            entity.ToTable("tbl_recepcion_compra");
            entity.HasIndex(e => e.numero_recepcion, "IX_recepcion_numero");
            entity.HasIndex(e => e.id_orden_compra, "IX_recepcion_orden");
            entity.HasIndex(e => e.fecha_recepcion, "IX_recepcion_fecha");
            entity.HasIndex(e => e.numero_recepcion, "UQ_tbl_recepcion_compra_numero").IsUnique();
            entity.Property(e => e.numero_recepcion).HasMaxLength(20).IsUnicode(false);
            entity.Property(e => e.numero_guia_remision).HasMaxLength(30).IsUnicode(false);
            entity.Property(e => e.fecha_recepcion).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.observaciones_recepcion).IsUnicode(false);
            entity.Property(e => e.estado_recepcion).HasMaxLength(30).IsUnicode(false).HasDefaultValue("Completa");
            entity.Property(e => e.activo).HasDefaultValue(true);

            entity.HasOne(d => d.id_orden_compraNavigation).WithMany(p => p.tbl_recepcion_compra)
                .HasForeignKey(d => d.id_orden_compra)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_recepcion_orden");

            entity.HasOne(d => d.id_usuario_recibeNavigation).WithMany()
                .HasForeignKey(d => d.id_usuario_recibe)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_recepcion_usuario");
        });

        modelBuilder.Entity<tbl_kardex>(entity =>
        {
            entity.HasKey(e => e.id_kardex).HasName("PK_tbl_kardex");
            entity.ToTable("tbl_kardex", tableBuilder =>
            {
                tableBuilder.HasTrigger("TR_tbl_kardex");
                tableBuilder.UseSqlOutputClause(false);
            });
            entity.HasIndex(e => e.id_producto, "IX_kardex_producto");
            entity.HasIndex(e => e.fecha_movimiento, "IX_kardex_fecha");
            entity.HasIndex(e => e.tipo_movimiento, "IX_kardex_tipo");
            entity.HasIndex(e => e.id_lote, "IX_kardex_lote");
            entity.Property(e => e.numero_lote).HasMaxLength(50).IsUnicode(false);
            entity.Property(e => e.fecha_movimiento).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.tipo_movimiento).HasMaxLength(30).IsUnicode(false);
            entity.Property(e => e.tipo_referencia).HasMaxLength(30).IsUnicode(false);
            entity.Property(e => e.comprobante_numero).HasMaxLength(50).IsUnicode(false);
            entity.Property(e => e.costo_unitario).HasColumnType("decimal(15, 2)");
            entity.Property(e => e.costo_total).HasColumnType("decimal(15, 2)");
            entity.Property(e => e.saldo_anterior_dinero).HasColumnType("decimal(15, 2)");
            entity.Property(e => e.saldo_nuevo_dinero).HasColumnType("decimal(15, 2)");
            entity.Property(e => e.precio_promedio_ponderado).HasColumnType("decimal(15, 2)");
            entity.Property(e => e.metodo_valuacion).HasMaxLength(30).IsUnicode(false).HasDefaultValue("Promedio");
            entity.Property(e => e.descripcion_movimiento).HasMaxLength(500).IsUnicode(false);
            entity.Property(e => e.glosa_contable).HasMaxLength(255).IsUnicode(false);
            entity.Property(e => e.cuenta_contable_debito).HasMaxLength(20).IsUnicode(false);
            entity.Property(e => e.cuenta_contable_credito).HasMaxLength(20).IsUnicode(false);
            entity.Property(e => e.centro_costo).HasMaxLength(20).IsUnicode(false);
            entity.Property(e => e.estado_kardex).HasMaxLength(20).IsUnicode(false).HasDefaultValue("Registrado");
            entity.Property(e => e.observaciones).IsUnicode(false);
            entity.Property(e => e.fecha_creacion).HasDefaultValueSql("(getdate())");

            entity.HasOne(d => d.id_productoNavigation).WithMany(p => p.tbl_kardex)
                .HasForeignKey(d => d.id_producto)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_kardex_producto");

            entity.HasOne(d => d.id_loteNavigation).WithMany(p => p.tbl_kardex)
                .HasForeignKey(d => d.id_lote)
                .HasConstraintName("FK_kardex_lote");

            entity.HasOne(d => d.id_usuario_movimientoNavigation).WithMany()
                .HasForeignKey(d => d.id_usuario_movimiento)
                .HasConstraintName("FK_kardex_usuario");
        });

        modelBuilder.Entity<tbl_liquidacion_compra>(entity =>
        {
            entity.HasKey(e => e.id_liquidacion_compra).HasName("PK_tbl_liquidacion_compra");
            entity.ToTable("tbl_liquidacion_compra");
            entity.HasIndex(e => e.numero_liquidacion, "UQ_tbl_liquidacion_compra_numero").IsUnique();
            entity.Property(e => e.numero_liquidacion).HasMaxLength(20).IsUnicode(false);
            entity.Property(e => e.fecha_liquidacion).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.numero_factura).HasMaxLength(50).IsUnicode(false);
            entity.Property(e => e.numero_autorizacion).HasMaxLength(100).IsUnicode(false);
            entity.Property(e => e.subtotal).HasDefaultValue(0m).HasColumnType("decimal(15, 2)");
            entity.Property(e => e.descuento_total).HasDefaultValue(0m).HasColumnType("decimal(15, 2)");
            entity.Property(e => e.impuesto_total).HasDefaultValue(0m).HasColumnType("decimal(15, 2)");
            entity.Property(e => e.total).HasDefaultValue(0m).HasColumnType("decimal(15, 2)");
            entity.Property(e => e.saldo_pagado).HasDefaultValue(0m).HasColumnType("decimal(15, 2)");
            entity.Property(e => e.saldo_pendiente).HasDefaultValue(0m).HasColumnType("decimal(15, 2)");
            entity.Property(e => e.estado_liquidacion).HasMaxLength(30).IsUnicode(false).HasDefaultValue("Pendiente");
            entity.Property(e => e.observaciones).IsUnicode(false);
            entity.Property(e => e.activo).HasDefaultValue(true);
            entity.Property(e => e.fecha_creacion).HasDefaultValueSql("(getdate())");

            entity.HasOne(d => d.id_orden_compraNavigation).WithMany(p => p.tbl_liquidacion_compra)
                .HasForeignKey(d => d.id_orden_compra)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_liquidacion_orden_compra");

            entity.HasOne(d => d.id_usuario_registroNavigation).WithMany()
                .HasForeignKey(d => d.id_usuario_registro)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_liquidacion_usuario_registro");
        });
    }
}
