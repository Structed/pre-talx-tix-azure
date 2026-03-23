# Copilot Instructions

## Build

```powershell
dotnet build
```

There are no tests or linters configured in this project.

## Architecture

This is a **Pulumi C# (IaC)** project that deploys [pretix](https://pretix.eu/) (ticketing) and [pretalx](https://pretalx.com/) (CfP/scheduling) to **Azure Container Apps**.

**Resource dependency chain** (defined in `Program.cs`):

```
ResourceGroup
├── SecretGenerator (RandomPasswords for DB + app secret keys)
├── PostgreSqlStack (Flexible Server B1ms, two databases: pretix, pretalx)
├── StorageStack (Azure Files, two shares: pretix-data, pretalx-data)
└── ContainerAppsEnvironmentStack (ACA + Log Analytics + storage mounts)
    ├── RedisContainerApp (internal TCP, shared by both apps)
    ├── PretixContainerApp (external HTTPS)
    └── PretalxContainerApp (external HTTPS)
```

Both pretix and pretalx use **standalone Docker images** that bundle nginx + gunicorn + celery worker in a single container, configured entirely via environment variables.

**Redis DB index allocation** — a single Redis instance is shared; apps are isolated by database number:

| DB | Owner | Purpose |
|----|-------|---------|
| 0 | pretix | Cache/sessions |
| 1 | pretix | Celery broker |
| 2 | pretix | Celery backend |
| 3 | pretalx | Cache |
| 4 | pretalx | Celery broker |
| 5 | pretalx | Celery backend |

## Conventions

### Infrastructure file pattern

Every infrastructure file in `Infrastructure/` follows this structure:

1. A `record` for return values (e.g., `PostgreSqlResult`, `StorageResult`)
2. A `public static class` with a single `Create()` method
3. Simple resources use positional parameters; container apps use a dedicated `*Args` record

```csharp
public record FooResult(Output<string> Something, string Name);

public static class FooStack
{
    public static FooResult Create(string prefix, ResourceGroup rg, ...) { ... }
}
```

### Pulumi type disambiguation

`Pulumi.AzureNative` namespaces frequently collide (e.g., `SkuArgs`, `StorageArgs`, `BackupArgs` exist in both resource and input namespaces). Use `using` aliases prefixed with the service abbreviation:

```csharp
using PgSkuArgs = Pulumi.AzureNative.DBforPostgreSQL.Inputs.SkuArgs;
using StorageSkuArgs = Pulumi.AzureNative.Storage.Inputs.SkuArgs;
```

Also watch for `System.IO.FileShare` colliding with `Pulumi.AzureNative.Storage.FileShare` — use fully qualified names.

### Resource naming

All Azure resources are prefixed with the `prefix` Pulumi config value (e.g., `{prefix}-pg`, `{prefix}-pretix`). Storage account names must be ≤24 characters, lowercase alphanumeric only — use `NamingConventions.StorageAccountName()` for this.

### Secrets

- Auto-generated via `Pulumi.Random.RandomPassword` (encrypted in Pulumi state)
- Injected into containers through ACA's `Configuration.Secrets` array
- Referenced in env vars using `SecretRef` (never plain `Value`)
- SMTP password is the only externally-provided secret (`pulumi config set --secret smtpPassword`)

### Container app env var helpers

Both `PretixContainerApp.cs` and `PretalxContainerApp.cs` define private helpers — use the matching one for each env var type:

- `Env(name, value)` — static string
- `EnvOutput(name, value)` — `Output<string>` from another resource
- `EnvSecret(name, secretRef)` — reference to a container app secret

### Pretix vs Pretalx env var formats

- **Pretix**: `PRETIX_{SECTION}_{KEY}` (single underscore) — e.g., `PRETIX_DATABASE_HOST`
- **Pretalx**: `PRETALX_{SECTION}_{KEY}` (also single underscore) — e.g., `PRETALX_DATABASE_HOST`

### Deployment scripts

Scripts in `Scripts/` are PowerShell. `Deploy.ps1` handles first-time setup; `Update.ps1` changes image tags and runs `pulumi up`; `InitApps.ps1` runs post-deploy migrations via `az containerapp exec`.

### Custom domains (two-phase deployment)

Custom domains are optional, configured via `pretixCustomDomain` / `pretalxCustomDomain` Pulumi config. When set, the container app files create a `ManagedCertificate` (CNAME-validated) and bind it via `IngressArgs.CustomDomains`. When not set, the apps use default ACA FQDNs.

`SiteUrl` is auto-derived from the custom domain (e.g., `tickets.example.com` → `https://tickets.example.com`) unless `pretixUrl` / `pretalxUrl` is explicitly set in config.

The ingress args are built separately to conditionally add `CustomDomains` only when a cert exists — this avoids nullable warnings with `InputList<T>`.
