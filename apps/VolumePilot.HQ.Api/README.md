# VolumePilot HQ API

The HQ API is the cloud-facing application boundary for HQ. This bootstrap exposes liveness and API health endpoints so the web client and deployment checks have a stable target.

Run the API locally:

```sh
dotnet run --project apps/VolumePilot.HQ.Api/VolumePilot.HQ.Api.csproj
```

The API listens on `http://localhost:5080` in the supplied launch profile. `GET /health/live` is a process liveness check. `GET /api/health` is the web client's same-origin health check. Neither endpoint reports database health; database readiness is added with the first persistent module.
