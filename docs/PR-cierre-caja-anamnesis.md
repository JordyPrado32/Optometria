# Cierre de caja persistente y control de edición clínica

El reporte de caja no registraba el cierre y la historia permitía sobrescribir encuentros sin motivo ni autorización temporal. Se agrega un arqueo permanente en `/reportes/cierre-caja` y controles de servidor en el guardado clínico.

- Caja general de todos los cajeros: importes por medio de pago, total, base anterior, efectivo contado, diferencia, retiros bancarios con destino/referencia, otros retiros, observación y saldo retenido para el siguiente cierre. Historial inmutable desde la interfaz.
- Los cierres posteriores incluyen las diferencias en cobros iniciales de ventas y nuevos abonos, excluyendo aplicaciones de notas de crédito. Se guarda una fotografía de las ventas para incluir cobros de borradores antiguos sin duplicar abonos. El primer cierre incluye el histórico disponible; se debe conciliar su saldo inicial al comenzar a usarlo.
- Los filtros de fecha/cajero siguen afectando exclusivamente al reporte informativo existente. El formulario de registro lo indica explícitamente.
- Toda edición de un encuentro existente exige motivo. A partir de 24 horas desde su apertura, el médico necesita autorización administrativa individual para ese encuentro; se consume al guardar. También se conservan versiones anterior/posterior, autor, fecha y autorización. Se protege el encuentro completo para que la anamnesis estructurada tampoco pueda alterarse por otra sección.
- Autorizaciones desde la historia clínica por un administrador. Índice único para una autorización pendiente por médico/encuentro, transacción serializable y comprobación de versión al guardar.

## Base de datos

`OptometriaApp/Sql/011_cash_clinical_control.sql` crea tres tablas nuevas sin borrar tablas existentes. Está embebido y se ejecuta mediante EF Core al arrancar, siguiendo el mecanismo existente. El usuario SQL de la aplicación necesita permisos DDL. No se ha ejecutado contra una base real en esta sesión.

## Validación

- `dotnet build OptometriaApp/OptometriaApp.csproj --no-restore`
- `dotnet run --project OptometriaApp.ModelChecks/OptometriaApp.ModelChecks.csproj --no-restore`
- `dotnet run --project OptometriaApp.RegressionTests/OptometriaApp.RegressionTests.csproj --no-restore`

Pendiente de validación integrada con SQL Server y sesión autenticada: guardar dos cierres consecutivos, comparar saldo retenido/base, validar carreras entre cajeros y consumir una autorización con una historia de más de 24 horas. También comprobar los formularios en escritorio y móvil.

No se pudo crear rama ni commit: `.git` es de solo lectura en este entorno. Descripción preparada para un PR; sin publicación ni despliegue.
