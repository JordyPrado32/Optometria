# Puesta en marcha y aceptación de los módulos completados

## Actualización

La aplicación incorpora `OptometriaApp/Sql/010_clinic_completion.sql` como recurso y lo ejecuta mediante EF al arrancar, después de crear el esquema de compras e historia clínica. Si la cuenta de aplicación no tiene permisos DDL, el administrador debe ejecutar ese archivo en la base correcta antes del arranque. El script es transaccional e idempotente; añade tablas y no elimina tablas existentes.

Los saldos pagados anteriores se conservan como un movimiento identificado como **Saldo inicial**. No se inventan fechas ni referencias de pagos antiguos. Conciliar cualquier saldo histórico superior al total o negativo antes de operar. Esos saldos iniciales no se cuentan como pagos nuevos en el flujo de caja.

Los usuarios deberán iniciar sesión nuevamente: las cookies anteriores no contienen la versión de credencial. Una contraseña cambiada, cuenta bloqueada/desactivada o cambio de rol invalida el acceso. La revalidación de los circuitos Blazor se realiza cada 30 segundos; no significa revocación instantánea de toda acción en todos los módulos. Los módulos nuevos comprueban permisos vigentes en sus operaciones.

El limitador de autenticación permite 10 POST por minuto por ruta e identidad/IP. Es local al proceso. En despliegues con varias instancias, complementar con un control compartido y configurar correctamente el proxy de confianza.

## Uso

- **Abonos:** Compras → Liquidaciones → Abonos. Registrar importe, método y referencia. El saldo y estado se calculan; ya no se edita el pagado directamente. Revertir requiere motivo y conserva el original; un movimiento solo puede revertirse una vez. No hay conexión bancaria: se registran pagos efectuados por el negocio.
- **Liquidaciones:** Documento abre ahora un comprobante interno imprimible con el historial de pagos. No equivale a un documento fiscal autorizado. Archivar conserva el registro y se impide cuando existen movimientos de pago.
- **Certificados:** Historia clínica → seleccionar encuentro → Certificados médicos. Solo el profesional tratante autorizado puede emitir, con consulta cerrada, licencia y texto revisado. El paciente titular puede leer sus documentos por la ruta autorizada. No se concede acceso genérico por conocer el identificador.
- **Impresión:** el certificado conserva una copia de nombres, licencia, fecha y texto al emitir; una modificación posterior del paciente no reescribe ese documento. La anulación conserva motivo e historial. La impresión deja un espacio para firma; no simula firma electrónica ni determina por sí sola validez legal.
- **Caja:** los egresos de compras proceden del historial de abonos, con reversos negativos. Recepciones y liquidaciones dejan de computarse como pagos por sí mismas. La conciliación completa de ventas a crédito, notas de crédito y cobros sigue requiriendo aceptación con datos reales de prueba.

## Pruebas de aceptación en una base aislada

Utilizar datos ficticios y cuentas separadas de profesional, paciente, compras y usuario sin permisos.

| Escenario | Resultado requerido |
|---|---|
| Liquidación 100; abono 20; abono 80 | Saldos 80 y 0; estados Parcial y Pagada |
| Abonos negativos, cero, tres decimales o mayores al saldo | Rechazo sin movimiento ni cambio de saldo |
| Dos abonos simultáneos de 80 sobre saldo 100 | Solo uno se confirma; el otro se rechaza o permite reintento sin sobrepago |
| Doble envío con la misma operación | Un solo movimiento |
| Reverso del abono de 20; segundo reverso del mismo | Primer reverso conserva el original; segundo rechazado |
| Editar total por debajo de pagado | Rechazo |
| Editar orden con pagos o archivar liquidación con movimientos | Rechazo |
| Abrir abonos de otro usuario o revocar permiso y guardar | Rechazo sin exposición de movimientos ajenos |
| Certificado de borrador o de otro profesional | Emisión rechazada |
| Emitir, cambiar datos del paciente y volver a imprimir | El documento conserva los datos de emisión |
| Texto con etiquetas HTML o JavaScript | Se imprime como texto; no se ejecuta |
| Anular certificado y abrir impresión | Estado ANULADO visible con su motivo |
| Paciente A consulta recetas/certificados de B | Sin acceso, respuesta 404 |
| Login con 2FA, acceso directo durante verificación y cambio obligatorio | Operaciones protegidas denegadas hasta completar los pasos |
| Cambiar contraseña, rol o bloquear con otra sesión abierta | Cookie rechazada y circuito invalidado dentro del intervalo configurado |
| Dos reservas simultáneas; paciente con otro médico a la misma hora | No se confirman citas superpuestas |
| Bloquear un día con varias citas | Cada cita encuentra un hueco distinto; si no hay huecos suficientes se revierte todo el bloqueo |
| Agenda cercana a medianoche | Generación de horarios termina sin volver al inicio del día |
| Recordatorios, SMTP caído y recuperación | Verificar entrega, no duplicación y aviso de reprogramación |
| Móvil y teclado | Formularios, mensajes y tablas operables a 360 px, 768 px, escritorio y zoom 200 % |

## Operación clínica y recuperación pendientes de validación externa

Registrar jurisdicción y responsable clínico; revisar los textos, firma, consentimiento, representación de menores, retención, derechos del titular y facturación con los responsables correspondientes. No se han certificado obligaciones legales.

Antes de producción, realizar una copia de seguridad y restaurarla en otra base; comprobar cantidades, documentos y saldos después de restaurar. Definir tiempos de recuperación y pérdida de datos aceptables, responsables de incidentes, almacenamiento y rotación de claves, permisos mínimos SQL y acceso a copias. No se ejecutaron copias ni cambios de servicios Windows en esta sesión.

## Evidencia disponible

- Ejecutable de regresiones: autorización, inventario, SRI simulado, importes de compras y escape de certificados.
- `dotnet run --project OptometriaApp.ModelChecks`: doce comprobaciones del modelo EF real, índices, precisión, protección frente a borrado en cascada, recurso SQL y versión de credencial. No necesita conexión SQL.
- `dotnet run --project OptometriaApp.ModelChecks -- --serve`: muestra de certificado ficticio en `http://127.0.0.1:8765/`, exclusivamente para revisión visual. No levanta la clínica ni toca datos.
- Base de datos: la instancia configurada SQLEXPRESS01 estaba detenida; SQLEXPRESS02 estaba activa, pero el acceso de prueba falló. No se cambió la conexión de la aplicación a otra base.
- La suite xUnit original continúa dependiendo de paquetes no disponibles en el entorno. Las comprobaciones anteriores no se presentan como sustituto de integración SQL ni como certificación general.
