using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using OptometriaApp.Models;

namespace OptometriaApp.Data;

public partial class OpticaDbContext : DbContext
{
    public OpticaDbContext(DbContextOptions<OpticaDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<tbl_abono> tbl_abonos { get; set; }

    public virtual DbSet<tbl_archivo_consulta> tbl_archivo_consulta { get; set; }

    public virtual DbSet<tbl_categoria_producto> tbl_categoria_productos { get; set; }

    public virtual DbSet<tbl_comprobante> tbl_comprobantes { get; set; }

    public virtual DbSet<tbl_comunicacion> tbl_comunicacions { get; set; }

    public virtual DbSet<tbl_configuracion_optica> tbl_configuracion_opticas { get; set; }

    public virtual DbSet<tbl_consulta> tbl_consulta { get; set; }

    public virtual DbSet<tbl_cta_cobrar> tbl_cta_cobrar { get; set; }

    public virtual DbSet<tbl_detalle_venta> tbl_detalle_venta { get; set; }

    public virtual DbSet<tbl_envio_laboratorio> tbl_envio_laboratorios { get; set; }

    public virtual DbSet<tbl_laboratorio> tbl_laboratorios { get; set; }

    public virtual DbSet<tbl_log_auditoria> tbl_log_auditoria { get; set; }

    public virtual DbSet<tbl_metodo_pago> tbl_metodo_pagos { get; set; }

    public virtual DbSet<tbl_movimiento_inventario> tbl_movimiento_inventarios { get; set; }

    public virtual DbSet<tbl_nota_credito> tbl_nota_credito { get; set; }

    public virtual DbSet<tbl_orden_rx> tbl_orden_rxes { get; set; }

    public virtual DbSet<tbl_paciente> tbl_pacientes { get; set; }

    public virtual DbSet<tbl_plantilla_mensaje> tbl_plantilla_mensajes { get; set; }

    public virtual DbSet<tbl_producto> tbl_productos { get; set; }

    public virtual DbSet<tbl_proveedor> tbl_proveedors { get; set; }

    public virtual DbSet<tbl_rol> tbl_rols { get; set; }

    public virtual DbSet<tbl_rx_contactologia> tbl_rx_contactologia { get; set; }

    public virtual DbSet<tbl_rx_lente> tbl_rx_lentes { get; set; }

    public virtual DbSet<tbl_sesion> tbl_sesions { get; set; }

    public virtual DbSet<tbl_usuario> tbl_usuarios { get; set; }

    public virtual DbSet<tbl_venta> tbl_venta { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<tbl_abono>(entity =>
        {
            entity.HasKey(e => e.id_abono).HasName("PK__tbl_abon__1E6B958340D1E4B9");

            entity.ToTable("tbl_abono");

            entity.HasIndex(e => e.id_cta_cobrar, "IX_abono_cta");

            entity.Property(e => e.fecha_abono).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.fecha_registro).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.monto_abono).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.referencia_pago)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.usuario_registro)
                .HasMaxLength(100)
                .IsUnicode(false);

            entity.HasOne(d => d.id_cta_cobrarNavigation).WithMany()
                .HasForeignKey(d => d.id_cta_cobrar)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_tbl_abono_tbl_cta_cobrar");

            entity.HasOne(d => d.metodo_pagoNavigation).WithMany()
                .HasForeignKey(d => d.metodo_pago_id)
                .HasConstraintName("FK_tbl_abono_tbl_metodo_pago");
        });

        modelBuilder.Entity<tbl_archivo_consulta>(entity =>
        {
            entity.HasKey(e => e.id_archivo_consulta).HasName("PK__tbl_arch__5E1572429D883D79");

            entity.Property(e => e.fecha_subida).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.nombre_original)
                .HasMaxLength(255)
                .IsUnicode(false);
            entity.Property(e => e.ruta_archivo)
                .HasMaxLength(255)
                .IsUnicode(false);
            entity.Property(e => e.tipo_archivo)
                .HasMaxLength(50)
                .IsUnicode(false);

            entity.HasOne(d => d.id_consultaNavigation).WithMany(p => p.tbl_archivo_consulta)
                .HasForeignKey(d => d.id_consulta)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_archivo_consulta");
        });

        modelBuilder.Entity<tbl_categoria_producto>(entity =>
        {
            entity.HasKey(e => e.id_categoria).HasName("PK__tbl_cate__CD54BC5AD7D0A38B");

            entity.ToTable("tbl_categoria_producto");

            entity.Property(e => e.descripcion).IsUnicode(false);
            entity.Property(e => e.activo).HasDefaultValue(true);
            entity.Property(e => e.nombre)
                .HasMaxLength(100)
                .IsUnicode(false);
        });

        modelBuilder.Entity<tbl_comprobante>(entity =>
        {
            entity.HasKey(e => e.id_comprobante).HasName("PK__tbl_comp__55E5E240429C96FF");

            entity.ToTable("tbl_comprobante");

            entity.HasIndex(e => e.numero_comprobante, "UQ__tbl_comp__1850D80D58238795").IsUnique();

            entity.Property(e => e.fecha_emision).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.fecha_autorizacion).HasColumnType("datetime2");
            entity.Property(e => e.numero_comprobante)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.numero_autorizacion)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.estado_comprobante)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.tipo_comprobante)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.ruta_pdf)
                .HasMaxLength(255)
                .IsUnicode(false);

            entity.HasOne(d => d.id_ventaNavigation).WithMany(p => p.tbl_comprobantes)
                .HasForeignKey(d => d.id_venta)
                .HasConstraintName("fk_comprobante_venta");
        });

        modelBuilder.Entity<tbl_comunicacion>(entity =>
        {
            entity.HasKey(e => e.id_comunicacion).HasName("PK__tbl_comu__D76C507105C0737B");

            entity.ToTable("tbl_comunicacion");

            entity.Property(e => e.canal)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.contenido_resumen).IsUnicode(false);
            entity.Property(e => e.destinatario)
                .HasMaxLength(150)
                .IsUnicode(false);
            entity.Property(e => e.fecha_envio).HasDefaultValueSql("(getdate())");

            entity.HasOne(d => d.id_orden_rxNavigation).WithMany(p => p.tbl_comunicacions)
                .HasForeignKey(d => d.id_orden_rx)
                .HasConstraintName("fk_comunicacion_orden");

            entity.HasOne(d => d.id_pacienteNavigation).WithMany(p => p.tbl_comunicacions)
                .HasForeignKey(d => d.id_paciente)
                .HasConstraintName("fk_comunicacion_paciente");

            entity.HasOne(d => d.id_plantilla_mensajeNavigation).WithMany(p => p.tbl_comunicacions)
                .HasForeignKey(d => d.id_plantilla_mensaje)
                .HasConstraintName("fk_comunicacion_plantilla");

            entity.HasOne(d => d.id_usuarioNavigation).WithMany(p => p.tbl_comunicacions)
                .HasForeignKey(d => d.id_usuario)
                .HasConstraintName("fk_comunicacion_usuario");
        });

        modelBuilder.Entity<tbl_configuracion_optica>(entity =>
        {
            entity.HasKey(e => e.id_configuracion).HasName("PK__tbl_conf__16A13EBDF44ECBFE");

            entity.ToTable("tbl_configuracion_optica");

            entity.Property(e => e.carpeta_rx)
                .HasMaxLength(255)
                .IsUnicode(false);
            entity.Property(e => e.direccion)
                .HasMaxLength(255)
                .IsUnicode(false);
            entity.Property(e => e.nombre_comercial)
                .HasMaxLength(150)
                .IsUnicode(false);
            entity.Property(e => e.porcentaje_impuesto)
                .HasDefaultValue(0.00m)
                .HasColumnType("decimal(5, 2)");
            entity.Property(e => e.prefijo_pais)
                .HasMaxLength(10)
                .IsUnicode(false);
            entity.Property(e => e.ruc)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.ruta_fondo)
                .HasMaxLength(255)
                .IsUnicode(false);
            entity.Property(e => e.ruta_logo)
                .HasMaxLength(255)
                .IsUnicode(false);
            entity.Property(e => e.telefono)
                .HasMaxLength(30)
                .IsUnicode(false);
        });

        modelBuilder.Entity<tbl_consulta>(entity =>
        {
            entity.HasKey(e => e.id_consulta).HasName("PK__tbl_cons__6F53588B217EE3BC");

            entity.HasIndex(e => e.fecha_consulta, "idx_consulta_fecha");

            entity.Property(e => e.alergias).IsUnicode(false);
            entity.Property(e => e.antecedentes_familiares).IsUnicode(false);
            entity.Property(e => e.antecedentes_oculares).IsUnicode(false);
            entity.Property(e => e.antecedentes_personales).IsUnicode(false);
            entity.Property(e => e.detalle_usa_lentes).IsUnicode(false);
            entity.Property(e => e.enfermedades_previas).IsUnicode(false);
            entity.Property(e => e.evaluaciones).IsUnicode(false);
            entity.Property(e => e.examenes_preliminares).IsUnicode(false);
            entity.Property(e => e.examenes_varios).IsUnicode(false);
            entity.Property(e => e.fecha_consulta).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.historia_clinica).IsUnicode(false);
            entity.Property(e => e.medicamentos).IsUnicode(false);
            entity.Property(e => e.motivo_consulta)
                .HasMaxLength(255)
                .IsUnicode(false);
            entity.Property(e => e.notas).IsUnicode(false);
            entity.Property(e => e.usa_lentes).HasDefaultValue(true);

            entity.HasOne(d => d.id_optometraNavigation).WithMany(p => p.tbl_consulta)
                .HasForeignKey(d => d.id_optometra)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_consulta_usuario");

            entity.HasOne(d => d.id_pacienteNavigation).WithMany(p => p.tbl_consulta)
                .HasForeignKey(d => d.id_paciente)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_consulta_paciente");
        });

        modelBuilder.Entity<tbl_cta_cobrar>(entity =>
        {
            entity.HasKey(e => e.id_cta_cobrar);

            entity.ToTable("tbl_cta_cobrar");

            entity.HasIndex(e => new { e.id_cliente, e.estado }, "IX_cta_cobrar_cliente_estado");
            entity.HasIndex(e => e.fecha_vencimiento, "IX_cta_cobrar_vencimiento")
                .HasFilter("([estado]='Pendiente')");

            entity.Property(e => e.estado)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("Pendiente");
            entity.Property(e => e.fecha_creacion).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.fecha_emision).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.monto_total).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.saldo).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.usuario_creacion)
                .HasMaxLength(100)
                .IsUnicode(false);

            entity.HasOne(d => d.id_comprobanteNavigation).WithMany()
                .HasForeignKey(d => d.id_comprobante)
                .HasConstraintName("FK_tbl_cta_cobrar_tbl_comprobante");

            entity.HasOne(d => d.id_ventaNavigation).WithMany()
                .HasForeignKey(d => d.id_venta)
                .HasConstraintName("FK_tbl_cta_cobrar_tbl_venta");
        });

        modelBuilder.Entity<tbl_detalle_venta>(entity =>
        {
            entity.HasKey(e => e.id_detalle_venta).HasName("PK__tbl_deta__5B265D474E76981D");

            entity.HasIndex(e => new { e.origen_tipo, e.origen_id }, "IX_tbl_detalle_venta_origen")
                .HasFilter("([origen_tipo] IS NOT NULL AND [origen_id] IS NOT NULL)");

            entity.Property(e => e.concepto_item).IsUnicode(false);
            entity.Property(e => e.descuento).HasColumnType("decimal(10, 2)");
            entity.Property(e => e.motivo_descuento)
                .HasMaxLength(255)
                .IsUnicode(false);
            entity.Property(e => e.origen_tipo)
                .HasMaxLength(40)
                .IsUnicode(false);
            entity.Property(e => e.precio_unitario).HasColumnType("decimal(10, 2)");
            entity.Property(e => e.origen_id);
            entity.Property(e => e.total_item).HasColumnType("decimal(10, 2)");

            entity.HasOne(d => d.id_productoNavigation).WithMany(p => p.tbl_detalle_venta)
                .HasForeignKey(d => d.id_producto)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_detalle_producto");

            entity.HasOne(d => d.id_ventaNavigation).WithMany(p => p.tbl_detalle_venta)
                .HasForeignKey(d => d.id_venta)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_detalle_venta");
        });

        modelBuilder.Entity<tbl_envio_laboratorio>(entity =>
        {
            entity.HasKey(e => e.id_envio_laboratorio).HasName("PK__tbl_envi__BF4AEA25C263BDF5");

            entity.ToTable("tbl_envio_laboratorio");

            entity.Property(e => e.canal)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.estado)
                .HasMaxLength(30)
                .IsUnicode(false);
            entity.Property(e => e.fecha_envio).HasDefaultValueSql("(getdate())");

            entity.HasOne(d => d.id_orden_rxNavigation).WithMany(p => p.tbl_envio_laboratorios)
                .HasForeignKey(d => d.id_orden_rx)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_envio_orden");

            entity.HasOne(d => d.id_usuarioNavigation).WithMany(p => p.tbl_envio_laboratorioid_usuarioNavigations)
                .HasForeignKey(d => d.id_usuario)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_envio_usuario");

            entity.HasOne(d => d.id_usuario_entregaNavigation).WithMany(p => p.tbl_envio_laboratorioid_usuario_entregaNavigations)
                .HasForeignKey(d => d.id_usuario_entrega)
                .HasConstraintName("fk_envio_usuario_entrega");
        });

        modelBuilder.Entity<tbl_laboratorio>(entity =>
        {
            entity.HasKey(e => e.id_laboratorio).HasName("PK__tbl_labo__781B42E28411F1C8");

            entity.ToTable("tbl_laboratorio");

            entity.Property(e => e.activo).HasDefaultValue(true);
            entity.Property(e => e.correo)
                .HasMaxLength(150)
                .IsUnicode(false);
            entity.Property(e => e.direccion)
                .HasMaxLength(255)
                .IsUnicode(false);
            entity.Property(e => e.nombre)
                .HasMaxLength(150)
                .IsUnicode(false);
            entity.Property(e => e.persona_contacto)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.whatsapp)
                .HasMaxLength(30)
                .IsUnicode(false);
        });

        modelBuilder.Entity<tbl_log_auditoria>(entity =>
        {
            entity.HasKey(e => e.id_log_auditoria).HasName("PK__tbl_log___CF188A05B770E02E");

            entity.Property(e => e.accion)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.detalle).IsUnicode(false);
            entity.Property(e => e.fecha).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.modulo)
                .HasMaxLength(100)
                .IsUnicode(false);

            entity.HasOne(d => d.id_usuarioNavigation).WithMany(p => p.tbl_log_auditoria)
                .HasForeignKey(d => d.id_usuario)
                .HasConstraintName("fk_log_usuario");
        });

        modelBuilder.Entity<tbl_metodo_pago>(entity =>
        {
            entity.HasKey(e => e.id_metodo_pago).HasName("PK__tbl_meto__85BE0EBC7C55A7BC");

            entity.ToTable("tbl_metodo_pago");

            entity.Property(e => e.nombre)
                .HasMaxLength(100)
                .IsUnicode(false);
        });

        modelBuilder.Entity<tbl_movimiento_inventario>(entity =>
        {
            entity.HasKey(e => e.id_movimiento_inventario).HasName("PK__tbl_movi__95610EAE94740B84");

            entity.ToTable("tbl_movimiento_inventario");

            entity.Property(e => e.comprobante_numero)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.costo_total_movimiento).HasColumnType("decimal(15, 2)");
            entity.Property(e => e.costo_unitario).HasColumnType("decimal(15, 2)");
            entity.Property(e => e.fecha_movimiento).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.metodo_valuacion)
                .HasMaxLength(30)
                .IsUnicode(false);
            entity.Property(e => e.numero_lote)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.observaciones).IsUnicode(false);
            entity.Property(e => e.saldo_en_dinero).HasColumnType("decimal(15, 2)");
            entity.Property(e => e.tipo_documento_referencia)
                .HasMaxLength(30)
                .IsUnicode(false);
            entity.Property(e => e.tipo_movimiento)
                .HasMaxLength(20)
                .IsUnicode(false);

            entity.HasOne(d => d.id_productoNavigation).WithMany(p => p.tbl_movimiento_inventarios)
                .HasForeignKey(d => d.id_producto)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_movimiento_producto");

            entity.HasOne(d => d.id_usuarioNavigation).WithMany(p => p.tbl_movimiento_inventarios)
                .HasForeignKey(d => d.id_usuario)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_movimiento_usuario");

            entity.HasOne(d => d.id_loteNavigation).WithMany()
                .HasForeignKey(d => d.id_lote)
                .HasConstraintName("FK_movimiento_lote");

            entity.HasOne(d => d.id_usuario_autorizaNavigation).WithMany()
                .HasForeignKey(d => d.id_usuario_autoriza)
                .HasConstraintName("FK_movimiento_usuario_autoriza");
        });

        modelBuilder.Entity<tbl_nota_credito>(entity =>
        {
            entity.HasKey(e => e.id_nota_credito);

            entity.ToTable("tbl_nota_credito");

            entity.HasIndex(e => new { e.id_comprobante_relacionado, e.id_cta_cobrar }, "IX_nota_relacion");

            entity.Property(e => e.estado)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("Emitida");
            entity.Property(e => e.fecha_creacion).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.fecha_emision).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.monto_total).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.motivo)
                .HasMaxLength(255)
                .IsUnicode(false);
            entity.Property(e => e.numero_nota)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.usuario_creacion)
                .HasMaxLength(100)
                .IsUnicode(false);

            entity.HasOne(d => d.id_comprobante_relacionadoNavigation).WithMany()
                .HasForeignKey(d => d.id_comprobante_relacionado)
                .HasConstraintName("FK_tbl_nota_credito_tbl_comprobante");

            entity.HasOne(d => d.id_cta_cobrarNavigation).WithMany()
                .HasForeignKey(d => d.id_cta_cobrar)
                .HasConstraintName("FK_tbl_nota_credito_tbl_cta_cobrar");
        });

        modelBuilder.Entity<tbl_orden_rx>(entity =>
        {
            entity.HasKey(e => e.id_orden_rx).HasName("PK__tbl_orde__FA23F3B4501E66F3");

            entity.ToTable("tbl_orden_rx");

            entity.HasIndex(e => e.numero_orden, "UQ__tbl_orde__37067115C2E49AC0").IsUnique();

            entity.HasIndex(e => e.estado, "idx_orden_estado");

            entity.Property(e => e.estado)
                .HasMaxLength(30)
                .IsUnicode(false)
                .HasDefaultValue("Enviado a laboratorio");
            entity.Property(e => e.fecha_creacion).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.numero_orden)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.observaciones).IsUnicode(false);
            entity.Property(e => e.tipo_rx)
                .HasMaxLength(30)
                .IsUnicode(false);

            entity.HasOne(d => d.id_consultaNavigation).WithMany(p => p.tbl_orden_rxes)
                .HasForeignKey(d => d.id_consulta)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_orden_consulta");

            entity.HasOne(d => d.id_laboratorioNavigation).WithMany(p => p.tbl_orden_rxes)
                .HasForeignKey(d => d.id_laboratorio)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_orden_laboratorio");

            entity.HasOne(d => d.id_rx_contactologiaNavigation).WithMany(p => p.tbl_orden_rxes)
                .HasForeignKey(d => d.id_rx_contactologia)
                .HasConstraintName("fk_orden_rx_contactologia");

            entity.HasOne(d => d.id_rx_lenteNavigation).WithMany(p => p.tbl_orden_rxes)
                .HasForeignKey(d => d.id_rx_lente)
                .HasConstraintName("fk_orden_rx_lente");
        });

        modelBuilder.Entity<tbl_paciente>(entity =>
        {
            entity.HasKey(e => e.id_paciente).HasName("PK__tbl_paci__2C2C72BB9F4DAC7E");

            entity.ToTable("tbl_paciente");

            entity.HasIndex(e => e.cedula, "UQ__tbl_paci__415B7BE5D61B9852").IsUnique();

            entity.HasIndex(e => e.codigo_paciente, "UQ__tbl_paci__94DC8C4D9285B096").IsUnique();

            entity.HasIndex(e => e.cedula, "idx_paciente_cedula");

            entity.HasIndex(e => new { e.apellidos, e.nombres }, "idx_paciente_nombre");

            entity.Property(e => e.activo).HasDefaultValue(true);
            entity.Property(e => e.apellidos)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.cedula)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.codigo_paciente)
                .HasMaxLength(30)
                .IsUnicode(false);
            entity.Property(e => e.direccion)
                .HasMaxLength(255)
                .IsUnicode(false);
            entity.Property(e => e.email)
                .HasMaxLength(150)
                .IsUnicode(false);
            entity.Property(e => e.estado_civil)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.fecha_registro).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.genero)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.nombres)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.observaciones).IsUnicode(false);
            entity.Property(e => e.ocupacion)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.telefono)
                .HasMaxLength(20)
                .IsUnicode(false);

            entity.HasOne(d => d.id_usuario_registroNavigation).WithMany(p => p.tbl_pacientes)
                .HasForeignKey(d => d.id_usuario_registro)
                .HasConstraintName("fk_paciente_usuario");
        });

        modelBuilder.Entity<tbl_plantilla_mensaje>(entity =>
        {
            entity.HasKey(e => e.id_plantilla_mensaje).HasName("PK__tbl_plan__FABE8524D220C42C");

            entity.ToTable("tbl_plantilla_mensaje");

            entity.Property(e => e.canal)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.contenido).IsUnicode(false);
            entity.Property(e => e.nombre)
                .HasMaxLength(150)
                .IsUnicode(false);
            entity.Property(e => e.tipo)
                .HasMaxLength(100)
                .IsUnicode(false);
        });

        modelBuilder.Entity<tbl_producto>(entity =>
        {
            entity.HasKey(e => e.id_producto).HasName("PK__tbl_prod__FF341C0DD2D32297");

            entity.ToTable("tbl_producto");

            entity.HasIndex(e => e.codigo_producto, "UQ__tbl_prod__105107A8124A631A").IsUnique();

            entity.HasIndex(e => e.codigo_producto, "idx_producto_codigo");

            entity.Property(e => e.activo).HasDefaultValue(true);
            entity.Property(e => e.almacen)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.cantidad_empaque).HasDefaultValue(1);
            entity.Property(e => e.cantidad_movimientos_mes).HasDefaultValue(0);
            entity.Property(e => e.cantidad_pedido_optima);
            entity.Property(e => e.centro_costo)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.codigo_producto)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.codigo_barras)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.descripcion).IsUnicode(false);
            entity.Property(e => e.cuenta_contable)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.descuento_cliente_fijo).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.descuento_mayorista).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.dimensiones_alto).HasColumnType("decimal(10, 4)");
            entity.Property(e => e.dimensiones_ancho).HasColumnType("decimal(10, 4)");
            entity.Property(e => e.dimensiones_largo).HasColumnType("decimal(10, 4)");
            entity.Property(e => e.estado_producto)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("Disponible");
            entity.Property(e => e.es_promocion).HasDefaultValue(false);
            entity.Property(e => e.etiquetas)
                .HasMaxLength(500)
                .IsUnicode(false);
            entity.Property(e => e.fecha_creacion).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.fecha_ultima_compra).HasColumnType("datetime");
            entity.Property(e => e.fecha_ultima_venta).HasColumnType("datetime");
            entity.Property(e => e.fecha_actualizacion_precio).HasColumnType("datetime");
            entity.Property(e => e.marca)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.material_principal)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.modelo)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.movimiento_frecuencia)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.motivo_estado)
                .HasMaxLength(300)
                .IsUnicode(false);
            entity.Property(e => e.naturaleza_item)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.nivel)
                .HasMaxLength(10)
                .IsUnicode(false);
            entity.Property(e => e.nombre_producto)
                .HasMaxLength(150)
                .IsUnicode(false);
            entity.Property(e => e.notas_internas).IsUnicode(false);
            entity.Property(e => e.pasillo)
                .HasMaxLength(10)
                .IsUnicode(false);
            entity.Property(e => e.precio_costo).HasColumnType("decimal(10, 2)");
            entity.Property(e => e.precio_venta).HasColumnType("decimal(10, 2)");
            entity.Property(e => e.porcentaje_iva)
                .HasDefaultValue(0.00m)
                .HasColumnType("decimal(5, 2)");
            entity.Property(e => e.peso_unitario).HasColumnType("decimal(10, 4)");
            entity.Property(e => e.porcentaje_descuento_promo).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.porcentaje_margen).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.proveedor_preferente)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.punto_reorden).HasDefaultValue(0);
            entity.Property(e => e.referencia_fabricante)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.requiere_fecha_vencimiento).HasDefaultValue(false);
            entity.Property(e => e.requiere_lote).HasDefaultValue(false);
            entity.Property(e => e.sku_alterno)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.stock_actual).HasDefaultValue(0);
            entity.Property(e => e.stock_maximo).HasDefaultValue(0);
            entity.Property(e => e.stock_minimo).HasDefaultValue(0);
            entity.Property(e => e.talla)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.tiene_iva).HasDefaultValue(false);
            entity.Property(e => e.tipo_item)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasDefaultValue("Producto");
            entity.Property(e => e.tiempo_entrega_dias);
            entity.Property(e => e.tratamiento_lente)
                .HasMaxLength(200)
                .IsUnicode(false);
            entity.Property(e => e.usuario_creacion)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.volumen_m3).HasColumnType("decimal(15, 8)");
            entity.Property(e => e.color)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.estante)
                .HasMaxLength(10)
                .IsUnicode(false);

            entity.HasOne(d => d.id_categoriaNavigation).WithMany(p => p.tbl_productos)
                .HasForeignKey(d => d.id_categoria)
                .HasConstraintName("fk_producto_categoria");

            entity.HasOne(d => d.id_proveedorNavigation).WithMany(p => p.tbl_productos)
                .HasForeignKey(d => d.id_proveedor)
                .HasConstraintName("fk_producto_proveedor");

            entity.HasOne(d => d.id_usuario_actualizacionNavigation).WithMany()
                .HasForeignKey(d => d.id_usuario_actualizacion)
                .HasConstraintName("FK_tbl_producto_usuario_actualizacion");
        });

        modelBuilder.Entity<tbl_proveedor>(entity =>
        {
            entity.HasKey(e => e.id_proveedor).HasName("PK__tbl_prov__8D3DFE28F17EAE7F");

            entity.ToTable("tbl_proveedor");

            entity.Property(e => e.banco_nombre)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.calificacion)
                .HasMaxLength(1)
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
            entity.Property(e => e.cuenta_bancaria)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.direccion)
                .HasMaxLength(255)
                .IsUnicode(false);
            entity.Property(e => e.email)
                .HasMaxLength(150)
                .IsUnicode(false);
            entity.Property(e => e.fecha_actualizacion).HasColumnType("datetime");
            entity.Property(e => e.fecha_registro).HasColumnType("datetime");
            entity.Property(e => e.limite_credito).HasColumnType("decimal(15, 2)");
            entity.Property(e => e.nombre)
                .HasMaxLength(150)
                .IsUnicode(false);
            entity.Property(e => e.nombre_comercial)
                .HasMaxLength(300)
                .IsUnicode(false);
            entity.Property(e => e.observaciones).IsUnicode(false);
            entity.Property(e => e.provincia)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.razon_social)
                .HasMaxLength(300)
                .IsUnicode(false);
            entity.Property(e => e.ruc)
                .HasMaxLength(13)
                .IsUnicode(false);
            entity.Property(e => e.saldo_pendiente).HasColumnType("decimal(15, 2)");
            entity.Property(e => e.telefono)
                .HasMaxLength(30)
                .IsUnicode(false);
            entity.Property(e => e.tipo_cuenta)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.tipo_identificacion)
                .HasMaxLength(2)
                .IsUnicode(false);

            entity.HasOne(d => d.id_usuario_actualizacionNavigation).WithMany()
                .HasForeignKey(d => d.id_usuario_actualizacion)
                .HasConstraintName("FK_tbl_proveedor_usuario_actualizacion");

            entity.HasOne(d => d.id_usuario_registroNavigation).WithMany()
                .HasForeignKey(d => d.id_usuario_registro)
                .HasConstraintName("FK_tbl_proveedor_usuario_registro");
        });

        modelBuilder.Entity<tbl_rol>(entity =>
        {
            entity.HasKey(e => e.id_rol).HasName("PK__tbl_rol__6ABCB5E0C9CAB04F");

            entity.ToTable("tbl_rol");

            entity.Property(e => e.descripcion)
                .HasMaxLength(255)
                .IsUnicode(false);
            entity.Property(e => e.nombre)
                .HasMaxLength(100)
                .IsUnicode(false);
        });

        modelBuilder.Entity<tbl_rx_contactologia>(entity =>
        {
            entity.HasKey(e => e.id_rx_contactologia).HasName("PK__tbl_rx_c__976E489E0939F373");

            entity.Property(e => e.observaciones).IsUnicode(false);
            entity.Property(e => e.od_av)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.od_avcc_cerca)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.od_avcc_lejos)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.od_cilindro).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.od_curva_base).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.od_diametro).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.od_eje).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.od_esfera).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.oi_av)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.oi_avcc_cerca)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.oi_avcc_lejos)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.oi_cilindro).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.oi_curva_base).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.oi_diametro).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.oi_eje).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.oi_esfera).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.tipo_lente)
                .HasMaxLength(100)
                .IsUnicode(false);

            entity.HasOne(d => d.id_consultaNavigation).WithMany(p => p.tbl_rx_contactologia)
                .HasForeignKey(d => d.id_consulta)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_rx_contactologia_consulta");
        });

        modelBuilder.Entity<tbl_rx_lente>(entity =>
        {
            entity.HasKey(e => e.id_rx_lente).HasName("PK__tbl_rx_l__79164068B84525CC");

            entity.ToTable("tbl_rx_lente");

            entity.Property(e => e.diseno_lente)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.material)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.observaciones).IsUnicode(false);
            entity.Property(e => e.od_addicion).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.od_altura).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.od_cilindro).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.od_dnp).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.od_dp).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.od_eje).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.od_esfera).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.od_prisma).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.oi_addicion).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.oi_altura).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.oi_cilindro).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.oi_dnp).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.oi_dp).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.oi_eje).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.oi_esfera).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.oi_prisma).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.tratamiento)
                .HasMaxLength(100)
                .IsUnicode(false);

            entity.HasOne(d => d.id_consultaNavigation).WithMany(p => p.tbl_rx_lentes)
                .HasForeignKey(d => d.id_consulta)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_rx_lente_consulta");
        });

        modelBuilder.Entity<tbl_sesion>(entity =>
        {
            entity.HasKey(e => e.id_sesion).HasName("PK__tbl_sesi__8D3F9DFE8823BBC0");

            entity.ToTable("tbl_sesion");

            entity.Property(e => e.ip)
                .HasMaxLength(100)
                .IsUnicode(false);

            entity.HasOne(d => d.id_usuarioNavigation).WithMany(p => p.tbl_sesions)
                .HasForeignKey(d => d.id_usuario)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_sesion_usuario");
        });

        modelBuilder.Entity<tbl_usuario>(entity =>
        {
            entity.HasKey(e => e.id_usuario).HasName("PK__tbl_usua__4E3E04AD04F9EB75");

            entity.ToTable("tbl_usuario");

            entity.HasIndex(e => e.usuario, "UQ__tbl_usua__9AFF8FC6AD86ADCC").IsUnique();

            entity.HasIndex(e => e.email, "UQ__tbl_usua__AB6E6164452C2F0F").IsUnique();

            entity.Property(e => e.activo).HasDefaultValue(true);
            entity.Property(e => e.apellidos)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.bloqueado).HasDefaultValue(true);
            entity.Property(e => e.email)
                .HasMaxLength(150)
                .IsUnicode(false);
            entity.Property(e => e.fecha_nacimiento).HasColumnType("date");
            entity.Property(e => e.fecha_creacion).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.intentos_fallidos).HasDefaultValue(0);
            entity.Property(e => e.nombres)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.password_hash)
                .HasMaxLength(255)
                .IsUnicode(false);
            entity.Property(e => e.telefono)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.usuario)
                .HasMaxLength(100)
                .IsUnicode(false);

            entity.HasOne(d => d.id_rolNavigation).WithMany(p => p.tbl_usuarios)
                .HasForeignKey(d => d.id_rol)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_usuario_rol");
        });

        modelBuilder.Entity<tbl_venta>(entity =>
        {
            entity.HasKey(e => e.id_venta).HasName("PK__tbl_vent__459533BF685FA7CC");

            entity.HasIndex(e => e.estado, "idx_venta_estado");

            entity.HasIndex(e => e.fecha_venta, "idx_venta_fecha");

            entity.HasIndex(e => new { e.id_paciente, e.estado }, "IX_tbl_venta_paciente_estado");

            entity.Property(e => e.concepto).IsUnicode(false);
            entity.Property(e => e.descuento_total).HasColumnType("decimal(10, 2)");
            entity.Property(e => e.dias_credito).HasDefaultValue(0);
            entity.Property(e => e.estado)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("Pendiente");
            entity.Property(e => e.fecha_venta).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.forma_pago)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasDefaultValue("Efectivo");
            entity.Property(e => e.impuesto_total).HasColumnType("decimal(10, 2)");
            entity.Property(e => e.observaciones_factura).IsUnicode(false);
            entity.Property(e => e.porcentaje_impuesto).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.saldo_pendiente).HasColumnType("decimal(10, 2)");
            entity.Property(e => e.subtotal).HasColumnType("decimal(10, 2)");
            entity.Property(e => e.total).HasColumnType("decimal(10, 2)");
            entity.Property(e => e.valor_cobrado).HasColumnType("decimal(10, 2)");

            entity.HasOne(d => d.id_pacienteNavigation).WithMany(p => p.tbl_venta)
                .HasForeignKey(d => d.id_paciente)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_venta_paciente");

            entity.HasOne(d => d.id_usuarioNavigation).WithMany(p => p.tbl_venta)
                .HasForeignKey(d => d.id_usuario)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_venta_usuario");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
