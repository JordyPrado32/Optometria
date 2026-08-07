using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using OptometriaApp.Data;
using OptometriaApp.Models;

namespace OptometriaApp.Api;

public static class MobileOperationsEndpoints
{
    public static IEndpointRouteBuilder MapMobileOperationsApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api")
            .WithTags("Operaciones moviles")
            .RequireAuthorization(policy =>
            {
                policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme);
                policy.RequireAuthenticatedUser();
            });

        group.MapGet("/profile/me", GetProfileAsync)
            .WithName("MobileApiProfile")
            .WithSummary("Devuelve el perfil del usuario autenticado");

        group.MapGet("/medicos", GetDoctorsAsync)
            .WithName("MobileApiDoctors")
            .WithSummary("Lista medicos activos para agendar citas");

        group.MapGet("/pacientes", GetPatientsAsync)
            .WithName("MobileApiPatients")
            .WithSummary("Lista pacientes activos");

        group.MapPost("/pacientes", CreatePatientAsync)
            .WithName("MobileApiCreatePatient")
            .WithSummary("Registra un paciente desde la aplicacion movil");

        group.MapGet("/medicos/{doctorId:int}/slots/{date}", GetSlotsAsync)
            .WithName("MobileApiDoctorSlots")
            .WithSummary("Calcula horarios disponibles para un medico y una fecha");

        group.MapGet("/citas", GetAppointmentsAsync)
            .WithName("MobileApiAppointments")
            .WithSummary("Lista citas registradas");

        group.MapPost("/citas", CreateAppointmentAsync)
            .WithName("MobileApiCreateAppointment")
            .WithSummary("Agenda una cita desde la aplicacion movil");

        return endpoints;
    }

    private static async Task<IResult> GetProfileAsync(
        ClaimsPrincipal principal,
        IDbContextFactory<OpticaDbContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
        {
            return Results.Unauthorized();
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var user = await dbContext.tbl_usuarios
            .AsNoTracking()
            .Include(current => current.id_rolNavigation)
            .Include(current => current.tbl_usuario_seguridad)
            .FirstOrDefaultAsync(current => current.id_usuario == userId, cancellationToken);

        return user is null
            ? Results.NotFound(new { message = "Usuario no encontrado." })
            : Results.Ok(MobileUserResponse.FromEntity(user));
    }

    private static async Task<IResult> GetDoctorsAsync(
        IDbContextFactory<OpticaDbContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var doctors = await dbContext.tbl_medico
            .AsNoTracking()
            .Where(doctor => doctor.activo == true && doctor.id_usuarioNavigation.activo == true)
            .OrderBy(doctor => doctor.id_usuarioNavigation.apellidos)
            .ThenBy(doctor => doctor.id_usuarioNavigation.nombres)
            .Select(doctor => new MobileDoctorResponse(
                doctor.id_medico,
                doctor.id_usuarioNavigation.nombres,
                doctor.id_usuarioNavigation.apellidos,
                doctor.especialidad ?? "Optometria",
                doctor.duracion_consulta_minutos ?? 30,
                doctor.precio_consulta_base ?? 0m))
            .ToListAsync(cancellationToken);

        return Results.Ok(doctors);
    }

    private static async Task<IResult> GetPatientsAsync(
        IDbContextFactory<OpticaDbContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var patientEntities = await dbContext.tbl_pacientes
            .AsNoTracking()
            .Where(patient => patient.activo == true)
            .OrderBy(patient => patient.apellidos)
            .ThenBy(patient => patient.nombres)
            .ToListAsync(cancellationToken);

        return Results.Ok(patientEntities.Select(MobilePatientResponse.FromEntity).ToList());
    }

    private static async Task<IResult> CreatePatientAsync(
        MobileCreatePatientRequest request,
        ClaimsPrincipal principal,
        IDbContextFactory<OpticaDbContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
        {
            return Results.Unauthorized();
        }

        var cedula = request.Cedula?.Trim() ?? string.Empty;
        var nombres = request.Nombres?.Trim() ?? string.Empty;
        var apellidos = request.Apellidos?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(cedula) ||
            string.IsNullOrWhiteSpace(nombres) ||
            string.IsNullOrWhiteSpace(apellidos))
        {
            return Results.BadRequest(new { message = "Cedula, nombres y apellidos son obligatorios." });
        }

        if (cedula.Length > 20 || nombres.Length > 100 || apellidos.Length > 100)
        {
            return Results.BadRequest(new { message = "Uno de los campos obligatorios supera la longitud permitida." });
        }

        DateOnly? birthDate = null;
        if (!string.IsNullOrWhiteSpace(request.FechaNacimiento))
        {
            if (!DateOnly.TryParseExact(
                    request.FechaNacimiento.Trim(),
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsedBirthDate))
            {
                return Results.BadRequest(new { message = "La fecha de nacimiento debe usar el formato YYYY-MM-DD." });
            }

            birthDate = parsedBirthDate;
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (await dbContext.tbl_pacientes.AnyAsync(patient => patient.cedula == cedula, cancellationToken))
        {
            return Results.Conflict(new { message = "Ya existe un paciente con esa cedula." });
        }

        var patient = new tbl_paciente
        {
            cedula = cedula,
            nombres = nombres,
            apellidos = apellidos,
            telefono = LimitOrNull(request.Telefono, 20),
            email = LimitOrNull(request.Email, 150),
            fecha_nacimiento = birthDate,
            genero = LimitOrNull(request.Genero, 20),
            estado_civil = LimitOrNull(request.EstadoCivil, 50),
            ocupacion = LimitOrNull(request.Ocupacion, 100),
            direccion = LimitOrNull(request.Direccion, 255),
            observaciones = request.Observaciones?.Trim(),
            activo = true,
            fecha_registro = DateTime.Now,
            id_usuario_registro = userId
        };

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            dbContext.tbl_pacientes.Add(patient);
            await dbContext.SaveChangesAsync(cancellationToken);
            patient.codigo_paciente = $"PAC-{patient.id_paciente}";
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Created($"/api/pacientes/{patient.id_paciente}", MobilePatientResponse.FromEntity(patient));
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Results.Conflict(new { message = "No se pudo registrar el paciente. Revisa que la cedula no este duplicada." });
        }
    }

    private static async Task<IResult> GetSlotsAsync(
        int doctorId,
        string date,
        IDbContextFactory<OpticaDbContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (!DateOnly.TryParseExact(
                date,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var appointmentDate))
        {
            return Results.BadRequest(new { message = "La fecha debe usar el formato YYYY-MM-DD." });
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var doctor = await dbContext.tbl_medico
            .AsNoTracking()
            .FirstOrDefaultAsync(current => current.id_medico == doctorId && current.activo == true, cancellationToken);

        if (doctor is null)
        {
            return Results.NotFound(new { message = "Medico no encontrado." });
        }

        var dayOfWeek = appointmentDate.DayOfWeek == DayOfWeek.Sunday
            ? 7
            : (int)appointmentDate.DayOfWeek;

        var schedules = await dbContext.tbl_disponibilidad_medico
            .AsNoTracking()
            .Where(schedule =>
                schedule.id_medico == doctorId &&
                schedule.dia_semana == dayOfWeek &&
                schedule.disponible == true)
            .OrderBy(schedule => schedule.hora_inicio)
            .ToListAsync(cancellationToken);

        if (schedules.Count == 0)
        {
            return Results.Ok(new MobileSlotsResponse(false, []));
        }

        var appointments = await dbContext.tbl_citas
            .AsNoTracking()
            .Include(appointment => appointment.id_estadoNavigation)
            .Where(appointment =>
                appointment.id_medico == doctorId &&
                appointment.fecha_cita == appointmentDate &&
                (appointment.id_estadoNavigation == null ||
                 (appointment.id_estadoNavigation.nombre_estado != "Cancelada" &&
                  appointment.id_estadoNavigation.nombre_estado != "No presentado" &&
                  appointment.id_estadoNavigation.nombre_estado != "No se presento")))
            .Select(appointment => new { appointment.hora_inicio, appointment.hora_fin })
            .ToListAsync(cancellationToken);

        var duration = Math.Clamp(doctor.duracion_consulta_minutos ?? 30, 5, 480);
        var slots = new List<MobileSlotResponse>();

        foreach (var schedule in schedules)
        {
            var current = ToMinutes(schedule.hora_inicio);
            var end = ToMinutes(schedule.hora_fin);
            var breakStart = schedule.permitir_descanso_medio_dia == true && schedule.hora_descanso_inicio.HasValue
                ? ToMinutes(schedule.hora_descanso_inicio.Value)
                : (int?)null;
            var breakEnd = schedule.permitir_descanso_medio_dia == true && schedule.hora_descanso_fin.HasValue
                ? ToMinutes(schedule.hora_descanso_fin.Value)
                : (int?)null;

            while (current + duration <= end)
            {
                var slotEnd = current + duration;
                if (breakStart.HasValue && breakEnd.HasValue && current < breakEnd && slotEnd > breakStart)
                {
                    current = breakEnd.Value;
                    continue;
                }

                var occupied = appointments.Any(appointment =>
                    current < ToMinutes(appointment.hora_fin) &&
                    slotEnd > ToMinutes(appointment.hora_inicio));

                slots.Add(new MobileSlotResponse(
                    FormatMinutes(current),
                    FormatMinutes(slotEnd),
                    !occupied));
                current += duration;
            }
        }

        var distinctSlots = slots
            .GroupBy(slot => slot.Hora)
            .Select(group => group.First())
            .OrderBy(slot => slot.Hora)
            .ToList();

        return Results.Ok(new MobileSlotsResponse(true, distinctSlots));
    }

    private static async Task<IResult> GetAppointmentsAsync(
        IDbContextFactory<OpticaDbContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var appointmentEntities = await dbContext.tbl_citas
            .AsNoTracking()
            .Include(appointment => appointment.id_estadoNavigation)
            .Include(appointment => appointment.id_pacienteNavigation)
            .Include(appointment => appointment.id_medicoNavigation)
                .ThenInclude(doctor => doctor.id_usuarioNavigation)
            .OrderByDescending(appointment => appointment.fecha_cita)
            .ThenBy(appointment => appointment.hora_inicio)
            .ToListAsync(cancellationToken);

        var appointments = appointmentEntities.Select(appointment => new MobileAppointmentResponse(
                appointment.id_cita,
                appointment.fecha_cita.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                appointment.hora_inicio.ToString("HH:mm", CultureInfo.InvariantCulture),
                appointment.hora_fin.ToString("HH:mm", CultureInfo.InvariantCulture),
                appointment.tipo_cita,
                appointment.motivo_cita,
                appointment.id_estadoNavigation != null
                    ? appointment.id_estadoNavigation.nombre_estado
                    : "Programada",
                appointment.id_pacienteNavigation.nombres + " " + appointment.id_pacienteNavigation.apellidos,
                appointment.id_medicoNavigation.id_usuarioNavigation.nombres + " " +
                    appointment.id_medicoNavigation.id_usuarioNavigation.apellidos))
            .ToList();

        return Results.Ok(appointments);
    }

    private static async Task<IResult> CreateAppointmentAsync(
        MobileCreateAppointmentRequest request,
        ClaimsPrincipal principal,
        IDbContextFactory<OpticaDbContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out _))
        {
            return Results.Unauthorized();
        }

        if (!DateOnly.TryParseExact(
                request.FechaCita,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var appointmentDate) ||
            !TimeOnly.TryParse(request.HoraInicio, CultureInfo.InvariantCulture, DateTimeStyles.None, out var startTime))
        {
            return Results.BadRequest(new { message = "La fecha u hora de la cita no tiene un formato valido." });
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var doctor = await dbContext.tbl_medico
            .FirstOrDefaultAsync(current => current.id_medico == request.IdMedico && current.activo == true, cancellationToken);
        var patientExists = await dbContext.tbl_pacientes
            .AnyAsync(current => current.id_paciente == request.IdPaciente && current.activo == true, cancellationToken);

        if (doctor is null || !patientExists)
        {
            return Results.BadRequest(new { message = "El medico o paciente seleccionado no esta disponible." });
        }

        var duration = Math.Clamp(doctor.duracion_consulta_minutos ?? 30, 5, 480);
        var endTime = TimeOnly.TryParse(
            request.HoraFin,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsedEndTime)
                ? parsedEndTime
                : startTime.AddMinutes(duration);

        if (endTime <= startTime)
        {
            return Results.BadRequest(new { message = "La hora de finalizacion debe ser posterior a la hora de inicio." });
        }

        var conflict = await dbContext.tbl_citas
            .AsNoTracking()
            .Include(appointment => appointment.id_estadoNavigation)
            .AnyAsync(appointment =>
                appointment.id_medico == request.IdMedico &&
                appointment.fecha_cita == appointmentDate &&
                appointment.hora_inicio < endTime &&
                appointment.hora_fin > startTime &&
                (appointment.id_estadoNavigation == null ||
                 appointment.id_estadoNavigation.nombre_estado != "Cancelada"),
                cancellationToken);

        if (conflict)
        {
            return Results.Conflict(new { message = "El horario seleccionado ya no se encuentra disponible." });
        }

        var scheduledStatusId = await dbContext.tbl_estado_cita
            .AsNoTracking()
            .Where(status => status.nombre_estado == "Programada")
            .Select(status => (int?)status.id_estado)
            .FirstOrDefaultAsync(cancellationToken);

        var appointment = new tbl_citas
        {
            id_medico = request.IdMedico,
            id_paciente = request.IdPaciente,
            fecha_cita = appointmentDate,
            hora_inicio = startTime,
            hora_fin = endTime,
            tipo_cita = string.IsNullOrWhiteSpace(request.TipoCita)
                ? "Consulta general"
                : request.TipoCita.Trim(),
            motivo_cita = request.MotivoCita?.Trim(),
            id_estado = scheduledStatusId ?? 1,
            notificacion_enviada = false,
            recordatorio_24hrs = false,
            recordatorio_1hr = false,
            fecha_creacion = DateTime.Now,
            fecha_actualizacion = DateTime.Now,
            usuario_creacion = principal.Identity?.Name
        };

        dbContext.tbl_citas.Add(appointment);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/citas/{appointment.id_cita}", new
        {
            message = "Cita agendada exitosamente.",
            id_cita = appointment.id_cita
        });
    }

    private static bool TryGetUserId(ClaimsPrincipal principal, out int userId)
    {
        return int.TryParse(principal.FindFirstValue("uid"), out userId) && userId > 0;
    }

    private static string? LimitOrNull(string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static int ToMinutes(TimeOnly value) => value.Hour * 60 + value.Minute;

    private static string FormatMinutes(int minutes) => $"{minutes / 60:00}:{minutes % 60:00}";
}

public sealed record MobileDoctorResponse(
    [property: JsonPropertyName("id_medico")] int IdMedico,
    [property: JsonPropertyName("nombres")] string Nombres,
    [property: JsonPropertyName("apellidos")] string Apellidos,
    [property: JsonPropertyName("especialidad")] string Especialidad,
    [property: JsonPropertyName("duracion_consulta_minutos")] int DuracionConsultaMinutos,
    [property: JsonPropertyName("precio_consulta_base")] decimal PrecioConsultaBase);

public sealed record MobilePatientResponse(
    [property: JsonPropertyName("id_paciente")] int IdPaciente,
    [property: JsonPropertyName("codigo_paciente")] string? CodigoPaciente,
    [property: JsonPropertyName("cedula")] string Cedula,
    [property: JsonPropertyName("nombres")] string Nombres,
    [property: JsonPropertyName("apellidos")] string Apellidos,
    [property: JsonPropertyName("telefono")] string? Telefono,
    [property: JsonPropertyName("email")] string? Email)
{
    public static MobilePatientResponse FromEntity(tbl_paciente patient)
    {
        return new MobilePatientResponse(
            patient.id_paciente,
            patient.codigo_paciente,
            patient.cedula,
            patient.nombres,
            patient.apellidos,
            patient.telefono,
            patient.email);
    }
}

public sealed record MobileCreatePatientRequest(
    [property: JsonPropertyName("cedula")] string? Cedula,
    [property: JsonPropertyName("nombres")] string? Nombres,
    [property: JsonPropertyName("apellidos")] string? Apellidos,
    [property: JsonPropertyName("telefono")] string? Telefono,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("fecha_nacimiento")] string? FechaNacimiento,
    [property: JsonPropertyName("genero")] string? Genero,
    [property: JsonPropertyName("estado_civil")] string? EstadoCivil,
    [property: JsonPropertyName("ocupacion")] string? Ocupacion,
    [property: JsonPropertyName("direccion")] string? Direccion,
    [property: JsonPropertyName("observaciones")] string? Observaciones);

public sealed record MobileSlotResponse(
    [property: JsonPropertyName("hora")] string Hora,
    [property: JsonPropertyName("hora_fin")] string HoraFin,
    [property: JsonPropertyName("disponible")] bool Disponible);

public sealed record MobileSlotsResponse(
    [property: JsonPropertyName("disponible")] bool Disponible,
    [property: JsonPropertyName("slots")] IReadOnlyCollection<MobileSlotResponse> Slots);

public sealed record MobileAppointmentResponse(
    [property: JsonPropertyName("id_cita")] int IdCita,
    [property: JsonPropertyName("fecha_cita")] string FechaCita,
    [property: JsonPropertyName("hora_inicio")] string HoraInicio,
    [property: JsonPropertyName("hora_fin")] string HoraFin,
    [property: JsonPropertyName("tipo_cita")] string? TipoCita,
    [property: JsonPropertyName("motivo_cita")] string? MotivoCita,
    [property: JsonPropertyName("estado_cita")] string EstadoCita,
    [property: JsonPropertyName("paciente")] string Paciente,
    [property: JsonPropertyName("optometra")] string Optometra);

public sealed record MobileCreateAppointmentRequest(
    [property: JsonPropertyName("id_medico")] int IdMedico,
    [property: JsonPropertyName("id_paciente")] int IdPaciente,
    [property: JsonPropertyName("fecha_cita")] string FechaCita,
    [property: JsonPropertyName("hora_inicio")] string HoraInicio,
    [property: JsonPropertyName("hora_fin")] string? HoraFin,
    [property: JsonPropertyName("tipo_cita")] string? TipoCita,
    [property: JsonPropertyName("motivo_cita")] string? MotivoCita);
