using Microsoft.EntityFrameworkCore;
using OptometriaApp.Models;

namespace OptometriaApp.Data;

public partial class OpticaDbContext
{
    public virtual DbSet<tbl_medico> tbl_medico { get; set; } = null!;

    public virtual DbSet<tbl_disponibilidad_medico> tbl_disponibilidad_medico { get; set; } = null!;

    public virtual DbSet<tbl_estado_cita> tbl_estado_cita { get; set; } = null!;

    public virtual DbSet<tbl_citas> tbl_citas { get; set; } = null!;

    public virtual DbSet<tbl_bloqueo_horarios> tbl_bloqueo_horarios { get; set; } = null!;

    public virtual DbSet<tbl_cancelaciones_paciente> tbl_cancelaciones_paciente { get; set; } = null!;

    internal static void ConfigureAppointmentEntities(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<tbl_medico>(entity =>
        {
            entity.HasKey(e => e.id_medico).HasName("PK_tbl_medico");
            entity.ToTable("tbl_medico");
            entity.HasIndex(e => e.id_usuario, "UQ_tbl_medico_usuario").IsUnique();
            entity.HasIndex(e => e.numero_licencia, "UQ_tbl_medico_licencia").IsUnique();
            entity.HasIndex(e => e.activo, "IX_tbl_medico_activo");
            entity.HasIndex(e => e.especialidad, "IX_tbl_medico_especialidad");
            entity.Property(e => e.numero_licencia).HasMaxLength(50).IsUnicode(false);
            entity.Property(e => e.especialidad).HasMaxLength(100).IsUnicode(false);
            entity.Property(e => e.cedula_profesional).HasMaxLength(50).IsUnicode(false);
            entity.Property(e => e.institucion_egreso).HasMaxLength(200).IsUnicode(false);
            entity.Property(e => e.telefono_consultorio).HasMaxLength(20).IsUnicode(false);
            entity.Property(e => e.biografia).IsUnicode(false);
            entity.Property(e => e.certificaciones).IsUnicode(false);
            entity.Property(e => e.idiomas).HasMaxLength(200).IsUnicode(false);
            entity.Property(e => e.precio_consulta_base).HasColumnType("decimal(10, 2)");
            entity.Property(e => e.descuento_porcentaje).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.aceptar_citas_telefonicas).HasDefaultValue(true);
            entity.Property(e => e.aceptar_citas_presenciales).HasDefaultValue(true);
            entity.Property(e => e.puede_gestionar_agenda).HasDefaultValue(true);
            entity.Property(e => e.puede_gestionar_disponibilidad).HasDefaultValue(true);
            entity.Property(e => e.puede_gestionar_historia_clinica).HasDefaultValue(true);
            entity.Property(e => e.puede_gestionar_facturacion).HasDefaultValue(false);
            entity.Property(e => e.duracion_consulta_minutos).HasDefaultValue(30);
            entity.Property(e => e.observaciones).IsUnicode(false);
            entity.Property(e => e.activo).HasDefaultValue(true);
            entity.Property(e => e.fecha_creacion).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.fecha_actualizacion).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.usuario_creacion).HasMaxLength(100).IsUnicode(false);
            entity.Property(e => e.usuario_actualizacion).HasMaxLength(100).IsUnicode(false);

            entity.HasOne(d => d.id_usuarioNavigation).WithOne(p => p.tbl_medico)
                .HasForeignKey<tbl_medico>(d => d.id_usuario)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("fk_medico_usuario");
        });

        modelBuilder.Entity<tbl_disponibilidad_medico>(entity =>
        {
            entity.HasKey(e => e.id_disponibilidad).HasName("PK_tbl_disponibilidad_medico");
            entity.ToTable("tbl_disponibilidad_medico");
            entity.HasIndex(e => e.id_medico, "IX_tbl_disponibilidad_medico_medico");
            entity.HasIndex(e => e.dia_semana, "IX_tbl_disponibilidad_medico_dia");
            entity.HasIndex(e => e.disponible, "IX_tbl_disponibilidad_medico_disponible");
            entity.Property(e => e.nombre_dia).HasMaxLength(20).IsUnicode(false);
            entity.Property(e => e.permitir_descanso_medio_dia).HasDefaultValue(false);
            entity.Property(e => e.disponible).HasDefaultValue(true);
            entity.Property(e => e.observaciones).HasMaxLength(500).IsUnicode(false);
            entity.Property(e => e.fecha_creacion).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.fecha_actualizacion).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.usuario_actualizacion).HasMaxLength(100).IsUnicode(false);

            entity.HasOne(d => d.id_medicoNavigation).WithMany(p => p.tbl_disponibilidad_medico)
                .HasForeignKey(d => d.id_medico)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_disponibilidad_medico");
        });

        modelBuilder.Entity<tbl_estado_cita>(entity =>
        {
            entity.HasKey(e => e.id_estado).HasName("PK_tbl_estado_cita");
            entity.ToTable("tbl_estado_cita");
            entity.HasIndex(e => e.nombre_estado, "UQ_tbl_estado_cita_nombre").IsUnique();
            entity.Property(e => e.nombre_estado).HasMaxLength(50).IsUnicode(false);
            entity.Property(e => e.descripcion).HasMaxLength(255).IsUnicode(false);
            entity.Property(e => e.activo).HasDefaultValue(true);
        });

        modelBuilder.Entity<tbl_citas>(entity =>
        {
            entity.HasKey(e => e.id_cita).HasName("PK_tbl_citas");
            entity.ToTable("tbl_citas");
            entity.HasIndex(e => e.id_medico, "IX_tbl_citas_medico");
            entity.HasIndex(e => e.id_paciente, "IX_tbl_citas_paciente");
            entity.HasIndex(e => e.fecha_cita, "IX_tbl_citas_fecha");
            entity.HasIndex(e => e.id_estado, "IX_tbl_citas_estado");
            entity.HasIndex(e => new { e.id_medico, e.fecha_cita }, "IX_tbl_citas_medico_fecha");
            entity.Property(e => e.fecha_cita).HasColumnType("date");
            entity.Property(e => e.tipo_cita).HasMaxLength(50).IsUnicode(false);
            entity.Property(e => e.motivo_cita).HasMaxLength(255).IsUnicode(false);
            entity.Property(e => e.descripcion_adicional).IsUnicode(false);
            entity.Property(e => e.id_estado).HasDefaultValue(1);
            entity.Property(e => e.usuario_confirmacion).HasMaxLength(100).IsUnicode(false);
            entity.Property(e => e.razon_cancelacion).HasMaxLength(500).IsUnicode(false);
            entity.Property(e => e.usuario_cancelacion).HasMaxLength(100).IsUnicode(false);
            entity.Property(e => e.notificacion_enviada).HasDefaultValue(false);
            entity.Property(e => e.tipo_notificacion).HasMaxLength(50).IsUnicode(false);
            entity.Property(e => e.recordatorio_24hrs).HasDefaultValue(false);
            entity.Property(e => e.recordatorio_1hr).HasDefaultValue(false);
            entity.Property(e => e.notas_medico).IsUnicode(false);
            entity.Property(e => e.fecha_creacion).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.fecha_actualizacion).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.usuario_creacion).HasMaxLength(100).IsUnicode(false);
            entity.Property(e => e.usuario_actualizacion).HasMaxLength(100).IsUnicode(false);

            entity.HasOne(d => d.id_medicoNavigation).WithMany(p => p.tbl_citas)
                .HasForeignKey(d => d.id_medico)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("fk_citas_medico");

            entity.HasOne(d => d.id_pacienteNavigation).WithMany(p => p.tbl_citas)
                .HasForeignKey(d => d.id_paciente)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("fk_citas_paciente");

            entity.HasOne(d => d.id_disponibilidadNavigation).WithMany(p => p.tbl_citas)
                .HasForeignKey(d => d.id_disponibilidad)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_citas_disponibilidad");

            entity.HasOne(d => d.id_consultaNavigation).WithMany()
                .HasForeignKey(d => d.id_consulta)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_citas_consulta");

            entity.HasOne(d => d.id_estadoNavigation).WithMany(p => p.tbl_citas)
                .HasForeignKey(d => d.id_estado)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("fk_citas_estado");
        });

        modelBuilder.Entity<tbl_bloqueo_horarios>(entity =>
        {
            entity.HasKey(e => e.id_bloqueo).HasName("PK_tbl_bloqueo_horarios");
            entity.ToTable("tbl_bloqueo_horarios");
            entity.HasIndex(e => e.id_medico, "IX_tbl_bloqueo_horarios_medico");
            entity.HasIndex(e => new { e.fecha_inicio, e.fecha_fin }, "IX_tbl_bloqueo_horarios_fechas");
            entity.Property(e => e.fecha_inicio).HasColumnType("date");
            entity.Property(e => e.fecha_fin).HasColumnType("date");
            entity.Property(e => e.alcance_bloqueo).HasMaxLength(20).IsUnicode(false).HasDefaultValue("Completo");
            entity.Property(e => e.tipo_bloqueo).HasMaxLength(50).IsUnicode(false);
            entity.Property(e => e.razon_bloqueo).HasMaxLength(300).IsUnicode(false);
            entity.Property(e => e.activo).HasDefaultValue(true);
            entity.Property(e => e.fecha_creacion).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.usuario_creacion).HasMaxLength(100).IsUnicode(false);

            entity.HasOne(d => d.id_medicoNavigation).WithMany(p => p.tbl_bloqueo_horarios)
                .HasForeignKey(d => d.id_medico)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_bloqueo_medico");
        });

        modelBuilder.Entity<tbl_cancelaciones_paciente>(entity =>
        {
            entity.HasKey(e => e.id_cancelacion).HasName("PK_tbl_cancelaciones_paciente");
            entity.ToTable("tbl_cancelaciones_paciente");
            entity.HasIndex(e => e.id_paciente, "IX_tbl_cancelaciones_paciente_paciente");
            entity.HasIndex(e => e.fecha_cancelacion, "IX_tbl_cancelaciones_paciente_fecha");
            entity.Property(e => e.fecha_cancelacion).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.razon_cancelacion).HasMaxLength(500).IsUnicode(false);
            entity.Property(e => e.quien_cancelo).HasMaxLength(50).IsUnicode(false);
            entity.Property(e => e.penalizacion_aplicada).HasDefaultValue(false);
            entity.Property(e => e.usuario_cancelacion).HasMaxLength(100).IsUnicode(false);

            entity.HasOne(d => d.id_citaNavigation).WithMany(p => p.tbl_cancelaciones_paciente)
                .HasForeignKey(d => d.id_cita)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_cancelaciones_cita");

            entity.HasOne(d => d.id_pacienteNavigation).WithMany(p => p.tbl_cancelaciones_paciente)
                .HasForeignKey(d => d.id_paciente)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_cancelaciones_paciente");
        });

        modelBuilder.Entity<tbl_paciente>(entity =>
        {
            entity.Property(e => e.id_usuario);

            entity.HasOne(d => d.id_usuarioNavigation).WithOne(p => p.tbl_paciente_vinculado)
                .HasForeignKey<tbl_paciente>(d => d.id_usuario)
                .HasConstraintName("FK_tbl_paciente_tbl_usuario");
        });
    }
}
