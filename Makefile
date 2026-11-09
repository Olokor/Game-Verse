# GameVerse developer commands. Works from Git Bash and PowerShell (GNU make).
# Recipes only call dotnet and docker, so there are no shell-specific commands (cp, copy, rm) to diverge.

SLN := backend/GameVerse.slnx
API := backend/src/GameVerse.Api/GameVerse.Api.csproj
SHARED_LIBS := shared/GameVerse.Rules/GameVerse.Rules.csproj shared/GameVerse.Contracts/GameVerse.Contracts.csproj

# docker compose needs .env (POSTGRES_PASSWORD and friends). Fail early with a clear message.
REQUIRE_ENV = $(if $(wildcard .env),,$(error .env not found. Copy .env.example to .env first))

.DEFAULT_GOAL := help
.PHONY: help build test rules up down logs migrate schema

help: ## List targets
	@echo make build    - build the solution - backend and shared
	@echo make test     - run all .NET tests, integration tests need Docker
	@echo make rules    - build GameVerse.Rules + Contracts in Release and copy DLLs into Unity
	@echo make up       - build images and start postgres, redis, api, worker and wait until healthy
	@echo make down     - stop the stack, data volumes are kept
	@echo make logs     - follow api and worker logs
	@echo make migrate  - apply EF Core migrations to the compose database
	@echo make schema   - export the OpenAPI document to packages/api-schema/openapi.json

build: ## Build the whole solution
	dotnet build $(SLN)

test: ## dotnet test for backend + shared
	dotnet test --solution $(SLN)

rules: ## Build shared libraries in Release and copy them to apps/mobile/Assets/GameVerse/Plugins/Shared/
	$(foreach p,$(SHARED_LIBS),dotnet build $(p) -c Release -p:CopyToUnity=true &&) echo Shared libraries delivered to Unity.

up: ## Start the local stack (migrations run automatically before api and worker start)
	$(REQUIRE_ENV)
	docker compose up -d --build --wait

down: ## Stop the local stack
	docker compose down

logs: ## Follow api and worker logs
	docker compose logs -f api worker

migrate: ## Apply migrations (the same one-shot container `make up` runs)
	$(REQUIRE_ENV)
	docker compose run --rm --build migrate

schema: ## Export OpenAPI at build time (no database needed)
	dotnet build $(API) -p:OpenApiGenerateDocuments=true
	@echo Wrote packages/api-schema/openapi.json
	@echo TODO gameverse: TypeScript client generation lands with the pnpm workspace in Prompt 0.5
