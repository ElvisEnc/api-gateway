# API Gateway — .NET 10 + YARP

Módulo 5 · Actividad 1. API Gateway que **emite un JWT propio** en `POST /api/login` y hace de
**reverse proxy** autenticado hacia [jsonplaceholder](https://jsonplaceholder.typicode.com).

Construido con ASP.NET Core (.NET 10) y [YARP](https://microsoft.github.io/reverse-proxy/). Sin base de datos:
los usuarios son estáticos y están en la configuración.

---

## Endpoints

| Endpoint | ¿Bearer token? | Acción |
|---|---|---|
| `POST /api/login` | No | Valida usuario y contraseña y devuelve un JWT |
| `GET /api/users` · `GET /api/users/{id}` | Sí | Reenvía a `https://jsonplaceholder.typicode.com/users[/{id}]` |
| `GET /api/posts` · `GET /api/posts/{id}` | Sí | Reenvía a `https://jsonplaceholder.typicode.com/posts[/{id}]` |
| `GET /health` | No | Health check del gateway (`Healthy`) |

> **Es un reverse proxy, no una redirección HTTP 302.** El cliente nunca habla con jsonplaceholder:
> el gateway valida el token, quita el prefijo `/api`, reenvía la petición y devuelve la respuesta.
> Con un 302 el cliente iría directo al upstream y se saltaría el control del token.
>
> El header `Authorization` **no** se reenvía a jsonplaceholder (es un tercero). Solo se enruta `GET`:
> cualquier otro método responde `405`.

---

## Requisitos

- **Docker Desktop** (Engine 24+ con Compose v2). Es lo único necesario.
- Opcional: **.NET 10 SDK**, para ejecutarlo sin Docker.

---

## Ejecutar

### Con Docker (recomendado)

```bash
git clone https://github.com/<usuario>/api-gateway.git
cd api-gateway
docker compose up --build -d
```

El gateway queda en **http://localhost:8090**.

```bash
docker compose ps                     # STATUS: Up
docker compose logs -f api-gateway    # logs (Ctrl+C para salir)
docker compose down                   # detener y borrar el contenedor
```

### Sin Docker

```bash
dotnet run --project src/ApiGateway
```

Usa el perfil `http` (entorno `Development`, puerto **8090**) y la clave de desarrollo de
`appsettings.Development.json`.

> Docker y `dotnet run` usan el mismo puerto 8090: **no los levantes a la vez**.

---

## Credenciales estáticas

| Usuario | Contraseña | Roles |
|---|---|---|
| `admin` | `admin123` | `admin`, `integracion` |
| `nutricionista` | `nutri123` | `nutricionista` |
| `cocina` | `cocina123` | `cocina` |
| `repartidor` | `repartidor123` | `repartidor` |

Son credenciales de demostración (la consigna permite usuarios estáticos). No usar en producción.

### El token

`POST /api/login` responde:

```json
{ "access_token": "eyJ...", "token_type": "Bearer", "expires_in": 3600 }
```

JWT firmado con **HS256**. Payload de ejemplo (admin):

```json
{
  "iss": "nur-tricenter-gateway",
  "aud": "nur-tricenter-api",
  "sub": "admin",
  "name": "admin",
  "role": ["admin", "integracion"],
  "jti": "…", "iat": 0, "nbf": 0, "exp": 0
}
```

Dura **60 minutos** por defecto (`Jwt:ExpiresMinutes`).

---

## Probar con curl

Levanta el gateway (Docker o `dotnet run`) y abre **otra terminal**. Antes de nada, comprueba que responde:
`curl.exe -i http://localhost:8090/health` (PowerShell) o `curl -i http://localhost:8090/health` (Bash) → `200 Healthy`.

Todos los ejemplos usan el mismo formato:

| Opción | Para qué |
|---|---|
| `-sS` | Oculta la barra de progreso, pero **sí muestra los errores** (con `-s` solo, un gateway apagado no imprime nada) |
| `-i` | Muestra la línea de status y los headers (`Retry-After`, `WWW-Authenticate`, …) antes del cuerpo |
| `-w $W` | Al final imprime `HTTP <código> \| <tiempo>s`, útil cuando el cuerpo es largo y el status quedó arriba |

`curl` interpreta el `\n` dentro de `-w`, así que `$W` se escribe igual en PowerShell y en Bash.
Si solo quieres el cuerpo y el código, quita `-i`.

Salida de ejemplo (`GET /api/users/1`):

```
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8
Date: ...

{
  "id": 1,
  "name": "Leanne Graham",
  ...
}
---
HTTP 200 | 0.412s
```

> **¿No sale nada o `Failed to connect to localhost port 8090`?** El gateway no está corriendo.
> Levántalo y **vuelve a pedir el token**: si el login falló, `$TOKEN` quedó vacío.

### PowerShell

En PowerShell usa **`curl.exe`**: `curl` a secas es un alias de `Invoke-WebRequest`.
Dentro de comillas simples, las comillas del JSON se escriben `\"`.

```powershell
$B = "http://localhost:8090"
$W = '\n---\nHTTP %{http_code} | %{time_total}s\n'

# --- Health (sin token) → 200 Healthy
curl.exe -sS -i -w $W "$B/health"

# --- Login correcto → 200 {access_token, token_type, expires_in}
curl.exe -sS -i -w $W -X POST "$B/api/login" -H "Content-Type: application/json" -d '{\"username\":\"admin\",\"password\":\"admin123\"}'

# --- Guardar el token en una variable (debe mostrar eyJ...)
$TOKEN = (curl.exe -sS -X POST "$B/api/login" -H "Content-Type: application/json" -d '{\"username\":\"admin\",\"password\":\"admin123\"}' | ConvertFrom-Json).access_token
$TOKEN

# --- Ver el payload del token (base64url → JSON)
$p = $TOKEN.Split('.')[1].Replace('-','+').Replace('_','/'); $p += '=' * ((4 - $p.Length % 4) % 4)
[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($p))

# --- Con token → 200
curl.exe -sS -i -w $W "$B/api/users"   -H "Authorization: Bearer $TOKEN"
curl.exe -sS -i -w $W "$B/api/posts"   -H "Authorization: Bearer $TOKEN"
curl.exe -sS -i -w $W "$B/api/users/1" -H "Authorization: Bearer $TOKEN"
curl.exe -sS -i -w $W "$B/api/posts/1" -H "Authorization: Bearer $TOKEN"

# --- JSON formateado con PowerShell (sin headers)
curl.exe -sS "$B/api/users/1" -H "Authorization: Bearer $TOKEN" | ConvertFrom-Json | ConvertTo-Json -Depth 5

# --- Contar elementos → 10 y 100
(curl.exe -sS "$B/api/users" -H "Authorization: Bearer $TOKEN" | ConvertFrom-Json).Count
(curl.exe -sS "$B/api/posts" -H "Authorization: Bearer $TOKEN" | ConvertFrom-Json).Count

# --- Sin token → 401 NO_AUTENTICADO
curl.exe -sS -i -w $W "$B/api/users"

# --- Token inválido → 401 NO_AUTENTICADO
curl.exe -sS -i -w $W "$B/api/users" -H "Authorization: Bearer basura"

# --- Contraseña incorrecta → 401 CREDENCIALES_INVALIDAS
curl.exe -sS -i -w $W -X POST "$B/api/login" -H "Content-Type: application/json" -d '{\"username\":\"admin\",\"password\":\"mal\"}'

# --- Faltan campos → 400 SOLICITUD_INVALIDA
curl.exe -sS -i -w $W -X POST "$B/api/login" -H "Content-Type: application/json" -d '{}'

# --- Método no enrutado → 405
curl.exe -sS -i -w $W -X POST "$B/api/users" -H "Authorization: Bearer $TOKEN"

# --- Rate limit del login: más de 10 intentos en un minuto → 429 DEMASIADOS_INTENTOS
1..11 | ForEach-Object { "intento $_ -> " + (curl.exe -sS -o NUL -w "%{http_code}" -X POST "$B/api/login" -H "Content-Type: application/json" -d '{\"username\":\"admin\",\"password\":\"mal\"}') }
curl.exe -sS -i -w $W -X POST "$B/api/login" -H "Content-Type: application/json" -d '{}'   # cuerpo y Retry-After: 60
```

Alternativa nativa con `Invoke-RestMethod`:

```powershell
$B = "http://localhost:8090"
$login = Invoke-RestMethod -Method Post "$B/api/login" -ContentType "application/json" `
           -Body (@{ username = "admin"; password = "admin123" } | ConvertTo-Json)
$h = @{ Authorization = "Bearer $($login.access_token)" }

# Los paréntesis hacen falta en PowerShell 5.1 para que el arreglo JSON se recorra elemento por elemento
(Invoke-RestMethod "$B/api/users" -Headers $h) | Select-Object -First 3 id, name, email
(Invoke-RestMethod "$B/api/posts" -Headers $h) | Select-Object -First 3 id, title

# 401 sin token (Invoke-RestMethod lanza una excepción en los 4xx): muestra el status y el cuerpo
try { Invoke-RestMethod "$B/api/users" } catch { $r = $_.Exception.Response; [int]$r.StatusCode; (New-Object IO.StreamReader($r.GetResponseStream())).ReadToEnd() }
```

### Bash (Linux, macOS o Git Bash)

```bash
B=http://localhost:8090
W='\n---\nHTTP %{http_code} | %{time_total}s\n'

# --- Health (sin token) → 200 Healthy
curl -sS -i -w "$W" $B/health

# --- Login correcto → 200 {access_token, token_type, expires_in}
curl -sS -i -w "$W" -X POST $B/api/login -H "Content-Type: application/json" \
     -d '{"username":"admin","password":"admin123"}'

# --- Guardar el token en una variable (debe mostrar eyJ...)
TOKEN=$(curl -sS -X POST $B/api/login -H "Content-Type: application/json" \
        -d '{"username":"admin","password":"admin123"}' | sed -E 's/.*"access_token":"([^"]+)".*/\1/')
echo $TOKEN

# --- Con token → 200
curl -sS -i -w "$W" $B/api/users   -H "Authorization: Bearer $TOKEN"
curl -sS -i -w "$W" $B/api/posts   -H "Authorization: Bearer $TOKEN"
curl -sS -i -w "$W" $B/api/users/1 -H "Authorization: Bearer $TOKEN"
curl -sS -i -w "$W" $B/api/posts/1 -H "Authorization: Bearer $TOKEN"

# --- Solo el comienzo del cuerpo (las listas son largas)
curl -sS $B/api/posts -H "Authorization: Bearer $TOKEN" | head -c 300; echo

# --- Contar elementos → 10 y 100
curl -sS $B/api/users -H "Authorization: Bearer $TOKEN" | grep -o '"id":' | wc -l
curl -sS $B/api/posts -H "Authorization: Bearer $TOKEN" | grep -o '"id":' | wc -l

# --- Sin token → 401 NO_AUTENTICADO
curl -sS -i -w "$W" $B/api/users

# --- Token inválido → 401 NO_AUTENTICADO
curl -sS -i -w "$W" $B/api/users -H "Authorization: Bearer basura"

# --- Contraseña incorrecta → 401 CREDENCIALES_INVALIDAS
curl -sS -i -w "$W" -X POST $B/api/login -H "Content-Type: application/json" \
     -d '{"username":"admin","password":"mal"}'

# --- Faltan campos → 400 SOLICITUD_INVALIDA
curl -sS -i -w "$W" -X POST $B/api/login -H "Content-Type: application/json" -d '{}'

# --- Método no enrutado → 405
curl -sS -i -w "$W" -X POST $B/api/users -H "Authorization: Bearer $TOKEN"

# --- Rate limit del login: más de 10 intentos en un minuto → 429 DEMASIADOS_INTENTOS
for i in $(seq 1 11); do
  echo "intento $i -> $(curl -sS -o /dev/null -w '%{http_code}' -X POST $B/api/login \
        -H 'Content-Type: application/json' -d '{"username":"admin","password":"mal"}')"
done
curl -sS -i -w "$W" -X POST $B/api/login -H "Content-Type: application/json" -d '{}'   # cuerpo y Retry-After: 60
```

> **Ojo con el rate limit:** cuentan **todos** los `POST /api/login` (correctos, incorrectos y `{}`).
> Si corres todo el bloque seguido, el 429 aparece antes del intento 11. Espera un minuto o reutiliza
> `$TOKEN`, que dura 60 minutos.

### Con un archivo `.http`

[`demo/actividad1.http`](demo/actividad1.http) tiene los 5 casos básicos (login, users y posts con token,
401 sin token y 401 con credenciales inválidas). Ábrelo en VS Code con la extensión **REST Client**, en Rider
o en Visual Studio y ejecuta primero la petición `login`: las demás toman el token de su respuesta.

### Probar el token expirado (opcional)

Levanta el gateway con tokens de 1 minuto, pide uno y espera ~1,5 minutos (hay 30 s de tolerancia de reloj):

```powershell
# PowerShell + Docker
docker compose down
docker compose run --rm -d -p 8090:8080 -e Jwt__ExpiresMinutes=1 --name gw-expira api-gateway
```

```bash
# Bash + Docker
docker compose down
docker compose run --rm -d -p 8090:8080 -e Jwt__ExpiresMinutes=1 --name gw-expira api-gateway
```

Luego, pasados ~90 s, `GET /api/users` con ese token responde **401 `TOKEN_EXPIRADO`**.
Para terminar: `docker stop gw-expira`.

---

## Contrato de errores

Todos los errores tienen el mismo cuerpo (igual que los microservicios del sistema):

```json
{ "codigo": "NO_AUTENTICADO", "mensaje": "Falta el token Bearer o no es válido." }
```

| HTTP | `codigo` | Cuándo |
|---|---|---|
| 400 | `SOLICITUD_INVALIDA` | `POST /api/login` sin `username` o `password` |
| 401 | `CREDENCIALES_INVALIDAS` | Usuario o contraseña incorrectos |
| 401 | `NO_AUTENTICADO` | Ruta protegida sin token, o con un token inválido (firma, issuer, audience) |
| 401 | `TOKEN_EXPIRADO` | El token venció (además: `WWW-Authenticate: Bearer error="invalid_token"`) |
| 403 | `SIN_PERMISO` | Token válido pero sin el rol requerido (se usa con las rutas de integración, fases siguientes) |
| 429 | `DEMASIADOS_INTENTOS` | Más de `Login:IntentosPorMinuto` logins por minuto desde la misma IP (header `Retry-After: 60`) |

---

## Configuración

Todo se configura con variables de entorno (en .NET, `__` equivale a `:`):

| Variable | Por defecto | Descripción |
|---|---|---|
| `JWT_KEY` | clave de demo de `docker-compose.yml` | Clave HMAC de firma. **Mínimo 32 bytes.** Compose la pasa como `Jwt__Key` |
| `Jwt__Key` | vacía en `appsettings.json` | La misma clave, para `dotnet run` o `docker run` |
| `Jwt__Issuer` | `nur-tricenter-gateway` | Claim `iss` |
| `Jwt__Audience` | `nur-tricenter-api` | Claim `aud` |
| `Jwt__ExpiresMinutes` | `60` | Vida del token (1 a 1440) |
| `Login__IntentosPorMinuto` | `10` | Límite de logins por minuto y por IP |

Si `Jwt:Key` falta o tiene menos de 32 bytes, **el gateway no arranca** (`OptionsValidationException`).

Cambiar la clave en Docker:

```powershell
$env:JWT_KEY = "mi-clave-secreta-de-al-menos-32-caracteres!!"; docker compose up -d
```

```bash
JWT_KEY="mi-clave-secreta-de-al-menos-32-caracteres!!" docker compose up -d
```

o con un archivo `.env` junto a `docker-compose.yml` (está en `.gitignore`):

```dotenv
JWT_KEY=mi-clave-secreta-de-al-menos-32-caracteres!!
```

### Agregar usuarios sin tocar el código

Los usuarios son un arreglo (`Auth:Usuarios`), y los índices 0 a 3 ya están ocupados. Para agregar uno,
usa el índice 4 en adelante, por ejemplo en `environment` de `docker-compose.yml`:

```yaml
      Auth__Usuarios__4__Username: soporte
      Auth__Usuarios__4__Password: soporte123
      Auth__Usuarios__4__Roles__0: soporte
```

### Rate limit detrás de Docker

El límite se aplica **por IP de origen**. Detrás de Docker, el contenedor ve las peticiones desde la IP
del bridge (p. ej. `172.x.0.1`), así que **todos los clientes del host comparten el mismo contador**
de 10 intentos por minuto. Para una demo es suficiente; en producción, detrás de un proxy, habría que usar
`X-Forwarded-For` (`UseForwardedHeaders`).

---

## Tests

```bash
dotnet test
```

Tests de integración con xUnit + `WebApplicationFactory<Program>`. jsonplaceholder se reemplaza por un
upstream falso de **WireMock.Net**, así que **no hace falta Docker ni internet**.

| Archivo | Qué cubre |
|---|---|
| `LoginTests.cs` | 200 + token, 401 `CREDENCIALES_INVALIDAS` (contraseña mala o usuario inexistente), 400 `SOLICITUD_INVALIDA`, username sin distinguir mayúsculas, 429 con `Retry-After` |
| `ProxyJsonPlaceholderTests.cs` | 401 sin token, con firma ajena, con `iss`/`aud` distintos y `TOKEN_EXPIRADO`; reenvío a `/users` y `/posts` sin el prefijo `/api` y sin `Authorization`; `POST` no enrutado; `/health` anónimo; toda ruta del proxy declara `AuthorizationPolicy` |
| `IdentidadTransformTests.cs` | Un `X-User-Id` falso enviado por el cliente no llega al upstream |

```bash
dotnet test --logger "console;verbosity=normal"   # lista cada test
```

---

## Decisiones de diseño (ADR-001, resumen)

- **YARP sobre ASP.NET Core .NET 10.** Misma tecnología que los 6 microservicios del sistema, y las rutas
  se declaran en `appsettings.json` (`ReverseProxy`), no en código.
- **El JWT lo emite el propio gateway**, detrás de la interfaz `ITokenIssuer`. Hoy: `LocalJwtTokenIssuer`
  (HS256, usuarios estáticos). Más adelante se puede cambiar por `KeycloakTokenIssuer` sin cambiar el
  contrato de `POST /api/login`.
- **Seguridad:**
  - firma con `JsonWebTokenHandler`, solo HS256 (`ValidAlgorithms`), `ClockSkew` de 30 s;
  - contraseñas comparadas en tiempo constante (`FixedTimeEquals` sobre SHA-256), aunque el usuario no exista;
  - rate limiting en el login;
  - **denegar por defecto**: una `FallbackPolicy` exige usuario autenticado en cualquier ruta que no diga otra cosa;
  - el gateway elimina los headers `X-User-*` entrantes para que nadie suplante a otro usuario.
- **Validación al arrancar** (`ValidateOnStart`): una configuración inválida falla en el arranque y no en
  la primera petición.
- **Docker:** multi-stage `sdk:10.0` → `aspnet:10.0`, restore en capa propia, usuario no-root (`$APP_UID`).

Alternativas descartadas: Ocelot y Express (mantenimiento), Keycloak ahora (sobredimensionado para esta
entrega), gateways de producto (igual necesitan un servicio de login) y servicios en la nube (no cumplen
Dockerfile + compose).

---

## Estructura

```
api-gateway/
├── src/ApiGateway/
│   ├── Auth/            # opciones, usuarios, emisión del JWT y endpoint /api/login
│   ├── Errores/         # formato {codigo, mensaje}
│   ├── Proxy/           # transform de identidad (X-User-*)
│   ├── Program.cs       # DI, JwtBearer, policies, rate limiter, YARP
│   └── appsettings*.json
├── tests/ApiGateway.Tests/   # xUnit + WebApplicationFactory + WireMock.Net
├── demo/actividad1.http
├── Dockerfile
├── docker-compose.yml
└── ApiGateway.slnx
```

---

## Integración con NUR-TRICENTER

**Próximamente.** En las siguientes fases el gateway se pondrá delante de los 6 microservicios
(`/{servicio}/api/...`), propagando la identidad con `X-User-Id`, `X-User-Name` y `X-User-Roles`.

## Despliegue

No desplegado. Ejecutar localmente con Docker (ver [Ejecutar](#ejecutar)).
