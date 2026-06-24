# Control de Lavados

Aplicación web interna para **registrar lavados y asignar operarios**, calculando automáticamente los tiempos de cada etapa del proceso. Cubre dos circuitos:

- **Lavado de Camiones** — proceso de 4 etapas (Atraco → Inicio lavado → Fin lavado → Desatraco), con hasta **2 dársenas en paralelo**.
- **Fábrica de Hielo** — proceso de 2 etapas (Inicio lavado → Fin lavado).

Cada lavado guarda los operarios asignados (de **Offal** o de **Agencia**) e incidencias; en camiones además registra frigorífico, tambores y pallets. Una grilla muestra el resumen con todos los tiempos y métricas calculadas.

---

## Tecnologías

| Capa | Tecnología |
|------|------------|
| Framework | **.NET 9** |
| UI | **Blazor Web App** (render mode `InteractiveServer`) |
| ORM / Datos | **Entity Framework Core 9** |
| Base de datos | **SQL Server** (LocalDB en desarrollo) |
| Lenguaje | **C#** + Razor |
| Estilos | CSS propio (tema oscuro, mobile-first, sin dependencias externas) |

> Esquema creado automáticamente con `EnsureCreated()` — no usa migraciones.

---

## Funcionalidades

- **Alta de lavados** con desplegables de patente/sector, dársena, frigorífico, tambores y pallets (según el circuito).
- **Selección de operarios** mediante combo box, diferenciados en Offal / Agencia, con varios operarios por lavado.
- **Validación de datos obligatorios** y reglas de disponibilidad: no se puede reasignar una patente, dársena u operario que ya está en un lavado en curso.
- **Botones de etapa secuenciales**: cada etapa se habilita solo cuando se completó la anterior y estampa la hora actual.
- **Tarjetas "En curso"** que permiten avanzar, editar o eliminar lavados activos (varios a la vez).
- **Grilla** que replica la planilla operativa con columnas calculadas:
  - Turno **automático** según la hora de inicio (Mañana / Tarde / Noche).
  - Conteo de operarios Offal y Agencia.
  - Duración de cada etapa y **tiempo total** de lavado.
  - **Número de semana ISO**, marca temporal, estado, incidencias.

### Estados del proceso

```
Camión:  Pendiente → Atracado → Lavando → Lavado terminado → Finalizado
Hielo:   Pendiente → Lavando → Finalizado
```

---

## Estructura

```
ControlLavados/
├── Models/
│   ├── Lavado.cs          # Entidad principal + columnas calculadas (duraciones, turno, semana…)
│   ├── Enums.cs           # TipoLavado, TipoOperario, estados, turnos
│   └── Catalogo.cs        # Listas fijas: operarios, patentes, dársenas, frigoríficos
├── Data/
│   └── AppDbContext.cs    # DbContext de EF Core
├── Services/
│   └── LavadoService.cs   # Lógica de negocio: crear, avanzar etapas, editar, disponibilidad
├── Components/
│   ├── PanelLavado.razor  # Componente reutilizable (sirve a ambas pantallas según el Tipo)
│   ├── Pages/             # LavadoCamiones (/), FabricaHielo (/hielo)
│   └── Layout/            # Navegación y layout principal
└── wwwroot/app.css        # Estilos
```

---

## Cómo correrlo (desarrollo)

Requisitos: **.NET 9 SDK** y **SQL Server LocalDB** (incluido con Visual Studio / SQL Server Express).

```bash
dotnet run --project ControlLavados.csproj
```

Luego abrir `http://localhost:5136`. La base de datos `ControlLavados` se crea sola en el primer arranque.

### Catálogos (operarios, patentes, etc.)

Se editan directamente en [`Models/Catalogo.cs`](Models/Catalogo.cs) — no requieren base de datos. Cada operario se marca como `Offal` o `Agencia`.
