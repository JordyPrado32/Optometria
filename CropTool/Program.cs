using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

const string connString = "Data Source=ARI;Initial Catalog=bd_optica_modelo_estrella;User ID=clase4b;Password=clase4b;Trust Server Certificate=True";

Console.WriteLine("Conectando directamente a SQL Server...");
using var conn = new SqlConnection(connString);
await conn.OpenAsync();

// 1. Asegurar la columna usa_lentes en tbl_historia_clinica_optometria
using (var cmd = new SqlCommand(@"
    IF OBJECT_ID('dbo.tbl_historia_clinica_optometria', 'U') IS NOT NULL
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tbl_historia_clinica_optometria') AND name = 'usa_lentes' AND is_nullable = 0)
        BEGIN
            ALTER TABLE dbo.tbl_historia_clinica_optometria ALTER COLUMN usa_lentes BIT NULL;
        END;
    END;
", conn))
{
    await cmd.ExecuteNonQueryAsync();
    Console.WriteLine("Columna usa_lentes verificada/modificada en la BD.");
}

// 2. Buscar paciente 'Ari' o 'Caceres'
int patientId = 0;
string patientNombres = "Ari";
string patientApellidos = "Cáceres";
string patientCedula = "1720304050";
string patientCode = "HC-00001";

using (var cmd = new SqlCommand(@"
    SELECT TOP 1 id_paciente, nombres, apellidos, cedula, codigo_paciente
    FROM dbo.tbl_paciente
    WHERE (nombres LIKE '%Ari%' AND apellidos LIKE '%Cacer%')
       OR (nombres LIKE '%Ari%' AND apellidos LIKE '%Cácer%')
       OR (nombres LIKE '%Ari%')
       OR (apellidos LIKE '%Cacer%')
       OR (apellidos LIKE '%Cácer%')
", conn))
{
    using var reader = await cmd.ExecuteReaderAsync();
    if (await reader.ReadAsync())
    {
        patientId = reader.GetInt32(0);
        patientNombres = reader.GetString(1);
        patientApellidos = reader.GetString(2);
        patientCedula = reader.IsDBNull(3) ? "1720304050" : reader.GetString(3);
        patientCode = reader.IsDBNull(4) ? $"HC-{patientId:D5}" : reader.GetString(4);
        Console.WriteLine($"Paciente encontrado: {patientNombres} {patientApellidos} (ID: {patientId}, Cédula: {patientCedula})");
    }
}

if (patientId == 0)
{
    Console.WriteLine("Creando paciente 'Ari Cáceres'...");
    using var cmd = new SqlCommand(@"
        INSERT INTO dbo.tbl_paciente (nombres, apellidos, cedula, fecha_nacimiento, edad, genero, estado_civil, ocupacion, direccion, telefono, email, observaciones, activo, fecha_registro)
        OUTPUT INSERTED.id_paciente
        VALUES (@nombres, @apellidos, @cedula, '2000-05-15', 24, 'Femenino', 'Soltera', 'Desarrollo de Software / Diseño UI', 'Quito, Ecuador', '0991234567', 'ari.caceres@optometria.local', 'Paciente con alta demanda en pantallas', 1, GETDATE());
    ", conn);
    cmd.Parameters.AddWithValue("@nombres", patientNombres);
    cmd.Parameters.AddWithValue("@apellidos", patientApellidos);
    cmd.Parameters.AddWithValue("@cedula", patientCedula);
    patientId = (int)(await cmd.ExecuteScalarAsync())!;
    patientCode = $"HC-{patientId:D5}";
    Console.WriteLine($"Paciente creado con ID: {patientId}");
}

// 3. Buscar un usuario médico activo
int doctorUserId = 1;
using (var cmd = new SqlCommand("SELECT TOP 1 id_usuario FROM dbo.tbl_usuario WHERE activo = 1", conn))
{
    var doc = await cmd.ExecuteScalarAsync();
    if (doc != null && doc != DBNull.Value) doctorUserId = (int)doc;
}

// 4. Preparar todos los bloques JSON
var anamnesisGuiadaObj = new
{
    MotivoPrincipal = "Control visual",
    Inicio = "Recurrente",
    DuracionValor = "13",
    DuracionUnidad = "anios",
    Lateralidad = "AO",
    Intensidad = "Moderada",
    Desencadenantes = "Pantallas, polvo",
    Aliviantes = "usar lentes frecuentemente, pausas activas",
    Sintomas = new[] { "Vision borrosa", "Destellos" },
    BanderasAlerta = new[] { "Destellos nuevos" },
    NotasAdicionales = "Movimientos oculares suaves, precisos y completos (SPEC) en las 9 posiciones de mirada. Test de Worth: Fusión normal (ve 4 luces)."
};

var antecedentesObj = new
{
    PersonalesOculares = "Astigmatismo",
    PersonalesGenerales = "ninguno",
    FamiliaresOculares = "Madre, hermana, padre, abuelos paternos y maternos",
    FamiliaresGenerales = "Abuela materna: deabetes, tiroides"
};

var lentesObj = new
{
    TipoLente = "Monofocal",
    Material = "Policarbonato",
    Filtro = "Antirreflejo básico",
    TiempoUso = "1 año y medio",
    DistPupilar = "63",
    OjoDerecho = "0.00 -2.50 x 25°",
    OjoIzquierdo = "0.00 -2.75 x 25°",
    AmbosOjos = "20/20",
    Add = "",
    Prismas = "",
    Observaciones = "Cristales con múltiples rayas en el centro óptico. El armazón está desajustado de las varillas."
};

var visualObj = new
{
    OdVlSc = "20/70",
    OiVlSc = "20/70",
    AoVlSc = "20/60",
    Ph = "20/20",
    OdVpSc = "20/40",
    OiVpSc = "20/50",
    AoVpSc = "20/40",
    Dominancia = "Ojo derecho",
    OdVlCc = "20/20",
    OiVlCc = "20/20",
    AoVlCc = "20/20",
    OdVpCc = "20/20",
    OiVpCc = "20/20",
    AoVpCc = "20/20"
};

var eyeGraphicOd = new
{
    Notes = "Hiperemia conjuntival leve. Película lagrimal disminuida asociado a uso continuo de pantallas.",
    Zones = new Dictionary<string, string>
    {
        ["Superior"] = "Normal",
        ["Inferior"] = "Normal",
        ["Nasal"] = "Observacion",
        ["Temporal"] = "Observacion",
        ["Centro"] = "Normal",
        ["Pupila"] = "Normal"
    }
};

var eyeGraphicOi = new
{
    Notes = "Hiperemia conjuntival leve. Película lagrimal disminuida asociado a uso continuo de pantallas.",
    Zones = new Dictionary<string, string>
    {
        ["Superior"] = "Normal",
        ["Inferior"] = "Normal",
        ["Nasal"] = "Observacion",
        ["Temporal"] = "Observacion",
        ["Centro"] = "Normal",
        ["Pupila"] = "Normal"
    }
};

string graphicOdJson = JsonSerializer.Serialize(eyeGraphicOd);
string graphicOiJson = JsonSerializer.Serialize(eyeGraphicOi);

var biomicroscopiaObj = new
{
    GraphicOd = graphicOdJson,
    GraphicOi = graphicOiJson,
    OrbitaOd = "Simétricas, sin alteraciones",
    OrbitaOi = "Simétricas, sin alteraciones",
    ParpadosOd = "Bordes limpios, sin inflamación. Posición normal",
    ParpadosOi = "Bordes limpios, sin inflamación. Posición normal",
    LagrimalOd = "Puntos permeables. Película lagrimal levemente disminuida",
    LagrimalOi = "Puntos permeables. Película lagrimal levemente disminuida",
    ConjuntivaOd = "Hiperemia leve (enrojecimiento). Esclera blanca y anictérica",
    ConjuntivaOi = "Hiperemia leve (enrojecimiento). Esclera blanca y anictérica",
    CorneaOd = "Transparente, sin opacidades ni cicatrices. Cámara profunda",
    CorneaOi = "Transparente, sin opacidades ni cicatrices. Cámara profunda",
    IrisOd = "Pupilas isocóricas y normorreactivas a la luz.",
    IrisOi = "Pupilas isocóricas y normorreactivas a la luz.",
    CristalinoOd = "Transparente, sin opacidades",
    CristalinoOi = "Transparente, sin opacidades",
    TestAdicionalesOd = "Test de Schirmer: 10 mm en 5 min",
    TestAdicionalesOi = "Test de Schirmer: 10 mm en 5 min",
    HallazgosOd = "Hiperemia conjuntival leve. Película lagrimal disminuida asociado a uso continuo de pantallas.",
    HallazgosOi = "Hiperemia conjuntival leve. Película lagrimal disminuida asociado a uso continuo de pantallas."
};

var oftalmoscopiaObj = new
{
    PapilaOd = "Papila de bordes netos, coloración rosada normal, excavación 0.3",
    PapilaOi = "Papila de bordes netos, coloración rosada normal, excavación 0.3",
    MaculaOd = "Brillo foveal presente, sin alteraciones tróficas",
    MaculaOi = "Brillo foveal presente, sin alteraciones tróficas",
    RetinaPerifericaOd = "Aplicada en 360°, sin desgarros ni hemorragias",
    RetinaPerifericaOi = "Aplicada en 360°, sin desgarros ni hemorragias",
    HallazgosOd = "Fondo de ojo normal sin signos patológicos",
    HallazgosOi = "Fondo de ojo normal sin signos patológicos"
};

var motorObj = new
{
    CoverTestLejos = "Ortotropia",
    CoverTestCerca = "Ortotropia",
    Resumen = "Ortotropia. Movimientos oculares suaves, precisos y completos (SPEC) en las 9 posiciones de mirada. Test de Worth: Fusión normal."
};

var keratometriaObj = new
{
    OdK1 = "42.50",
    OdK2 = "45.00",
    OdEje = "25",
    OiK1 = "42.25",
    OiK2 = "45.00",
    OiEje = "25",
    Observaciones = "OD: -2.50 x 25° / OI: -2.75 x 25°. Astigmatismo corneal simétrico, coincidente con la refracción final."
};

var refractionObj = new
{
    OdEsfera = "0.00",
    OdCilindro = "-2.50",
    OdEje = "25",
    OdAv = "20/20",
    OiEsfera = "0.00",
    OiCilindro = "-2.75",
    OiEje = "25",
    OiAv = "20/20",
    Dp = "63",
    RxEstaticaDinamica = "Retinoscopía estática: OD: Neutro -2.75 x 25° / OI: Neutro -3.00 x 25°. Reflejos nítidos con leve patrón en tijera (típico en astigmatismos moderados). Retinoscopía dinámica (MEM) dentro de rangos normales (+0.50), descartando espasmo acomodativo por exceso de acomodación.",
    SubjetivoAfinacion = "Subjetivo alcanza agudeza visual 20/20 en ambos ojos. Afinación con Cilindro Cruzado de Jackson (CCJ) confirma el eje a 25° y afina el poder cilíndrico a -2.50 (OD) y -2.75 (OI). Balance binocular con prismas de Risley muestra equilibrio perfecto, sin dominancia forzada.",
    PruebaAmbulatoria = "Se coloca montura de prueba. Paciente camina por el consultorio y revisa texto en pantalla por 15 minutos. Tolerancia al 100%, no refiere mareos, no percibe el piso inclinado ni distorsiones periféricas. RX Final a prescribir -> OD: 0.00 -2.50 x 25° / OI: 0.00 -2.75 x 25° (D.P. 63mm).",
    Observaciones = "Dada la alta exigencia visual en desarrollo de software y diseño de interfaces, se prescribe material de alto índice (1.60 o 1.67) en diseño asférico para reducir el grosor de los bordes. Obligatorio aplicar filtro Blue Cut y antirreflejo superhidrofóbico para mitigar el deslumbramiento en monitores."
};

var diagnosticoObj = new
{
    DiagnosticoOd = "Astigmatismo miópico simple",
    DiagnosticoOi = "Astigmatismo miópico simple",
    Cie10 = "H52.2",
    PatologicoPresuntivo = "Síndrome Visual Informático / Ojo seco leve por evaporación",
    TratamientoConducta = "1. Prescripción de corrección óptica permanente. 2. Dispensación de lentes asféricos (alto índice 1.67) con tratamiento Antirreflejo Superhidrofóbico y filtro bloqueador de luz azul. 3. Educación en higiene visual: Aplicar la regla 20-20-20 (cada 20 minutos, mirar a 20 pies por 20 segundos) durante jornadas de programación. 4. Lubricante ocular (lágrimas artificiales sin preservantes) 1 gota en ambos ojos cada 8 horas o a demanda.",
    ExamenesIndicados = "Consulta Optométrica Integral. Refracción Clínica. Test de Schirmer.",
    MedicamentosRecetados = "Lágrimas artificiales al 0.15% (sin preservantes) - 1 frasco.",
    PrescripcionItems = new[]
    {
        new { TipoItem = "Medicamento", ProductoId = 0, NombreItem = "Lágrimas artificiales al 0.15% (sin preservantes)", Cantidad = 1, Unidad = "frasco", Indicaciones = "1 gota en ambos ojos cada 8 horas o a demanda", EnviarAFacturacion = true, Observaciones = "" },
        new { TipoItem = "Insumo", ProductoId = 0, NombreItem = "Lentes Asféricos Alto Índice 1.67 con Blue Cut y Antirreflejo Superhidrofóbico", Cantidad = 1, Unidad = "par", Indicaciones = "Uso permanente para visión de lejos y trabajo en pantallas", EnviarAFacturacion = true, Observaciones = "" },
        new { TipoItem = "Examen", ProductoId = 0, NombreItem = "Consulta Optométrica Integral y Refracción Clínica", Cantidad = 1, Unidad = "servicio", Indicaciones = "Evaluación refractiva y biomicroscopía completa", EnviarAFacturacion = true, Observaciones = "" }
    }
};

var seguimientoObj = new
{
    RequiereNuevaCita = true,
    Prioridad = "Control",
    DiasSugeridos = 90,
    Motivo = "Control visual y evaluación de adaptación a nuevas lunas asféricas 1.67",
    Observaciones = "Cita sugerida en 90 días (3 meses)."
};

var consentimientoObj = new
{
    Autorizado = true,
    Nombre = $"{patientNombres} {patientApellidos}".Trim(),
    Cedula = patientCedula,
    FechaConsentimiento = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
    Texto = "Permito que se me realicen pruebas no invasivas para la evaluacion visual y el uso clinico de los resultados.",
    FirmaReferencia = $"{patientNombres} {patientApellidos}"
};

var fullEditorObj = new
{
    NumeroHistoria = patientCode,
    Consultorio = "Consultorio 1 - Optometría Integral",
    LlaveClinica = $"CLI-OPTO-{patientId}",
    LugarNacimiento = "Quito",
    Procedencia = "Pichincha",
    UltimoControl = "Hace 1 año y medio",
    MotivoConsulta = "Control visual rutinario por visión borrosa y destellos refractivos en pantallas",
    Anamnesis = "Resumen guiado: Motivo principal: Control visual. inicio recurrente, duracion 13 anios, lateralidad ao. Sintomas: Vision borrosa, Destellos. Intensidad: Moderada. Desencadenantes: Pantallas, polvo. Aliviantes: usar lentes frecuentemente, pausas activas. Alertas: Destellos nuevos. Notas: Movimientos oculares suaves, precisos y completos (SPEC) en las 9 posiciones de mirada. Test de Worth: Fusión normal (ve 4 luces).",
    UsaLentes = true,
    ObservacionesGenerales = "Paciente colaboradora en todas las pruebas. Alta demanda acomodativa y refractiva en monitores.",
    NombreExaminador = "Alejandra Cáceres",
    NivelParaleloJornada = "4to B matutina",
    AnamnesisGuiada = anamnesisGuiadaObj,
    Antecedentes = antecedentesObj,
    Lentes = lentesObj,
    Visual = visualObj,
    Biomicroscopia = biomicroscopiaObj,
    Oftalmoscopia = oftalmoscopiaObj,
    Motor = motorObj,
    Keratometria = keratometriaObj,
    Refraction = refractionObj,
    Diagnostico = diagnosticoObj,
    Seguimiento = seguimientoObj,
    Consentimiento = consentimientoObj,
    ExamenesClinicos = new object[] { }
};

var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = null };
string anamnesisJson = JsonSerializer.Serialize(anamnesisGuiadaObj, jsonOptions);
string antecedentesJson = JsonSerializer.Serialize(antecedentesObj, jsonOptions);
string lentesJson = JsonSerializer.Serialize(lentesObj, jsonOptions);
string visualJson = JsonSerializer.Serialize(visualObj, jsonOptions);
string biomicroscopiaJson = JsonSerializer.Serialize(biomicroscopiaObj, jsonOptions);
string oftalmoscopiaJson = JsonSerializer.Serialize(oftalmoscopiaObj, jsonOptions);
string motorJson = JsonSerializer.Serialize(motorObj, jsonOptions);
string keratometriaJson = JsonSerializer.Serialize(keratometriaObj, jsonOptions);
string refractionJson = JsonSerializer.Serialize(refractionObj, jsonOptions);
string diagnosticoJson = JsonSerializer.Serialize(diagnosticoObj, jsonOptions);
string consentimientoJson = JsonSerializer.Serialize(consentimientoObj, jsonOptions);
string fullPayloadJson = JsonSerializer.Serialize(fullEditorObj, jsonOptions);

// 5. Insertar o actualizar tbl_historia_clinica_optometria
int historyId = 0;
using (var cmd = new SqlCommand("SELECT TOP 1 id_historia_clinica FROM dbo.tbl_historia_clinica_optometria WHERE id_paciente = @id_paciente", conn))
{
    cmd.Parameters.AddWithValue("@id_paciente", patientId);
    var h = await cmd.ExecuteScalarAsync();
    if (h != null && h != DBNull.Value) historyId = (int)h;
}

if (historyId == 0)
{
    using var cmd = new SqlCommand(@"
        INSERT INTO dbo.tbl_historia_clinica_optometria (
            id_paciente, id_optometra_apertura, id_optometra_ultima_actualizacion,
            fecha_apertura, fecha_ultima_actualizacion, numero_historia,
            consultorio, llave_clinica, lugar_nacimiento, procedencia, ultimo_control,
            datos_apertura_json, motivo_consulta, anamnesis, antecedentes_json,
            usa_lentes, lentes_json, agudeza_visual_json, biomicroscopia_json,
            oftalmoscopia_json, examen_motor_json, queratometria_json,
            refraccion_json, diagnostico_json, observaciones_generales,
            nombre_examinador, nivel_paralelo_jornada, consentimiento_json, activo
        )
        OUTPUT INSERTED.id_historia_clinica
        VALUES (
            @id_paciente, @id_optometra, @id_optometra,
            GETDATE(), GETDATE(), @numero_historia,
            'Consultorio 1 - Optometría Integral', @llave_clinica, 'Quito', 'Pichincha', 'Hace 1 año y medio',
            '{}', @motivo_consulta, @anamnesis, @antecedentes_json,
            1, @lentes_json, @visual_json, @biomicroscopia_json,
            @oftalmoscopia_json, @motor_json, @keratometria_json,
            @refraccion_json, @diagnostico_json, 'Paciente colaboradora en todas las pruebas. Alta demanda acomodativa y refractiva en monitores.',
            'Alejandra Cáceres', '4to B matutina', @consentimiento_json, 1
        );
    ", conn);
    cmd.Parameters.AddWithValue("@id_paciente", patientId);
    cmd.Parameters.AddWithValue("@id_optometra", doctorUserId);
    cmd.Parameters.AddWithValue("@numero_historia", patientCode);
    cmd.Parameters.AddWithValue("@llave_clinica", $"CLI-OPTO-{patientId}");
    cmd.Parameters.AddWithValue("@motivo_consulta", "Control visual rutinario por visión borrosa y destellos refractivos en pantallas");
    cmd.Parameters.AddWithValue("@anamnesis", "Resumen guiado: Motivo principal: Control visual. inicio recurrente, duracion 13 anios, lateralidad ao. Sintomas: Vision borrosa, Destellos. Intensidad: Moderada. Desencadenantes: Pantallas, polvo. Aliviantes: usar lentes frecuentemente, pausas activas. Alertas: Destellos nuevos. Notas: Movimientos oculares suaves, precisos y completos (SPEC) en las 9 posiciones de mirada. Test de Worth: Fusión normal (ve 4 luces).");
    cmd.Parameters.AddWithValue("@antecedentes_json", antecedentesJson);
    cmd.Parameters.AddWithValue("@lentes_json", lentesJson);
    cmd.Parameters.AddWithValue("@visual_json", visualJson);
    cmd.Parameters.AddWithValue("@biomicroscopia_json", biomicroscopiaJson);
    cmd.Parameters.AddWithValue("@oftalmoscopia_json", oftalmoscopiaJson);
    cmd.Parameters.AddWithValue("@motor_json", motorJson);
    cmd.Parameters.AddWithValue("@keratometria_json", keratometriaJson);
    cmd.Parameters.AddWithValue("@refraccion_json", refractionJson);
    cmd.Parameters.AddWithValue("@diagnostico_json", diagnosticoJson);
    cmd.Parameters.AddWithValue("@consentimiento_json", consentimientoJson);
    historyId = (int)(await cmd.ExecuteScalarAsync())!;
    Console.WriteLine($"tbl_historia_clinica_optometria insertada con ID: {historyId}");
}
else
{
    using var cmd = new SqlCommand(@"
        UPDATE dbo.tbl_historia_clinica_optometria SET
            id_optometra_ultima_actualizacion = @id_optometra,
            fecha_ultima_actualizacion = GETDATE(),
            numero_historia = @numero_historia,
            consultorio = 'Consultorio 1 - Optometría Integral',
            llave_clinica = @llave_clinica,
            motivo_consulta = @motivo_consulta,
            anamnesis = @anamnesis,
            antecedentes_json = @antecedentes_json,
            usa_lentes = 1,
            lentes_json = @lentes_json,
            agudeza_visual_json = @visual_json,
            biomicroscopia_json = @biomicroscopia_json,
            oftalmoscopia_json = @oftalmoscopia_json,
            examen_motor_json = @motor_json,
            queratometria_json = @keratometria_json,
            refraccion_json = @refraccion_json,
            diagnostico_json = @diagnostico_json,
            observaciones_generales = 'Paciente colaboradora en todas las pruebas. Alta demanda acomodativa y refractiva en monitores.',
            nombre_examinador = 'Alejandra Cáceres',
            nivel_paralelo_jornada = '4to B matutina',
            consentimiento_json = @consentimiento_json,
            activo = 1
        WHERE id_historia_clinica = @id_historia;
    ", conn);
    cmd.Parameters.AddWithValue("@id_historia", historyId);
    cmd.Parameters.AddWithValue("@id_optometra", doctorUserId);
    cmd.Parameters.AddWithValue("@numero_historia", patientCode);
    cmd.Parameters.AddWithValue("@llave_clinica", $"CLI-OPTO-{patientId}");
    cmd.Parameters.AddWithValue("@motivo_consulta", "Control visual rutinario por visión borrosa y destellos refractivos en pantallas");
    cmd.Parameters.AddWithValue("@anamnesis", "Resumen guiado: Motivo principal: Control visual. inicio recurrente, duracion 13 anios, lateralidad ao. Sintomas: Vision borrosa, Destellos. Intensidad: Moderada. Desencadenantes: Pantallas, polvo. Aliviantes: usar lentes frecuentemente, pausas activas. Alertas: Destellos nuevos. Notas: Movimientos oculares suaves, precisos y completos (SPEC) en las 9 posiciones de mirada. Test de Worth: Fusión normal (ve 4 luces).");
    cmd.Parameters.AddWithValue("@antecedentes_json", antecedentesJson);
    cmd.Parameters.AddWithValue("@lentes_json", lentesJson);
    cmd.Parameters.AddWithValue("@visual_json", visualJson);
    cmd.Parameters.AddWithValue("@biomicroscopia_json", biomicroscopiaJson);
    cmd.Parameters.AddWithValue("@oftalmoscopia_json", oftalmoscopiaJson);
    cmd.Parameters.AddWithValue("@motor_json", motorJson);
    cmd.Parameters.AddWithValue("@keratometria_json", keratometriaJson);
    cmd.Parameters.AddWithValue("@refraccion_json", refractionJson);
    cmd.Parameters.AddWithValue("@diagnostico_json", diagnosticoJson);
    cmd.Parameters.AddWithValue("@consentimiento_json", consentimientoJson);
    await cmd.ExecuteNonQueryAsync();
    Console.WriteLine($"tbl_historia_clinica_optometria actualizada con ID: {historyId}");
}

// 6. Insertar tbl_consulta
int consultationId = 0;
using (var cmd = new SqlCommand(@"
    INSERT INTO dbo.tbl_consulta (
        id_paciente, id_optometra, fecha_consulta, motivo_consulta, historia_clinica,
        antecedentes_personales, antecedentes_familiares, antecedentes_oculares,
        enfermedades_previas, usa_lentes, detalle_usa_lentes, examenes_preliminares,
        evaluaciones, examenes_varios, medicamentos, notas
    )
    OUTPUT INSERTED.id_consulta
    VALUES (
        @id_paciente, @id_optometra, GETDATE(), @motivo_consulta, @historia_clinica,
        'ninguno', 'Abuela materna: deabetes, tiroides', 'Astigmatismo | Madre, hermana, padre, abuelos paternos y maternos',
        'Control visual. inicio recurrente, duracion 13 anios', 1, 'Cristales con múltiples rayas en el centro óptico. El armazón está desajustado de las varillas.',
        'AV s/c: OD 20/70, OI 20/70, AO 20/60. PH 20/20. Dominancia: Ojo derecho',
        '1. Prescripción de corrección óptica permanente. 2. Dispensación de lentes asféricos (alto índice 1.67) con Blue Cut y Antirreflejo Superhidrofóbico. 3. Higiene visual 20-20-20. 4. Lágrimas artificiales.',
        'Consulta Optométrica Integral. Refracción Clínica. Test de Schirmer.',
        'Lágrimas artificiales al 0.15% (sin preservantes) - 1 frasco.',
        'Paciente colaboradora en todas las pruebas. Alta demanda acomodativa y refractiva en monitores.'
    );
", conn))
{
    cmd.Parameters.AddWithValue("@id_paciente", patientId);
    cmd.Parameters.AddWithValue("@id_optometra", doctorUserId);
    cmd.Parameters.AddWithValue("@motivo_consulta", "Control visual rutinario por visión borrosa y destellos refractivos en pantallas");
    cmd.Parameters.AddWithValue("@historia_clinica", patientCode);
    consultationId = (int)(await cmd.ExecuteScalarAsync())!;
    Console.WriteLine($"tbl_consulta creada con ID: {consultationId}");
}

// 7. Insertar tbl_historia_clinica_optometria_evento
int eventId = 0;
using (var cmd = new SqlCommand(@"
    INSERT INTO dbo.tbl_historia_clinica_optometria_evento (
        id_historia_clinica, id_paciente, id_consulta, id_optometra,
        fecha_evento, fecha_ultima_actualizacion, estado, resumen_progreso,
        motivo_consulta, anamnesis, diagnostico_resumen, cie10,
        payload_json, consentimiento_firmado, es_legado_migrado, activo
    )
    OUTPUT INSERTED.id_historia_evento
    VALUES (
        @id_historia_clinica, @id_paciente, @id_consulta, @id_optometra,
        GETDATE(), GETDATE(), 'Cerrada', 100,
        @motivo_consulta, @anamnesis, 'Astigmatismo miópico simple (H52.2)', 'H52.2',
        @payload_json, 1, 0, 1
    );
", conn))
{
    cmd.Parameters.AddWithValue("@id_historia_clinica", historyId);
    cmd.Parameters.AddWithValue("@id_paciente", patientId);
    cmd.Parameters.AddWithValue("@id_consulta", consultationId);
    cmd.Parameters.AddWithValue("@id_optometra", doctorUserId);
    cmd.Parameters.AddWithValue("@motivo_consulta", "Control visual rutinario por visión borrosa y destellos refractivos en pantallas");
    cmd.Parameters.AddWithValue("@anamnesis", "Resumen guiado: Motivo principal: Control visual. inicio recurrente, duracion 13 anios, lateralidad ao. Sintomas: Vision borrosa, Destellos.");
    cmd.Parameters.AddWithValue("@payload_json", fullPayloadJson);
    eventId = (int)(await cmd.ExecuteScalarAsync())!;
    Console.WriteLine($"tbl_historia_clinica_optometria_evento creada con ID: {eventId}");
}

Console.WriteLine("==================================================================");
Console.WriteLine($"¡TODO GUARDADO EXITOSAMENTE AL 100% PARA {patientNombres} {patientApellidos}!");
Console.WriteLine($"Historia ID: {historyId} | Consulta ID: {consultationId} | Evento ID: {eventId}");
Console.WriteLine("==================================================================");
