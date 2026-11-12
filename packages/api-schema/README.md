# @gameverse/api-schema

The OpenAPI document of GameVerse.Api (`openapi.json`) and the TypeScript types generated from it (`schema.d.ts`). Both are build output: regenerate with `make schema` after changing an endpoint or a Contracts type, and commit them with that change.

The API marks every non-nullable property as `required` (`backend/src/GameVerse.Api/Hosting/OpenApiRequiredProperties.cs`), so a field typed `string` here is guaranteed; nullable C# properties (`string?`) become optional.

The web app uses these types through the `openapi-fetch` client in `apps/web/src/lib/api.ts`. Unity uses the C# Contracts types directly (no generated client).
