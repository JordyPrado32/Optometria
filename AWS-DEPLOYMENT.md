# Despliegue de Optometria en AWS con Docker y Tailscale

Esta rama prepara una sola aplicacion ASP.NET Core que publica la interfaz Blazor y una API de lectura para la aplicacion movil.

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
- `GET /api/productos?limite=20`: devuelve hasta 20 productos o servicios activos. El limite permitido es de 1 a 200.

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

Los endpoints son anonimos para facilitar la demostracion academica. Antes de utilizar esta configuracion en produccion se debe implementar autenticacion para la aplicacion movil.

## 1. Obtener la rama en EC2

Desde la carpeta del repositorio clonado:

```bash
git fetch origin
git switch agent/aws-docker-api-integration
git pull --ff-only origin agent/aws-docker-api-integration
```

Comprueba la rama activa:

```bash
git branch --show-current
```

Debe responder:

```text
agent/aws-docker-api-integration
```

## 2. Crear el archivo privado de configuracion

```bash
cp .env.aws.example .env.aws
nano .env.aws
```

Sustituye todos los marcadores por los datos que configuraste fuera del repositorio:

- `VPN_DB_HOST` y `VPN_DB_PORT` por la direccion y el puerto privados alcanzables desde EC2.
- `DATABASE_NAME`, `SQL_USER` y `SQL_PASSWORD` por la base y el usuario SQL de solo lectura.

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

El archivo `appsettings.json` local no entra en la imagen. La cadena real se inyecta mediante `ConnectionStrings__OpticaConnection` al iniciar el contenedor.

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
curl "http://127.0.0.1:8080/api/productos?limite=5"
```

La primera prueba valida el contenedor. La segunda valida el recorrido completo hasta SQL Server local.

## 7. Probar desde otra computadora

El grupo de seguridad de EC2 debe permitir temporalmente TCP `8080` desde la IP utilizada para la prueba.

```text
http://IP_PUBLICA_EC2:8080/
http://IP_PUBLICA_EC2:8080/api/salud
http://IP_PUBLICA_EC2:8080/api/productos?limite=5
```

No abras el puerto de SQL Server ni el puerto reenviado a Internet. Deben utilizarse solamente dentro de la ruta privada de la VPN.

## 8. Configurar la aplicacion movil

La URL base que debe usar el cliente movil es:

```text
http://IP_PUBLICA_EC2:8080/
```

El cliente consultara:

```text
GET api/productos?limite=20
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
git pull --ff-only origin agent/aws-docker-api-integration
docker compose --env-file .env.aws -f docker-compose.aws.yml up -d --build
```

## Alcance del usuario SQL de lectura

El usuario SQL de solo lectura permite demostrar `/api/productos`. Las operaciones completas de la aplicacion web que insertan o modifican informacion requieren permisos adicionales y no forman parte de este endpoint de prueba. No otorgues `sysadmin` ni expongas la base de datos a Internet.
