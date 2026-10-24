# Cómo funciona el API Gateway (y cómo extenderlo)

Guía interna del repositorio `api-gateway` (Módulo 5 · Actividad 1). Explica **qué hace cada pieza**, **cómo se
emite y se valida el token**, **cómo funcionan y se ejecutan los tests**, **dónde un cambio pequeño rompe mucho**
y **cómo conectar el gateway a los microservicios** del sistema NUR-TRICENTER.

> Las referencias `archivo:línea` apuntan al código tal como está al escribir este documento. Si cambias el
> código, los números pueden moverse un poco; busca el símbolo mencionado.

## Índice

0. [Conceptos en 2 minutos](#0-conceptos-en-2-minutos)
1. [Mapa de archivos](#1-mapa-de-archivos)
2. [Cómo se arma la configuración](#2-cómo-se-arma-la-configuración)
3. [El pipeline de una petición](#3-el-pipeline-de-una-petición)
4. [Emisión del token (`POST /api/login`)](#4-emisión-del-token-post-apilogin)
5. [Validación del token](#5-validación-del-token)
6. [Las piezas que comparten emisor y validador](#6-las-piezas-que-comparten-emisor-y-validador)
7. [Autorización: políticas y roles](#7-autorización-políticas-y-roles)
8. [El proxy (YARP): rutas, clusters y transforms](#8-el-proxy-yarp-rutas-clusters-y-transforms)
9. [Propagación de identidad (`X-User-*`)](#9-propagación-de-identidad-x-user-)
10. [Rate limit, health y formato de errores](#10-rate-limit-health-y-formato-de-errores)
11. [Tests: cómo funcionan, cómo se ejecutan y cómo saber que corrieron](#11-tests-cómo-funcionan-cómo-se-ejecutan-y-cómo-saber-que-corrieron)
12. [Ejecutar y probar a mano](#12-ejecutar-y-probar-a-mano)
13. [Puntos sensibles: dónde un cambio pequeño rompe mucho](#13-puntos-sensibles-dónde-un-cambio-pequeño-rompe-mucho)
14. [Usarlo con los microservicios](#14-usarlo-con-los-microservicios)
15. [Recetas: cómo agregar cosas](#15-recetas-cómo-agregar-cosas)
16. [Seguridad: qué está cubierto y qué no](#16-seguridad-qué-está-cubierto-y-qué-no)
17. [Diagnóstico rápido y preguntas frecuentes](#17-diagnóstico-rápido-y-preguntas-frecuentes)
18. [Chuleta de comandos](#18-chuleta-de-comandos)

---

## 0. Conceptos en 2 minutos

| Concepto | En este proyecto |
|---|---|
| **API Gateway** | Única puerta de entrada. El cliente solo habla con el gateway (`http://localhost:8090`); el gateway decide a qué servicio reenviar. |
| **Reverse proxy** (no redirección) | El gateway **hace él mismo** la llamada al servicio de atrás y devuelve la respuesta. Con un `302` el cliente iría directo al servicio y se saltaría el control del token. |
| **Upstream / destino / cluster** | El servicio de atrás (hoy `jsonplaceholder.typicode.com`). En YARP, un *cluster* es un grupo de destinos. |
| **JWT** | Texto `header.payload.firma` en Base64URL. El payload lleva *claims* (datos) y la firma garantiza que nadie los cambió. **No está cifrado**: cualquiera puede leer el payload, pero nadie puede modificarlo sin la clave. |
| **HS256** | Firma HMAC-SHA256 con **clave simétrica**: la misma `Jwt:Key` sirve para firmar (login) y para verificar (cada petición). Por eso esa clave nunca sale del gateway. |
| **Claims usados** | `sub` (usuario), `name`, `role` (uno por rol), `jti` (id único del token), `iss` (quién lo emitió), `aud` (para quién es), `iat`/`nbf`/`exp` (emitido, válido desde, vence). |
| **Autenticación** | "¿Quién eres?" → validar el token. Falla = **401**. |
| **Autorización** | "¿Puedes hacer esto?" → políticas y roles. Falla = **403**. |

Flujo completo de uso:

```text
1) POST /api/login {username,password}  ──►  gateway valida usuario  ──►  devuelve JWT firmado
2) GET /api/users  + Authorization: Bearer <JWT>
        ──► gateway verifica firma, iss, aud, exp ──► quita "/api" y el header Authorization
        ──► GET https://jsonplaceholder.typicode.com/users ──► devuelve la respuesta tal cual
```

---

## 1. Mapa de archivos

| Archivo | Responsabilidad |
|---|---|
| `src/ApiGateway/Program.cs` | **El cableado de todo**: opciones, rate limit, autenticación JWT, políticas, YARP y orden de middlewares. |
| `src/ApiGateway/appsettings.json` | Configuración base: `Jwt` (sin clave), usuarios, límite de login, **rutas y clusters de YARP**. |
| `src/ApiGateway/appsettings.Development.json` | Clave JWT de desarrollo y logs más verbosos (solo al correr con `dotnet run`). |
| `src/ApiGateway/Properties/launchSettings.json` | Perfiles de `dotnet run`: puerto **8090**, entorno `Development`. |
| `Auth/JwtOptions.cs` | Clase que se llena desde la sección `Jwt` (Issuer, Audience, Key, ExpiresMinutes) con validaciones. |
| `Auth/UsuariosOptions.cs` | Clase que se llena desde `Auth:Usuarios` (usuario, contraseña, roles). |
| `Auth/IUsuarioStore.cs` + `ConfigUsuarioStore.cs` | "¿Este usuario y contraseña son válidos?". Hoy lee de la configuración. |
| `Auth/ITokenIssuer.cs` + `LocalJwtTokenIssuer.cs` | "Dame un token para este usuario". Construye y firma el JWT. |
| `Auth/ClaveDeFirma.cs` | Convierte el texto `Jwt:Key` en la clave criptográfica. **La usan el emisor y el validador.** |
| `Auth/LoginEndpoints.cs` | Endpoint `POST /api/login` y los DTO `LoginRequest` / `TokenResponse`. |
| `Auth/Politicas.cs` | Nombres de las políticas (`Autenticado`, `Integracion`) y del rol `integracion`. |
| `Errores/RespuestasError.cs` | Formato único de error `{ "codigo", "mensaje" }`. |
| `Proxy/IdentidadTransformProvider.cs` | Transform de YARP que borra/propaga los headers `X-User-*`. |
| `Dockerfile` · `docker-compose.yml` | Imagen multi-stage y contenedor publicado en `8090:8080`. |
| `tests/ApiGateway.Tests/GatewayFactory.cs` | Levanta el gateway en memoria con jsonplaceholder reemplazado por WireMock. |
| `tests/ApiGateway.Tests/TokenDePrueba.cs` | Fabrica tokens a medida (válidos, vencidos, con otra clave…). |
| `tests/ApiGateway.Tests/LoginTests.cs` | Tests del login y del rate limit. |
| `tests/ApiGateway.Tests/ProxyJsonPlaceholderTests.cs` | Tests de validación del token y del proxy. |
| `tests/ApiGateway.Tests/IdentidadTransformTests.cs` | Test anti-suplantación de `X-User-Id`. |

---

## 2. Cómo se arma la configuración

ASP.NET Core junta varias fuentes; **la última gana**:

```text
appsettings.json
  └─► appsettings.{Entorno}.json        (Development al hacer dotnet run; Production en Docker)
        └─► variables de entorno        (Jwt__Key, ReverseProxy__Clusters__...)  ← "__" equivale a ":"
              └─► argumentos de línea de comando
                    └─► (solo tests) builder.UseSetting(...) en GatewayFactory
```

Ejemplos:

- **La clave JWT** está vacía en `appsettings.json:12`. En desarrollo la pone `appsettings.Development.json`; en
  Docker la pone la variable `Jwt__Key` de `docker-compose.yml`; en los tests la pone `GatewayFactory` con `UseSetting`.
- **La dirección de jsonplaceholder** (`appsettings.json:79`) se puede cambiar sin tocar el JSON:
  `ReverseProxy__Clusters__jsonplaceholder__Destinations__d1__Address=http://otro-host`. Así es como los tests la
  apuntan a WireMock.

**Validación al arranque** (`Program.cs:15-25`): `ValidateDataAnnotations` + `Validate` + `ValidateOnStart` hacen
que el gateway **no arranque** si la `Jwt:Key` falta o tiene menos de 32 bytes, si falta `Issuer`/`Audience`, o si
no hay ningún usuario. Es preferible fallar al iniciar que emitir tokens inseguros.

---

## 3. El pipeline de una petición

Orden de los middlewares (`Program.cs:102-108`):

```text
Petición HTTP
   │
   ▼
[Routing]           decide qué endpoint corresponde: /health, /api/login o una ruta de YARP
   │
   ▼
UseAuthentication   (102)  si hay "Authorization: Bearer ...", valida el token y arma HttpContext.User
   │
   ▼
UseAuthorization    (103)  aplica la política del endpoint; si no tiene, la FallbackPolicy (exige autenticado)
   │                        sin usuario ──► OnChallenge ──► 401 NO_AUTENTICADO / TOKEN_EXPIRADO
   │                        sin rol    ──► OnForbidden ──► 403 SIN_PERMISO
   ▼
UseRateLimiter      (104)  solo afecta a endpoints con RequireRateLimiting (el login) ──► 429
   │
   ▼
Endpoint
   ├── /health            ──► "Healthy"
   ├── POST /api/login    ──► LoginEndpoints
   └── MapReverseProxy    ──► transforms (quitar /api, quitar Authorization, X-User-*) ──► upstream
```

### Recorrido: `GET /api/users` **sin** token

1. El routing encuentra la ruta YARP `jsonplaceholder-users` (política `Autenticado`).
2. `UseAuthentication` no encuentra header → usuario anónimo.
3. `UseAuthorization` exige usuario autenticado → *challenge* → `OnChallenge` (`Program.cs:74`) escribe
   **401 `NO_AUTENTICADO`**.
4. **YARP nunca se ejecuta**: jsonplaceholder no recibe nada (esto lo comprueba un test).

### Recorrido: `GET /api/users` **con** token válido

1. `UseAuthentication` valida firma, `iss`, `aud`, `exp` → `HttpContext.User` con claims `sub`, `name`, `role`.
2. `UseAuthorization` → política `Autenticado` cumplida.
3. YARP aplica los transforms de la ruta: `/api/users` → `/users`, quita `Authorization`, y el transform de
   identidad borra cualquier `X-User-*`.
4. YARP llama a `https://jsonplaceholder.typicode.com/users` y copia la respuesta (status, headers, body) al cliente.

---

## 4. Emisión del token (`POST /api/login`)

```text
LoginEndpoints.MapLogin  ──►  ITokenIssuer.EmitirAsync  ──►  IUsuarioStore.Validar
       (HTTP)                   (LocalJwtTokenIssuer)          (ConfigUsuarioStore)
                                        │
                                        └──► JsonWebTokenHandler.CreateToken (firma con ClaveDeFirma)
```

**Paso a paso:**

1. **Endpoint** (`Auth/LoginEndpoints.cs:20`): recibe `{ "username", "password" }`.
   - Si falta alguno o está vacío/en blanco (`:22`) → **400 `SOLICITUD_INVALIDA`**.
   - Tiene `.AllowAnonymous()` (`:34`, si no, la FallbackPolicy pediría token para hacer login) y
     `.RequireRateLimiting("login")` (`:35`).
2. **Emisor** (`Auth/LocalJwtTokenIssuer.cs`): primero pide al store que valide las credenciales; si devuelve
   `null` → el endpoint responde **401 `CREDENCIALES_INVALIDAS`** (`LoginEndpoints.cs:30`). Mismo mensaje para
   "usuario no existe" y "contraseña mala": así no se puede averiguar qué usuarios existen.
3. **Store** (`Auth/ConfigUsuarioStore.cs:10-20`):
   - Busca el usuario **sin distinguir mayúsculas** (`:12`, `ADMIN` = `admin`).
   - Compara las contraseñas con hash SHA-256 + `CryptographicOperations.FixedTimeEquals` (`:13-15`): la
     comparación tarda lo mismo acierte o falle (evita ataques por tiempo). Si el usuario no existe, compara contra
     un `Guid` aleatorio para que también tarde lo mismo.
   - Lee de `IOptionsMonitor`, así que un cambio en `appsettings.json` se aplica sin reiniciar.
4. **Construcción del token** (`LocalJwtTokenIssuer.cs:21-38`):
   - Claims: `sub` y `name` = username, `jti` = Guid nuevo, y **un claim `role` por cada rol** (`:28`).
   - `Issuer`, `Audience` y `ExpiresMinutes` salen de la sección `Jwt`.
   - `iat` = `nbf` = ahora; `exp` = ahora + `ExpiresMinutes` (60 por defecto).
   - Se firma con `ClaveDeFirma.Crear(jwt.Key)` y `HmacSha256` (`:38`).
   - La hora sale de `TimeProvider` (inyectado), lo que permite en el futuro "congelar el reloj" en tests.
5. **Respuesta** (`:41`):

```json
{ "access_token": "eyJhbGciOi...", "token_type": "Bearer", "expires_in": 3600 }
```

**Contenido del token decodificado** (payload) para `admin`:

```json
{
  "sub": "admin",
  "name": "admin",
  "jti": "5b0e1c4e-…",
  "role": ["admin", "integracion"],
  "iss": "nur-tricenter-gateway",
  "aud": "nur-tricenter-api",
  "iat": 1761300000, "nbf": 1761300000, "exp": 1761303600
}
```

> **Por qué hay interfaces (`ITokenIssuer`, `IUsuarioStore`)**: para poder cambiar el "de dónde salen los usuarios"
> (base de datos, Keycloak…) sin tocar el endpoint ni la validación. Ver receta [15.10](#1510-cambiar-el-emisor-por-keycloak-u-otro-idp).

---

## 5. Validación del token

Se configura en `Program.cs:51-88`. Cada petición con `Authorization: Bearer <token>` pasa por estas comprobaciones:

| Parámetro (`Program.cs`) | Qué comprueba | Si falla |
|---|---|---|
| `ValidateIssuerSigningKey` + `IssuerSigningKey = ClaveDeFirma.Crear(jwt.Key)` (`:63-64`) | Que la firma se hizo con **nuestra** clave | 401 `NO_AUTENTICADO` |
| `ValidAlgorithms = [HmacSha256]` (`:65`) | Que no venga con otro algoritmo (p. ej. `none`) | 401 |
| `ValidateIssuer` / `ValidIssuer` (`:59-60`) | `iss` = `nur-tricenter-gateway` | 401 |
| `ValidateAudience` / `ValidAudience` (`:61-62`) | `aud` = `nur-tricenter-api` | 401 |
| `ValidateLifetime` + `ClockSkew = 30 s` (`:66-67`) | `nbf` ≤ ahora ≤ `exp` + 30 s de tolerancia | 401 **`TOKEN_EXPIRADO`** |
| `NameClaimType = "name"`, `RoleClaimType = "role"` (`:68-69`) | De qué claim salen `User.Identity.Name` y `User.IsInRole(...)` | — (si no coincide con el emisor: 403 siempre) |
| `MapInboundClaims = false` (`:56`) | Que los claims conserven su nombre corto (`sub`, `role`) | — (si se quita: `role` pasa a ser una URI larga y los roles dejan de funcionar) |

**Respuestas de error personalizadas** (`Program.cs:72-88`):

- `OnChallenge` (`:74`): se dispara cuando hace falta autenticación y no la hay o es inválida.
  `ctx.HandleResponse()` evita la respuesta por defecto (401 sin cuerpo). Si la causa fue
  `SecurityTokenExpiredException` → `TOKEN_EXPIRADO`; si no → `NO_AUTENTICADO`. También pone el header estándar
  `WWW-Authenticate`.
- `OnForbidden` (`:85`): usuario válido pero sin el rol que pide la política → **403 `SIN_PERMISO`**.

> **Por qué `iat`/`nbf` importan en el test de token expirado:** si un token tuviera `nbf` > `exp`, la librería
> lo rechazaría por otro motivo y no se obtendría `TOKEN_EXPIRADO`. Por eso `TokenDePrueba` pone `nbf` 20 minutos
> antes de `exp`.

---

## 6. Las piezas que comparten emisor y validador

**Es el punto más importante del documento.** El login (emisor) y la validación (cada petición) son dos códigos
distintos que **deben estar de acuerdo** en tres cosas. Si cambias una en un lado y no en el otro, **todos** los
tokens dejan de servir.

```text
                     ┌───────────── sección "Jwt" de la configuración ─────────────┐
                     │   Key  ·  Issuer  ·  Audience   (ExpiresMinutes solo emisor) │
                     └───────────────┬───────────────────────────────┬─────────────┘
                                     │                               │
          LocalJwtTokenIssuer (firma)│                               │Program.cs (valida)
                                     ▼                               ▼
                       ClaveDeFirma.Crear(Key)  ◄── misma función ──► ClaveDeFirma.Crear(Key)
                       claims "role" (:28)       ◄── mismo nombre ──► RoleClaimType = "role" (:69)
                       "sub", "name"             ◄── mismo nombre ──► NameClaimType = "name", MapInboundClaims=false
```

| Pieza compartida | Dónde se escribe | Dónde se lee | Consecuencia de un cambio |
|---|---|---|---|
| `Jwt:Key` | `appsettings.Development.json` / `Jwt__Key` | Emisor y validador | Todos los tokens emitidos antes quedan inválidos (401 a todo el mundo). |
| `Jwt:Issuer`, `Jwt:Audience` | `appsettings.json:13-14` | Emisor y validador | Igual: tokens viejos → 401. Y si algún microservicio comprobara `aud`, también se rompe. |
| `ClaveDeFirma.Crear` | `Auth/ClaveDeFirma.cs` | Emisor y validador | Cambiar UTF-8 por otra codificación o Base64 = otra clave → 401 siempre. |
| Nombre del claim `role` | `LocalJwtTokenIssuer.cs:28` | `RoleClaimType` (`Program.cs:69`) + `IdentidadTransformProvider` | Roles no reconocidos → 403 en rutas con rol; `X-User-Roles` vacío. |
| `sub` / `name` | `LocalJwtTokenIssuer.cs:23-24` | `NameClaimType`, `IdentidadTransformProvider` | `X-User-Id`/`X-User-Name` vacíos. |

**Regla práctica:** toca estas piezas solo a propósito y sabiendo que "cierras la sesión de todos". Todo lo demás
(usuarios, rutas, límites) está aislado en su propia sección y no afecta a los tokens ya emitidos.

---

## 7. Autorización: políticas y roles

Definidas en `Program.cs:90-93` con los nombres de `Auth/Politicas.cs`:

| Política | Exige | Uso |
|---|---|---|
| `Autenticado` | Token válido | Rutas normales (`jsonplaceholder-users`, `jsonplaceholder-posts`). |
| `Integracion` | Token válido **y** rol `integracion` | Preparada para las rutas `/{servicio}/api/integracion/**` de los microservicios. Solo `admin` tiene ese rol. |
| **FallbackPolicy** | Token válido | Se aplica a cualquier endpoint que **no** declare política. Red de seguridad: si alguien agrega un endpoint y se olvida, queda protegido. |

- Las rutas de YARP eligen su política en el JSON con `"AuthorizationPolicy": "Autenticado"`.
- Los endpoints públicos lo dicen explícitamente con `.AllowAnonymous()` (`/health` en `Program.cs:106`, login en
  `LoginEndpoints.cs:34`).
- El nombre en el JSON debe coincidir **exactamente** con el registrado en `AddPolicy`; si no, YARP falla al arrancar.

Usuarios y roles actuales (`appsettings.json`, sección `Auth:Usuarios`):

| Usuario | Contraseña | Roles |
|---|---|---|
| `admin` | `admin123` | `admin`, `integracion` |
| `nutricionista` | `nutri123` | `nutricionista` |
| `cocina` | `cocina123` | `cocina` |
| `repartidor` | `repartidor123` | `repartidor` |

---

## 8. El proxy (YARP): rutas, clusters y transforms

YARP se registra en `Program.cs:96-98` y se activa con `app.MapReverseProxy()` (`:108`). **Toda** la definición
está en `appsettings.json` → sección `ReverseProxy` (regla del proyecto: rutas y clusters nunca en código).

### Anatomía de una ruta (`appsettings.json:50-61`)

```jsonc
"jsonplaceholder-users": {                       // id de la ruta (aparece en logs y en el test de políticas)
  "ClusterId": "jsonplaceholder",                // a qué cluster se envía
  "AuthorizationPolicy": "Autenticado",          // OBLIGATORIO en este proyecto
  "Match": {
    "Path": "/api/users/{**resto}",              // {**resto} = cualquier cosa después, incluido nada
    "Methods": [ "GET" ]                         // solo GET; POST/PUT/DELETE no hacen match
  },
  "Transforms": [
    { "PathRemovePrefix": "/api" },              // /api/users/1 → /users/1
    { "RequestHeaderRemove": "Authorization" }   // nunca enviar nuestro token a un tercero
  ]
}
```

### Anatomía de un cluster (`appsettings.json:75-82`)

```jsonc
"jsonplaceholder": {
  "Destinations": {
    "d1": { "Address": "https://jsonplaceholder.typicode.com" }   // varios destinos = balanceo
  }
}
```

### Detalles a tener en cuenta

- **`POST /api/users` da 404 o 405**: ninguna ruta hace match con `POST`, así que no hay endpoint y nunca llega
  al upstream (lo comprueba un test).
- **YARP copia casi todo** (método, query string, body, headers) salvo lo que los transforms quiten o cambien.
- **Transforms útiles** para el futuro: `PathPrefix` (agregar prefijo), `PathPattern` (reescribir ruta completa),
  `RequestHeader` (poner un header), `RequestHeaderOriginalHost`, `X-Forwarded` (viene activo por defecto),
  `ResponseHeaderRemove`.

---

## 9. Propagación de identidad (`X-User-*`)

`Proxy/IdentidadTransformProvider.cs` se aplica a **todas** las rutas (`AddTransforms<...>` en `Program.cs:98`):

1. **Siempre** borra `X-User-Id`, `X-User-Name` y `X-User-Roles` que vengan del cliente (`:32-34`). Si no, cualquiera
   podría mandar `X-User-Id: admin` y un microservicio le creería (**suplantación**).
2. **Solo si** la ruta tiene `"Metadata": { "PropagarIdentidad": "true" }` (`:25`) y el usuario está autenticado,
   los vuelve a poner con los datos **del token** (`:37-42`):

| Header | Valor |
|---|---|
| `X-User-Id` | claim `sub` |
| `X-User-Name` | claim `name` |
| `X-User-Roles` | todos los `role` separados por coma (`admin,integracion`) |

Hoy **ninguna ruta** tiene `PropagarIdentidad`: jsonplaceholder es un tercero y no debe recibir datos de nuestros
usuarios. Está preparado para las rutas de los microservicios ([sección 14](#14-usarlo-con-los-microservicios)).

---

## 10. Rate limit, health y formato de errores

### Rate limit del login (`Program.cs:32-49`)

- Ventana fija de 1 minuto, `Login:IntentosPorMinuto` intentos (**10** por defecto, `appsettings.json:45`),
  **por IP** del cliente.
- Al superarlo: **429 `DEMASIADOS_INTENTOS`** con header `Retry-After: 60`.
- Cuenta todos los intentos (buenos y malos). Solo se aplica a `/api/login` (por `RequireRateLimiting`).
- En los tests no hay IP (TestServer), así que todos comparten la misma "partición" → por eso la factory normal usa
  un límite de 1000 y el test del 429 usa una factory propia con límite 3.

### `/health`

`app.MapHealthChecks("/health").AllowAnonymous()` (`Program.cs:106`): devuelve `200 Healthy` sin token. Sirve para
Docker, balanceadores o simplemente saber si el gateway está vivo. No comprueba jsonplaceholder.

### Formato de errores (`Errores/RespuestasError.cs`)

Todo error **propio del gateway** responde así:

```json
{ "codigo": "NO_AUTENTICADO", "mensaje": "Falta el token Bearer o no es válido." }
```

| Status | `codigo` | Cuándo |
|---|---|---|
| 400 | `SOLICITUD_INVALIDA` | Login sin `username` o `password` |
| 401 | `CREDENCIALES_INVALIDAS` | Usuario o contraseña incorrectos |
| 401 | `NO_AUTENTICADO` | Sin token, firma inválida, otro `iss`/`aud`, algoritmo no permitido |
| 401 | `TOKEN_EXPIRADO` | `exp` pasado (más de 30 s) |
| 403 | `SIN_PERMISO` | Token válido pero sin el rol requerido |
| 429 | `DEMASIADOS_INTENTOS` | Demasiados logins en un minuto |

Los errores que **no** son del gateway (404/405 de ruta inexistente, 502 si el upstream no responde, o los
errores que devuelva el propio upstream) no tienen este formato.

---

## 11. Tests: cómo funcionan, cómo se ejecutan y cómo saber que corrieron

### 11.1 La idea en una frase

Los tests **arrancan el gateway real en memoria** (el mismo `Program.cs`), le mandan peticiones HTTP como lo haría
un cliente, y **sustituyen jsonplaceholder por un servidor falso (WireMock)** que registra todo lo que recibe. Así
se puede comprobar no solo lo que responde el gateway, sino **lo que le llega al servicio de atrás**, sin Internet.

```text
   Test (xUnit)
      │  HttpClient en memoria (sin puerto, sin red)
      ▼
   Gateway real (Program.cs) ← configuración cambiada por GatewayFactory
      │  HTTP real a localhost:<puerto aleatorio>
      ▼
   WireMock (jsonplaceholder falso)  ── LogEntries: "recibí GET /users sin Authorization"
```

### 11.2 Las piezas

| Pieza | Qué hace |
|---|---|
| **xUnit** | El framework de tests. Cada método con `[Fact]` es un test; `[Theory]` + `[InlineData(...)]` es el mismo test con varios datos (cada fila cuenta como un test). |
| **`WebApplicationFactory<Program>`** (paquete `Microsoft.AspNetCore.Mvc.Testing`) | Ejecuta el `Program.cs` del gateway dentro del proceso de tests con un `TestServer`. `factory.CreateClient()` devuelve un `HttpClient` que habla con él directamente. Por eso existe `public partial class Program { }` en `Program.cs:112`: hace visible la clase `Program` al proyecto de tests. |
| **`GatewayFactory`** | Hereda de la anterior y, en `ConfigureWebHost`, cambia la configuración **antes** de que arranque: entorno `Testing`, `Jwt:Key` de prueba, límite de login 1000 y — lo más importante — la dirección del cluster `jsonplaceholder` = la URL del WireMock. Al destruirse, detiene el WireMock. |
| **`GatewayFactoryLimitado`** | Igual, pero con límite de login 3, para probar el 429 sin afectar al resto. |
| **WireMock.Net** | Servidor HTTP de mentira. `WireMockServer.Start()` lo levanta en un puerto libre. Con `Given(...).RespondWith(...)` se le dice qué responder (*stub*) y con `LogEntries` se ve **cada petición que recibió** (ruta, método, headers, body). `Reset()` borra stubs y log. |
| **`TokenDePrueba`** | Crea tokens sin pasar por el login, para poder fabricar los **malos**: firmados con otra clave, vencidos, con otro emisor o audiencia, con roles concretos. |
| **`IClassFixture<T>`** | xUnit crea **una sola** factory (un solo gateway + un WireMock) por clase de tests y la comparte. Pero crea **una instancia nueva de la clase por cada test**, así que el constructor corre antes de cada test: por eso ahí se hace `Upstream.Reset()` y se registran los stubs. |

### 11.3 Anatomía de un test (patrón Arrange / Act / Assert)

```csharp
[Fact]
public async Task Users_con_token_valido_reenvia_a_users_sin_prefijo_api()
{
    // Arrange: (en el constructor) WireMock responde 200 a GET /users
    // Act: el cliente llama al gateway con un token válido
    var respuesta = await Get("/api/users", TokenDePrueba.Crear());

    // Assert 1: lo que ve el cliente
    Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
    Assert.Contains("Leanne Graham", await respuesta.Content.ReadAsStringAsync());

    // Assert 2: lo que recibió el upstream (exactamente 1 llamada, a /users sin /api)
    var llamada = Assert.Single(_factory.Upstream.LogEntries);
    Assert.Equal("/users", llamada.RequestMessage!.Path);
}
```

### 11.4 Qué protege cada test

| Clase · Test | Si falla, significa que… |
|---|---|
| `LoginTests` · `Login_con_credenciales_validas_devuelve_200_y_token` | El login, el formato de la respuesta o los claims (`sub`, `role`) cambiaron. |
| · `Login_con_contrasena_incorrecta_devuelve_401` / `..._usuario_inexistente_...` | Se aceptan credenciales malas o cambió el código de error. |
| · `Login_sin_campos_obligatorios_devuelve_400` (3 casos) | Falta la validación de entrada. |
| · `Login_no_distingue_mayusculas_en_el_username` | La búsqueda de usuario volvió a ser sensible a mayúsculas. |
| `LoginRateLimitTests` · `Cuarto_login_en_un_minuto_devuelve_429_con_retry_after` | El rate limit del login no funciona. |
| `ProxyJsonPlaceholderTests` · `Users_sin_token_devuelve_401_y_no_llama_al_upstream` | Una ruta quedó pública o se llama al upstream sin autenticar. |
| · `Token_firmado_con_otra_clave_devuelve_401` | No se verifica la firma. |
| · `Token_expirado_hace_5_minutos_devuelve_401_token_expirado` | No se valida `exp`, el `ClockSkew` es enorme o `OnChallenge` cambió. |
| · `Token_con_otro_iss_o_aud_devuelve_401` (2 casos) | No se valida emisor o audiencia. |
| · `Users_con_token_valido_reenvia_a_users_sin_prefijo_api` | Se rompió el enrutado o el `PathRemovePrefix`. |
| · `Users_con_token_valido_no_reenvia_authorization_al_tercero` | **Se filtra el token** a jsonplaceholder. |
| · `Posts_con_token_valido_reenvia_a_posts` | Se rompió la ruta de posts. |
| · `Post_a_users_no_esta_enrutado_y_no_llega_al_upstream` | Se abrió un método HTTP no previsto. |
| · `Health_sin_token_devuelve_200` | `/health` pide token o dejó de existir. |
| · `Todas_las_rutas_del_proxy_declaran_authorization_policy` | Alguien añadió una ruta en el JSON sin `AuthorizationPolicy`. |
| `IdentidadTransformTests` · `Un_X_User_Id_falso_no_llega_a_jsonplaceholder` | Se puede suplantar identidad o se filtra a un tercero. |

### 11.5 Cómo se ejecutan

Desde la raíz del repo (donde está `ApiGateway.slnx`):

```powershell
dotnet test                                                    # compila y ejecuta todo; resumen al final
dotnet test --logger "console;verbosity=normal"                # además lista cada test: "Correctas <nombre> [ms]"
dotnet test --filter "FullyQualifiedName~LoginTests"           # solo una clase
dotnet test --filter "FullyQualifiedName~Token_expirado"       # solo los tests cuyo nombre contenga eso
dotnet test --logger "trx;LogFileName=resultados.trx"          # informe en tests/ApiGateway.Tests/TestResults/
```

En **Visual Studio**: menú *Prueba → Explorador de pruebas* → *Ejecutar todas*. Muestra el árbol de tests con ✔/✖,
permite depurar uno con un punto de interrupción (clic derecho → *Depurar*).

### 11.6 Cómo saber que realmente se ejecutaron

1. **El resumen final** de `dotnet test` (el SDK está en español):

   ```text
   Correctas! - Con error:     0, Superado:    20, Omitido:     0, Total:    20, Duración: 3 s - ApiGateway.Tests.dll (net10.0)
   ```

   - `Total` debe coincidir con el número de tests esperado (hoy **20**: 15 `[Fact]` + 5 filas de `[InlineData]` en los `[Theory]`).
     Si agregas un test y `Total` no sube, no se descubrió (¿falta `[Fact]`? ¿el método no es `public`?).
   - `Omitido` debe ser 0 (un `[Fact(Skip = "...")]` aparecería ahí).
2. **Con `verbosity=normal`** aparece una línea `Correctas ...` por test, con su duración. Si un test aparece, se ejecutó.
3. **El código de salida**: `dotnet test` devuelve 0 si todo pasa y distinto de 0 si algo falla. En PowerShell:
   `dotnet test; $LASTEXITCODE`. Es lo que usaría un pipeline de CI.
4. **El archivo `.trx`** (con `--logger trx`) queda como evidencia: se abre con Visual Studio.
5. **La prueba de fuego (prueba de mutación)**: romper algo a propósito y ver el test en rojo. Ejemplo ya hecho:
   se quitó `"AuthorizationPolicy"` de la ruta `jsonplaceholder-posts` y el resultado fue

   ```text
   Con error ApiGateway.Tests.ProxyJsonPlaceholderTests.Todas_las_rutas_del_proxy_declaran_authorization_policy
      Error: La ruta 'jsonplaceholder-posts' no declara AuthorizationPolicy.
   Con error! - Con error: 1, Superado: 0, Omitido: 0, Total: 1
   ```

   Después se restauró el archivo. Si rompes algo y **ningún** test se pone en rojo, ese comportamiento no está
   cubierto.

### 11.7 Cómo leer un fallo

```text
Con error ApiGateway.Tests.ProxyJsonPlaceholderTests.Users_sin_token_devuelve_401_y_no_llama_al_upstream
  Error Message:
   Assert.Equal() Failure: Values differ
   Expected: Unauthorized
   Actual:   OK
  Stack Trace:
   at ...ProxyJsonPlaceholderTests.cs:line 56
```

`Expected` es lo que el test esperaba, `Actual` lo que pasó, y la línea dice qué `Assert` falló. En este ejemplo
una ruta dejó de exigir token.

### 11.8 Cómo agregar un test

1. Elige la clase según el tema (o crea una con `IClassFixture<GatewayFactory>`).
2. Nombre en español que describa el comportamiento: `Algo_en_tal_situacion_devuelve_tal_cosa`.
3. Si necesitas que el upstream responda algo distinto, registra el stub en el test o en el constructor.

```csharp
[Fact]
public async Task Users_por_id_reenvia_el_id_al_upstream()
{
    _factory.Upstream
        .Given(Request.Create().WithPath("/users/7").UsingGet())
        .RespondWith(Response.Create().WithStatusCode(200).WithBody("""{"id":7}"""));

    var respuesta = await Get("/api/users/7", TokenDePrueba.Crear());

    Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
    Assert.Equal("/users/7", Assert.Single(_factory.Upstream.LogEntries).RequestMessage!.Path);
}
```

4. Si un test necesita una configuración distinta (otro límite, otro entorno), crea una factory derivada como
   `GatewayFactoryLimitado` en lugar de cambiar la compartida.
5. `dotnet test` y comprueba que `Total` subió.

---

## 12. Ejecutar y probar a mano

### Levantar

```powershell
dotnet run --project src/ApiGateway          # http://localhost:8090, entorno Development (clave de desarrollo)
docker compose up --build -d                 # http://localhost:8090 → contenedor :8080, entorno Production
$env:JWT_KEY="mi-clave-propia-de-al-menos-32-bytes-123456"; docker compose up --build -d   # con clave propia
```

### Probar con PowerShell

```powershell
$login = Invoke-RestMethod -Method Post -Uri http://localhost:8090/api/login `
  -ContentType 'application/json' -Body '{"username":"admin","password":"admin123"}'
$h = @{ Authorization = "Bearer $($login.access_token)" }

Invoke-RestMethod http://localhost:8090/api/users -Headers $h          # lista de usuarios
Invoke-RestMethod http://localhost:8090/api/posts/1 -Headers $h        # un post
Invoke-RestMethod http://localhost:8090/health                         # Healthy
```

### Probar con curl (Git Bash)

```bash
TOKEN=$(curl -s -X POST http://localhost:8090/api/login -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"admin123"}' | sed -E 's/.*"access_token":"([^"]+)".*/\1/')
curl -i http://localhost:8090/api/users -H "Authorization: Bearer $TOKEN"
curl -i http://localhost:8090/api/users                                 # 401 NO_AUTENTICADO
```

### Ver qué hay dentro del token

- Pegarlo en <https://jwt.io> (es una herramienta del navegador; con tokens de desarrollo no hay problema, **nunca**
  con tokens reales de producción).
- O en PowerShell, decodificando la parte central:

```powershell
$p = $login.access_token.Split('.')[1].Replace('-','+').Replace('_','/')
$p += '=' * ((4 - $p.Length % 4) % 4)
[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($p))
```

---

## 13. Puntos sensibles: dónde un cambio pequeño rompe mucho

| Si tocas… | Qué pasa | Síntoma | Test que lo detecta |
|---|---|---|---|
| `Jwt:Key` / `Jwt__Key` | Todos los tokens ya emitidos dejan de validar | 401 para todos los que tenían sesión | — (es esperado) |
| `Jwt:Issuer` o `Jwt:Audience` | Igual: emisor y validador leen la misma sección | 401 con tokens viejos | `Login_con_credenciales_validas...` si los tests usan otros valores |
| `ClaveDeFirma.Crear` | La usan **los dos lados** | 401 siempre con tokens externos | `Token_*`, `Users_con_token_valido...` |
| Quitar `MapInboundClaims = false` | `role`, `sub`, `name` se renombran a URIs largas | 403 en rutas con rol; `X-User-*` vacíos | (aparecerá con las rutas de microservicios) |
| `RoleClaimType` o el nombre `"role"` del emisor (solo uno) | Los roles dejan de reconocerse | 403 `SIN_PERMISO` | Igual que el anterior |
| `ClockSkew` | Cambia cuánto "sobrevive" un token vencido | Tokens vencidos aceptados | `Token_expirado_hace_5_minutos...` (si > 5 min) |
| Orden de `UseAuthentication` / `UseAuthorization` | La política se evalúa sin usuario | 401 en todo | Casi todos |
| Quitar `SetFallbackPolicy` | Un endpoint nuevo sin política queda público | Nada visible | Solo una revisión |
| Renombrar una política en `Politicas.cs` y no en el JSON | YARP no encuentra la política | El gateway no arranca | Todos (fallan al crear la factory) |
| Quitar `AuthorizationPolicy` de una ruta | Ruta solo protegida por la Fallback… o pública si también falta | — | `Todas_las_rutas_del_proxy_declaran_authorization_policy` |
| `ClusterId` mal escrito | Ruta sin cluster | El gateway no arranca | Todos |
| Nombre del cluster (`jsonplaceholder`) | `GatewayFactory` y las variables de entorno lo sobrescriben **por nombre** | Los tests llaman a Internet o fallan | `Users_con_token_valido...` |
| Quitar `PathRemovePrefix` | El upstream recibe `/api/users` | 404 del upstream | `Users_con_token_valido_reenvia_a_users_sin_prefijo_api` |
| Quitar `RequestHeaderRemove: Authorization` | **Se filtra el token** a un tercero | Nada visible para el cliente | `Users_con_token_valido_no_reenvia_authorization_al_tercero` |
| Dejar de borrar `X-User-*` en `IdentidadTransformProvider` | Suplantación de usuarios | Nada visible | `Un_X_User_Id_falso_no_llega_a_jsonplaceholder` |
| Agregar `PropagarIdentidad` a una ruta de tercero | Se filtra la identidad | Nada visible | `Un_X_User_Id_falso_no_llega_a_jsonplaceholder` |
| Quitar `.AllowAnonymous()` del login | Hace falta token para obtener un token | 401 en el login | Todos los `LoginTests` |
| Quitar `public partial class Program { }` | Los tests no compilan | Error de compilación | Compilación |
| `Login:IntentosPorMinuto` muy bajo | Bloquea usuarios legítimos | 429 | — |

---

## 14. Usarlo con los microservicios

> **Estado:** este repo (Actividad 1) solo enruta jsonplaceholder. Todo lo necesario para los microservicios
> **ya está preparado en el código** (política `Integracion`, `IdentidadTransformProvider` con `PropagarIdentidad`,
> rutas y clusters por configuración). Falta **solo configuración**. La implementación completa de referencia está
> en `..\implementation\api-gateway` (`appsettings.Sistema.json`) y en la guía
> `Guias API Gateway\02 - Microservicios - Modo hibrido.md`.

### 14.1 La regla de mapeo

```text
Directo al microservicio:   http://localhost:5010/api/pacientes
A través del gateway:       http://localhost:8090/pacientes/api/pacientes   + Authorization: Bearer <token>
                                               └────────┘
                                 prefijo del servicio (el gateway lo quita con PathRemovePrefix)
```

| Prefijo público | Cluster | Local (`dotnet run`) | Docker (misma red) | Ruta que recibe el ms |
|---|---|---|---|---|
| `/pacientes/api/**` | `pacientes` | `http://localhost:5010` | `http://ms-pacientes:8080` | `/api/**` |
| `/plan-nutricional/api/**` | `plannutricional` | `http://localhost:5020` | `http://ms-plan-nutricional:8080` | `/api/**` |
| `/contratos/api/**` | `contratos` | `http://localhost:5030` | `http://ms-contratos-facturacion:8080` | `/api/**` |
| `/catering/api/**` | `catering` | `http://localhost:5040` | `http://ms-catering:8080` | `/api/**` |
| `/produccion/api/**` | `produccion` | `http://localhost:5050` | `http://ms-produccion-alimentos:8080` | `/api/**` |
| `/logistica/api/**` | `logistica` | `http://localhost:5060` | `http://ms-logistica-entrega:8080` | `/api/**` |
| `/{servicio}/api/integracion/**` | el del servicio | ídem | ídem | `/api/integracion/**` (exige rol `integracion`) |

- **Id de cluster sin guion** (`plannutricional`): las variables de entorno con guion dan problemas en bash. El
  prefijo público sí puede llevarlo.
- Si un microservicio no está levantado, solo sus rutas responden **502**; el resto sigue funcionando.

### 14.2 Qué agregar (por cada microservicio)

Crear `src/ApiGateway/appsettings.Sistema.json` (se carga con `ASPNETCORE_ENVIRONMENT=Sistema`) y **sumar** rutas
y clusters a las de `appsettings.json` (los objetos JSON se fusionan; las rutas de jsonplaceholder siguen activas).
Ejemplo para `ms-pacientes`:

```jsonc
{
  "ReverseProxy": {
    "Routes": {
      // 1) Específica: endpoints de integración, solo con rol "integracion". Order bajo = se evalúa primero.
      "pacientes-integracion": {
        "ClusterId": "pacientes", "Order": 10, "AuthorizationPolicy": "Integracion",
        "Match": { "Path": "/pacientes/api/integracion/{**resto}" },
        "Metadata": { "PropagarIdentidad": "true" },
        "Transforms": [ { "PathRemovePrefix": "/pacientes" } ]
      },
      // 2) General: todo lo demás del servicio, con cualquier usuario autenticado y cualquier método.
      "pacientes": {
        "ClusterId": "pacientes", "Order": 100, "AuthorizationPolicy": "Autenticado",
        "Match": { "Path": "/pacientes/api/{**resto}" },
        "Metadata": { "PropagarIdentidad": "true" },
        "Transforms": [ { "PathRemovePrefix": "/pacientes" } ]
      }
    },
    "Clusters": {
      "pacientes": {
        "Destinations": { "d1": { "Address": "http://ms-pacientes:8080" } }
      }
    }
  }
}
```

Diferencias con las rutas de jsonplaceholder (y por qué):

| | jsonplaceholder (tercero) | Microservicio (propio) |
|---|---|---|
| `Methods` | Solo `GET` | Sin restricción (GET, POST, PUT, DELETE…) |
| `RequestHeaderRemove: Authorization` | Sí (no filtrar el token) | No hace falta (es nuestro), aunque se puede quitar igual |
| `PropagarIdentidad` | **No** | **Sí**: el ms recibe `X-User-Id/Name/Roles` |
| `Order` | — | `10` en la de integración, `100` en la general. Sin `Order`, `/pacientes/api/{**resto}` también "atrapa" `/pacientes/api/integracion/...` y se saltaría el rol. |

Y un perfil en `Properties/launchSettings.json` para correrlo local apuntando a los puertos 5010-5060:

```jsonc
"sistema-local": {
  "commandName": "Project",
  "applicationUrl": "http://localhost:8090",
  "environmentVariables": {
    "ASPNETCORE_ENVIRONMENT": "Sistema",
    "Jwt__Key": "clave-de-desarrollo-local-de-al-menos-32-bytes!!",
    "ReverseProxy__Clusters__pacientes__Destinations__d1__Address": "http://localhost:5010"
    // … una línea por cluster
  }
}
```

Para apuntar a un servicio que corre en otra máquina **no hay que tocar código**, solo una variable:
`ReverseProxy__Clusters__catering__Destinations__d1__Address=http://192.168.1.50:5040`.

### 14.3 Cómo debe leer la identidad un microservicio

El microservicio **no valida el JWT** (no tiene la clave y no debería tenerla): confía en el gateway y lee los
headers. Mínimo necesario en la capa WebApi del ms:

```csharp
public sealed record UsuarioActual(string Id, string Nombre, IReadOnlyList<string> Roles)
{
    public bool TieneRol(string rol) => Roles.Contains(rol, StringComparer.OrdinalIgnoreCase);

    public static UsuarioActual? Desde(HttpRequest request)
    {
        var id = request.Headers["X-User-Id"].ToString();
        if (string.IsNullOrEmpty(id)) return null;
        var roles = request.Headers["X-User-Roles"].ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new UsuarioActual(id, request.Headers["X-User-Name"].ToString(), roles);
    }
}

// En un controller:
var usuario = UsuarioActual.Desde(Request);
if (usuario is null || !usuario.TieneRol("nutricionista")) return Forbid();
```

Condiciones para que esto sea seguro:

- **El ms no debe ser accesible desde fuera** salvo a través del gateway (en Docker: no publicar sus puertos, solo
  la red interna). Si alguien llega directo al ms, puede inventarse los headers.
- Tolerar que la identidad falte (`UsuarioActual?`): llamadas directas en desarrollo o eventos internos.
- Si se quiere que los ms validen el token por su cuenta (defensa en profundidad), lo correcto es pasar a firma
  asimétrica (RS256) o a Keycloak, para repartir solo la **clave pública**.

### 14.4 Trampas al poner un ms detrás del gateway

- **URLs que genera el ms**: un `201 Created` con `Location: /api/pacientes/1` no lleva el prefijo `/pacientes`. O
  el cliente arma las URLs con la tabla de 14.1, o se envía `X-Forwarded-Prefix` desde la ruta
  (`{ "RequestHeader": "X-Forwarded-Prefix", "Set": "/pacientes" }`) y el ms usa `UseForwardedHeaders`.
- **Llamadas ms → ms**: no deben pasar por el gateway con un token de usuario. Van directo por la red interna o por
  eventos.
- **Front-end en el navegador** en otro origen: hace falta CORS (receta 15.9).
- **Páginas HTML** servidas por un ms no se pueden abrir escribiendo la URL en el navegador a través del gateway: el
  navegador no manda el `Bearer`.

### 14.5 Cómo testearlo

Igual que con jsonplaceholder: una factory con **un WireMock por cluster** que sobrescribe cada `Address` y usa
`UseEnvironment("Sistema")`. Tests mínimos por servicio: llega la ruta sin prefijo, llegan los `X-User-*` del token,
un `X-User-Id` falso se reemplaza, `/integracion` sin rol da 403 y no llega al ms, sin token da 401. Referencia:
`..\implementation\api-gateway\tests\ApiGateway.Tests\SistemaFactory.cs` y `SistemaRoutingTests.cs`.

---

## 15. Recetas: cómo agregar cosas

### 15.1 Agregar o cambiar un usuario
En `appsettings.json` → `Auth:Usuarios`, agregar `{ "Username", "Password", "Roles": [...] }`. Se aplica sin
reiniciar (el store usa `IOptionsMonitor`). No toca tokens existentes.

### 15.2 Agregar un rol con su política
1. Dar el rol a un usuario (`Roles`).
2. En `Auth/Politicas.cs`: `public const string Cocina = "Cocina"; public const string RolCocina = "cocina";`
3. En `Program.cs` (bloque `AddAuthorizationBuilder`, línea 90):
   `.AddPolicy(Politicas.Cocina, p => p.RequireAuthenticatedUser().RequireRole(Politicas.RolCocina))`
4. En la ruta del JSON: `"AuthorizationPolicy": "Cocina"`.
5. Test: token con y sin el rol → 200 / 403 `SIN_PERMISO`.

### 15.3 Ruta pública (sin token)
YARP acepta `"AuthorizationPolicy": "Anonymous"`. Úsalo con mucho cuidado; el test de políticas la acepta porque
no está vacía, así que conviene documentarlo y añadir un test que la cubra.

### 15.4 Otro servicio externo (tercero)
Copiar el patrón de jsonplaceholder: cluster nuevo, ruta con `Methods` limitados, `PathRemovePrefix`,
`RequestHeaderRemove: Authorization` y **sin** `PropagarIdentidad`. Test: el upstream no recibe `Authorization`.

### 15.5 Un microservicio nuevo (p. ej. `ms-notificaciones`, puerto 5070)
Par de rutas `notificaciones-integracion` (Order 10) y `notificaciones` (Order 100) + cluster `notificaciones` como
en 14.2, línea en el perfil `sistema-local` y en la factory de tests.

### 15.6 Duración del token
`Jwt:ExpiresMinutes` (1 a 1440). Solo afecta a los tokens nuevos. El test de login espera `expires_in = 3600`: si
cambias el valor por defecto, ajusta el test.

### 15.7 Permitir otro método HTTP
Añadirlo a `Match.Methods` de la ruta (`[ "GET", "POST" ]`). Actualizar el test
`Post_a_users_no_esta_enrutado_y_no_llega_al_upstream`.

### 15.8 Rate limit en otras rutas
Crear otra política con `o.AddPolicy(...)` en `AddRateLimiter` y asignarla a la ruta YARP con
`"RateLimiterPolicy": "nombre"`.

### 15.9 CORS para un front-end
```csharp
builder.Services.AddCors(o => o.AddPolicy("front", p =>
    p.WithOrigins("http://localhost:5173").AllowAnyHeader().AllowAnyMethod()));
// …
app.UseCors("front");   // antes de UseAuthentication
```
Y en cada ruta YARP: `"CorsPolicy": "front"`.

### 15.10 Cambiar el emisor por Keycloak u otro IdP
- **Emisión**: otra implementación de `ITokenIssuer` (o eliminar el login y que el cliente obtenga el token del IdP).
- **Validación**: en `AddJwtBearer`, usar `Authority` (el IdP publica sus claves) en lugar de `IssuerSigningKey`, y
  ajustar `ValidIssuer`, `ValidAudience` y `RoleClaimType` al formato de ese IdP.
- Rutas, políticas, transforms y tests de proxy no cambian. Guía: `Guias API Gateway\04 - Opcional - Keycloak.md`.

---

## 16. Seguridad: qué está cubierto y qué no

**✅ Cubierto**
- Firma, algoritmo, emisor, audiencia y vencimiento verificados en cada petición.
- Ninguna ruta sin política (FallbackPolicy + test que recorre la configuración).
- No se reenvía `Authorization` ni `X-User-*` a terceros; `X-User-*` del cliente siempre se descartan.
- Comparación de contraseñas en tiempo constante y mismo error para usuario inexistente y contraseña mala.
- Rate limit del login.
- Clave validada al arranque (≥ 32 bytes) y fuera del repo en producción (`Jwt__Key`).
- Contenedor con usuario no-root.

**⚠️ No cubierto (aceptable para la actividad, a resolver antes de producción)**
- Contraseñas en texto plano en `appsettings.json` (debería ser una base de datos con hash tipo PBKDF2/bcrypt).
- Sin *refresh token* ni revocación: un token robado sirve hasta que vence (60 min).
- HS256 con clave compartida: si algún día los ms validan el token, habría que repartir el secreto (mejor RS256).
- Sin HTTPS en el contenedor (se espera un balanceador o proxy TLS delante).
- El rate limit es por IP y en memoria: detrás de otro proxy todos comparten IP, y con varias réplicas cada una
  cuenta por separado.
- La clave por defecto de `docker-compose.yml` es pública: cámbiala con `JWT_KEY` fuera de la demo.

---

## 17. Diagnóstico rápido y preguntas frecuentes

| Síntoma | Causa probable |
|---|---|
| **401 `NO_AUTENTICADO`** | Falta `Authorization: Bearer`, token copiado incompleto, se cambió `Jwt:Key`/`Issuer`/`Audience`, o el token es de otro entorno (Docker y `dotnet run` usan claves distintas). |
| **401 `TOKEN_EXPIRADO`** | Pasaron más de 60 min. Volver a hacer login. |
| **401 `CREDENCIALES_INVALIDAS`** | Usuario o contraseña mal (la contraseña sí distingue mayúsculas). |
| **403 `SIN_PERMISO`** | Token válido pero sin el rol de la política. |
| **404** | Ninguna ruta coincide (ruta mal escrita o falta el prefijo). |
| **405** | La ruta existe pero el método no está en `Methods`. |
| **429** | Más de 10 logins por minuto desde la misma IP. Esperar `Retry-After` segundos. |
| **502 / 504** | El upstream no responde (jsonplaceholder caído, sin Internet, o el ms no está levantado). |
| El gateway no arranca | `Jwt:Key` vacía o corta (en `Production` hay que pasar `Jwt__Key`), política inexistente en una ruta, `ClusterId` mal escrito. El mensaje de error lo dice. |

**¿Por qué mi token de `dotnet run` no sirve en Docker?** Porque usan claves distintas (Development vs `Jwt__Key`
del compose). Un token solo vale donde se firmó con la misma clave.

**¿Por qué los tests no necesitan Internet?** Porque `GatewayFactory` cambia la dirección del cluster a WireMock
antes de que el gateway arranque.

**¿Los tests usan el puerto 8090?** No. `TestServer` no abre puertos; solo WireMock abre uno aleatorio en
`localhost`. Puedes tener el gateway corriendo mientras ejecutas los tests.

**¿Por qué el `X-User-Id` no le llega a jsonplaceholder si tengo token?** Porque esa ruta no tiene
`PropagarIdentidad`. Es intencional: es un tercero.

**¿Dónde cambio el puerto?** `dotnet run`: `launchSettings.json` (`applicationUrl`). Docker: `ports` en
`docker-compose.yml` (lado izquierdo de `8090:8080`).

---

## 18. Chuleta de comandos

```powershell
# Compilar y probar
dotnet build ApiGateway.slnx
dotnet test
dotnet test --logger "console;verbosity=normal"
dotnet test --filter "FullyQualifiedName~ProxyJsonPlaceholderTests"
dotnet test --logger "trx;LogFileName=resultados.trx"

# Ejecutar
dotnet run --project src/ApiGateway                     # http://localhost:8090

# Docker
docker compose up --build -d
docker compose logs -f api-gateway
docker compose down

# Probar
$t = (Invoke-RestMethod -Method Post http://localhost:8090/api/login -ContentType 'application/json' `
      -Body '{"username":"admin","password":"admin123"}').access_token
Invoke-RestMethod http://localhost:8090/api/users -Headers @{ Authorization = "Bearer $t" }
```
