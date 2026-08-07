# Despliegue de Optometria en AWS con Docker y Tailscale

Esta rama prepara una sola aplicacion ASP.NET Core que publica la interfaz Blazor y una API autenticada para la aplicacion movil.

## Flujo de la practica

```text
Aplicacion movil o navegador
        -> IP publica de EC2:8080
        -> contenedor OptometriaApp (Blazor + API)
        -> tunel VPN privado
        -> reenvio de puertos configurado por el equipo
        -> servidor SQL local
```

La aplicacion movil nunca debe usar la IP de Tailscale, el puerto de SQL Server ni las credenciales de la base de datos.

## Endpoints agregados

- `GET /api/salud`: comprueba que la aplicacion esta iniciada.
- `POST /api/auth/login`: valida un usuario o correo existente y emite un token.
- `POST /api/auth/login/mfa`: completa el acceso si el usuario tiene MFA activo.
- `GET /api/productos?buscar=lentes&limite=20`: busca productos activos. Requiere el token recibido durante el login.
- `GET /api/profile/me`: consulta el perfil autenticado.
- `GET|POST /api/pacientes`: lista o registra pacientes.
- `GET /api/medicos`: lista medicos activos.
- `GET /api/medicos/{id}/slots/{fecha}`: calcula horarios disponibles.
- `GET|POST /api/citas`: lista o agenda citas.

Ejemplo de respuesta:

```json
[
  {
    "idProducto": 1,
    "codigoProducto": "MARCO-001",
    "nombreProducto": "Marco negro",
    "tipoItem": "Producto",
    "precioVenta": 40.00,
    "stockActual": 8
  }
]
```

Solo el endpoint de salud es anonimo. Las credenciales viajan a la API de AWS y nunca se incluyen en el codigo movil ni se envian directamente a SQL Server.

## 1. Obtener la rama en EC2

Desde la carpeta del repositorio clonado:

```bash
git fetch origin
git switch agent/mobile-auth-product-search
git pull --ff-only origin agent/mobile-auth-product-search
```

Comprueba la rama activa:

```bash
git branch --show-current
```

Debe responder:

```text
agent/mobile-auth-product-search
```

## 2. Crear el archivo privado de configuracion

```bash
nano .env.aws
```

Si ya tienes `.env.aws` funcionando, **no lo reemplaces**: conserva la cadena SQL y agrega solamente `MOBILE_API_SIGNING_KEY`. Si todavia no existe, crealo primero con `cp .env.aws.example .env.aws`.

Sustituye todos los marcadores por los datos que configuraste fuera del repositorio:

- `VPN_DB_HOST` y `VPN_DB_PORT` por la direccion y el puerto privados alcanzables desde EC2.
- `DATABASE_NAME`, `SQL_USER` y `SQL_PASSWORD` por la base y el usuario SQL de solo lectura.
- `MOBILE_API_SIGNING_KEY` por un secreto aleatorio para firmar los tokens. Generalo con:

```bash
openssl rand -base64 48
```

Conserva las comillas simples alrededor de la cadena y protege el archivo:

```bash
chmod 600 .env.aws
```

`.env.aws` esta ignorado por Git y no debe publicarse.

## 3. Verificar VPN y reenvio antes de construir

```bash
tailscale status
tailscale ping VPN_DB_HOST
nc -vz VPN_DB_HOST VPN_DB_PORT
```

El ultimo comando debe terminar con `succeeded`. Si falla, no continues con Docker: revisa Tailscale, el firewall de Windows, `portproxy` y SQL Server.

## 4. Validar y construir el contenedor

```bash
docker compose --env-file .env.aws -f docker-compose.aws.yml config
docker compose --env-file .env.aws -f docker-compose.aws.yml build
```

El archivo `appsettings.json` local no entra en la imagen. La cadena SQL y el secreto de tokens se inyectan como variables privadas al iniciar el contenedor.

## 5. Iniciar la aplicacion

```bash
docker compose --env-file .env.aws -f docker-compose.aws.yml up -d
docker compose --env-file .env.aws -f docker-compose.aws.yml ps
```

Consulta los registros:

```bash
docker compose --env-file .env.aws -f docker-compose.aws.yml logs --tail=200 optometria
```

## 6. Probar desde EC2

```bash
curl http://127.0.0.1:8080/api/salud
```

La respuesta esperada es `{"estado":"ok","servicio":"OptometriaApp"}`.

Prueba el login con un usuario real que no tenga MFA activo:

```bash
curl -X POST http://127.0.0.1:8080/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"identifier":"USUARIO","password":"CONTRASENA"}'
```

Copia el valor de `token` y consulta productos:

```bash
curl "http://127.0.0.1:8080/api/productos?buscar=lentes&limite=5" \
  -H 'Authorization: Bearer TOKEN_COPIADO'
```

Esta ultima prueba valida el recorrido completo hasta SQL Server local.

Tambien puedes comprobar los modulos recibidos con el backend movil original:

```bash
curl http://127.0.0.1:8080/api/pacientes \
  -H 'Authorization: Bearer TOKEN_COPIADO'

curl http://127.0.0.1:8080/api/citas \
  -H 'Authorization: Bearer TOKEN_COPIADO'
```

## 7. Probar desde otra computadora

El grupo de seguridad de EC2 debe permitir temporalmente TCP `8080` desde la IP utilizada para la prueba.

```text
http://IP_PUBLICA_EC2:8080/
http://IP_PUBLICA_EC2:8080/api/salud
```

No abras el puerto de SQL Server ni el puerto reenviado a Internet. Deben utilizarse solamente dentro de la ruta privada de la VPN.

## 8. Configurar la aplicacion movil

La URL base que debe usar el cliente movil es:

```text
http://IP_PUBLICA_EC2:8080/api
```

El cliente iniciara sesion y luego consultara:

```text
POST auth/login
GET productos?buscar=lentes&limite=50
```

Si Android bloquea HTTP durante la practica, agrega el permiso de Internet y habilita temporalmente trafico sin cifrar. Para una entrega definitiva se debe publicar la API mediante HTTPS.

## Comandos de diagnostico

```bash
docker ps
docker logs optometria-app --tail 200
ss -lntp | grep 8080
tailscale status
nc -vz VPN_DB_HOST VPN_DB_PORT
```

## Detener o reconstruir

Detener:

```bash
docker compose --env-file .env.aws -f docker-compose.aws.yml down
```

Reconstruir despues de actualizar la rama:

```bash
git pull --ff-only origin agent/mobile-auth-product-search
docker compose --env-file .env.aws -f docker-compose.aws.yml up -d --build
```

## Alcance del usuario SQL de lectura

Para login, productos y consultas, el usuario SQL debe poder leer `tbl_usuario`, `tbl_usuario_seguridad`, `tbl_rol`, `tbl_producto`, `tbl_paciente`, `tbl_medico`, `tbl_disponibilidad_medico`, `tbl_citas` y `tbl_estado_cita`.

Si tambien demostraras **Nuevo paciente** y **Nueva cita**, reemplaza `USUARIO_SQL_API` por el usuario configurado en `.env.aws` y ejecuta en SSMS:

```sql
USE bd_optica_modelo_estrella;
GRANT SELECT ON dbo.tbl_usuario TO [USUARIO_SQL_API];
GRANT SELECT ON dbo.tbl_usuario_seguridad TO [USUARIO_SQL_API];
GRANT SELECT ON dbo.tbl_rol TO [USUARIO_SQL_API];
GRANT SELECT ON dbo.tbl_producto TO [USUARIO_SQL_API];
GRANT SELECT, INSERT, UPDATE ON dbo.tbl_paciente TO [USUARIO_SQL_API];
GRANT SELECT ON dbo.tbl_medico TO [USUARIO_SQL_API];
GRANT SELECT ON dbo.tbl_disponibilidad_medico TO [USUARIO_SQL_API];
GRANT SELECT, INSERT ON dbo.tbl_citas TO [USUARIO_SQL_API];
GRANT SELECT ON dbo.tbl_estado_cita TO [USUARIO_SQL_API];
```

No otorgues `sysadmin`, no publiques la contrasena y no abras SQL Server a Internet.
