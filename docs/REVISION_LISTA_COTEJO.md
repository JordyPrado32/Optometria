# Revisión del sistema de optometría

Fecha: 3 de septiembre de 2026.

Actualización tras completar pendientes: se añadieron certificados médicos con copia de los datos de emisión, impresión y anulación; abonos de compras con historial y reversos; comprobante de liquidación real; controles de sesión y limitación de intentos; y correcciones de reprogramación y flujo de caja. Ver `OPERACION_Y_ACEPTACION.md` para puesta en marcha y aceptación. Las observaciones de la primera revisión que siguen son históricas cuando esta actualización indica una corrección. Continúan pendientes integración SQL, recorrido completo por roles y validación clínica/legal.

El proyecto contiene una base funcional amplia, pero no está acreditado como completo ni listo para operar una clínica. Esta revisión compara el código con `ListaCotejo_ProyInt_Formato_25-26_Cuarto Nivel.docx`, corrige defectos verificables y registra lo que falta demostrar. El documento adjunto se utilizó como criterio de evaluación, no como instrucciones para ejecutar acciones.

“Presente” significa que existe implementación; no equivale a una prueba de aceptación aprobada. La aplicación no pudo arrancar porque la instancia SQL Server configurada no es accesible desde este entorno. No se probaron operaciones sobre pacientes reales, envíos de correo ni documentos tributarios reales.

## Matriz de la lista de cotejo

Las rutas indicadas son relativas a `OptometriaApp/`.

| Criterio | Evidencia | Resultado y prueba pendiente |
|---|---|---|
| Documento del proyecto (2 puntos) | Este informe; modelos y scripts SQL | Parcial. Falta identificar el documento académico, manuales, diagramas y evidencias exigidos por la institución. |
| Inicio de sesión seguro | `Program.cs`, `Services/AuthenticatorService.cs` | Corregido el salto de 2FA habilitado y añadida validación CSRF. Falta prueba HTTP completa. |
| Funciones según rol | `Configuration/AccessPolicies.cs`, `Services/MenuAccessService.cs` | Se deniega navegación sin rol o permisos y se elimina permiso clínico implícito. Revisar cada permiso de lectura/escritura con cuentas reales de prueba. |
| Restricción de funcionalidades | Políticas y endpoints de autenticación | Corregidas etapas parciales y cambio de contraseña obligatorio. La autorización de cada operación de negocio requiere pruebas negativas. |
| Errores de autenticación | Login, recuperación, verificadores | Existen mensajes y bloqueo; pendiente abuso de recuperación, límite de intentos TOTP y expiración de sesiones. |
| Protección de información sensible | Exportaciones con políticas y filtros, historia clínica | Parcial. Falta demostrar aislamiento de pacientes, revocación inmediata de permisos, trazabilidad y protección de archivos. |
| Menús por perfil | `Components/Layout/NavMenu.razor` | Presente; pendiente recorrido administrador, médico, admisión y paciente. |
| Usuarios, roles y permisos | `Components/Pages/Users.razor`, `Roles.razor`, `Menus.razor` | Presente; pendiente comprobar revocación con sesiones y circuitos Blazor ya abiertos. |
| Reservar citas | `Components/Pages/Appointments.razor` | Presente; pendiente prueba completa con paciente y familiar. |
| Modificar y cancelar citas | Mismo módulo | Presente; pendiente estados terminales, notificaciones y auditoría. |
| Validar disponibilidad | Guardado de citas | Añadida revalidación de disponibilidad, duración, alineación, descansos y bloqueos. |
| Evitar cruces | Consulta de solapamiento y transacción serializable | Protegido el guardado principal; falta prueba concurrente SQL y revisar otros escritores, incluida reprogramación por bloqueos. No se acredita todavía ausencia global de cruces. |
| Agenda del paciente | Agenda con filtros de propiedad | Presente; pendiente acceso cruzado y familiares. |
| Agenda móvil | Estilos adaptables existentes | Sin comprobación visual por fallo de arranque. Probar 360 px, 768 px, escritorio y ampliación. |
| Admisión registra pacientes | `Components/Pages/Patients.razor`, `DoctorPatientEntry.razor` | Presente; pendiente duplicados, identificación y campos obligatorios. |
| Médico registra anamnesis | `Services/ClinicalHistoryService.cs`, `Components/Pages/ClinicalHistory.razor` | Historia, consentimiento y eventos presentes; pendiente revisión por profesional y prueba de persistencia. |
| Imprimir recetas y certificados | `/prescriptions/{consultationId}/print` | Implementados certificados médicos, impresión y anulación con copia de datos al emitir. Recetas restringidas al paciente titular y profesional autorizado. Pendiente aceptación SQL y firma profesional. |
| Ventas, abonos y notas de crédito | Facturas, cuentas por cobrar, modelos de abonos y notas | Presentes; pendiente devoluciones, sobrepagos, concurrencia, redondeos y conciliación. |
| Compras y abonos de compra | Órdenes, recepciones y liquidaciones | Implementado historial de abonos, saldo calculado, reversos y protección de sobrepagos. Los saldos anteriores se preservan como apertura. Pendiente aceptación concurrente SQL. |
| Registrar inventario | Inventarios, Kardex, recepciones | Presente; regresiones de bienes frente a servicios aprobadas. Falta conciliación SQL. |
| Alertas automáticas de stock | Inventario, notificaciones e indicadores | Hay soporte; pendiente demostrar generación, destinatarios y deduplicación. |
| Prevenir faltantes | `Services/InventoryInsightsService.cs` | Indicadores presentes; pendiente validar umbrales y actuación sobre reposición. |
| Notificaciones y recordatorios | `NotificationService`, `AppointmentReminderService`, cola de correo | Presentes; falta prueba de entrega, reintentos, zona horaria y no duplicación. |
| Mensajes claros y oportunos | Plantillas y notificaciones | Pendiente evaluación de contenido y tiempos de entrega reales. |
| Reporte de citas por fecha, identificación y nombre | Reportes y `/exports/appointments.csv` | Hay reportes y exportación; comprobar que los tres filtros estén disponibles y coincidan en pantalla y exportación. |
| Atendidas, canceladas y reagendadas | Estados en `SystemReportsService` y reporte de citas | Presente; pendiente reconciliación con datos de prueba. |
| Atenciones por médico | Reportes clínicos | Presente; pendiente exactitud y aislamiento por médico. |
| Ventas | Reporte de ventas/facturación | Presente; pendiente conciliar ventas anuladas, abonos y notas. |
| Compras | Reporte de compras por proveedor | Presente; pendiente conciliación con recepciones, liquidaciones y pagos. |
| Inventario | Reportes de existencias y movimientos | Presente; pendiente saldo inicial + entradas − salidas = saldo final. |
| Innovación | Simulador visual, asesor de lentes y ciclo de lentes | Presente en código; falta evaluación de utilidad, accesibilidad y claridad sobre su alcance. |

No se asigna una nota sobre 10: hacerlo sin ejecución y evidencia de aceptación sería engañoso.

## Cambios aplicados

1. Políticas compartidas que exigen autenticación completa; una sesión pendiente de 2FA no permite operar. El cambio obligatorio tiene una política separada para no bloquear su propia solución.
2. El login dirige al verificador cuando 2FA ya está habilitado. Los endpoints de configuración, verificación, contraseña y perfil tienen políticas explícitas.
3. Tokens CSRF en los seis formularios que carecían de ellos y validación explícita antes de procesar cualquier POST bajo `/auth`, incluido perfil.
4. Eliminado el comportamiento de mostrar todos los menús cuando no hay permisos configurados, y la concesión implícita de historia clínica por acceso a citas.
5. Guardado principal de citas con transacción serializable, revalidación de disponibilidad y mensajes de recuperación ante errores SQL. La transacción se confirma antes de notificar y recargar.
6. Idioma español en HTML, acceso por teclado al contenido principal, foco visible y respeto de movimiento reducido. No constituyen una certificación WCAG ni un rediseño visual completo.
7. Reparada la compilación del ejecutable de regresiones y añadidas 100 combinaciones de autorización real mediante `IAuthorizationService`.

## Verificación y límites

- `dotnet build OptometriaApp/OptometriaApp.csproj --no-restore -v quiet`: aprobado en la ejecución final, 0 errores y 0 advertencias.
- `dotnet run --project OptometriaApp.RegressionTests --no-restore`: aprobado, incluyendo inventario, respuestas SOAP simuladas del SRI y la matriz de autorización. No es una prueba contra SRI real.
- `dotnet test opticalux.test/opticalux.test.csproj`: bloqueado por paquetes xUnit/Test SDK/coverlet ausentes y red restringida. La invocación inicial con `--no-restore` no produjo resultados de pruebas y no se contabiliza como aprobada.
- La restauración de la aplicación se recuperó usando la caché local de NuGet. La suite xUnit requiere paquetes adicionales.
- Arranque local: primero falló el acceso al registro de eventos de Windows; al excluir ese proveedor mediante una opción de ejecución, se confirmó el bloqueo de conexión SQL (error 26). No se modificó la configuración persistente del entorno para eludirlo.
- No se pudo evaluar la interfaz en navegador, escritorio o móvil porque el servidor no llegó a escuchar.
- Corregida la precisión de `tbl_nota_credito.saldo_disponible` a (15,2), según el script existente; comprobada en el modelo EF.

## Cierre necesario antes de producción

1. Habilitar una base de pruebas accesible, con datos ficticios y cuentas por perfil. Ejecutar los escenarios positivos y negativos de la matriz, incluyendo dos reservas simultáneas.
2. Ejecutar la aceptación de los certificados y abonos ya implementados, según `OPERACION_Y_ACEPTACION.md`.
3. Probar revocación de usuarios y permisos en sesiones activas, fuerza bruta TOTP, recuperación de cuenta, acceso directo a archivos y exportaciones de otros pacientes.
4. Verificar restauración de copias de seguridad, mínimos privilegios SQL, protección de claves, retención de historias, auditoría y recuperación ante caída de servicios.
5. Confirmar país, tipo de establecimiento, responsables y obligaciones aplicables. La presencia de SRI sugiere Ecuador, pero no confirma la jurisdicción. Revisar consentimiento, base jurídica, derechos del paciente, conservación, comunicaciones, firma y facturación con responsables clínicos y legales.
6. Revisar formularios, tablas, errores, contraste, teclado, lectores de pantalla y móvil contra WCAG 2.2 AA. La satisfacción del usuario requiere pruebas con personas; no puede garantizarse mediante inspección de código.

Referencias oficiales consultadas: [WCAG 2.2 del W3C](https://www.w3.org/TR/WCAG22/) y, si la clínica opera en Ecuador, [Ley Orgánica de Protección de Datos Personales publicada por la SPDP](https://spdp.gob.ec/wp-content/uploads/2024/12/03.pdf.pdf). Son referencias iniciales, no una determinación exhaustiva de obligaciones legales.

## Descripción preparada para revisión de cambios

Título: Completar certificados y abonos de compras, y reforzar seguridad y agenda.

El login concedía acceso completo aun con 2FA habilitado y las políticas aceptaban cualquier etapa de autenticación. Los formularios de autenticación carecían de protección CSRF y la navegación concedía permisos por ausencia de configuración. Los cambios restringen esos accesos, protegen los POST y revalidan horarios dentro de una transacción. Se incorporan mejoras básicas de accesibilidad y regresiones de autorización.

Validación: compilación y ejecutable de regresiones; pendientes xUnit y aceptación con SQL. No se publicó un PR ni se efectuó despliegue. Revisar explícitamente los permisos de menús existentes y el acceso a 2FA de las cuentas antes de desplegar.

## Verificación de la segunda entrega

Las regresiones de autorización, inventario y SRI simulado siguen pasando, junto con casos nuevos de sobrepago, precisión y contenido seguro del certificado. Doce comprobaciones adicionales inspeccionan el modelo EF real sin base de datos. Se revisó el certificado ficticio en navegador a 1280 px y 360 px, sin desbordamiento horizontal. Esto no certifica las pantallas completas de la clínica.

La actualización SQL 010 queda integrada en el arranque y disponible como archivo para ejecución por el administrador. No se pudo aplicar ni probar contra una base accesible. Las sesiones anteriores deberán autenticarse de nuevo. Los límites de intentos son por proceso; despliegues distribuidos requieren diseño adicional. No se realizaron envíos, despliegues ni transacciones reales.