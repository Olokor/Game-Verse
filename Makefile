# GameVerse developer commands. Works from Git Bash and PowerShell (GNU make).
# Recipes only call dotnet, docker, node and pnpm, so there are no shell-specific commands (cp, copy, rm) to diverge.

SLN := backend/GameVerse.slnx
# Unity 6000.6.8f1 (ADR-0004). Override with UNITY=/path/to/Unity on other machines or CI.
UNITY ?= C:/Program Files/Unity/Hub/Editor/6000.6.8f1/Editor/Unity.exe
UNITY_PROJECT := apps/mobile
ADB ?= C:/Program Files/Unity/Hub/Editor/6000.6.8f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe
# Error reporting for device builds (ADR-0005): baked in by BuildScript for the duration of `make android`. Empty = off.
UNITY_ERROR_DSN ?= $(shell node infrastructure/scripts/env-get.mjs UNITY_ERROR_DSN)
export UNITY_ERROR_DSN
API := backend/src/GameVerse.Api/GameVerse.Api.csproj
TOKENS := packages/design-tokens
SHARED_LIBS := shared/GameVerse.Rules/GameVerse.Rules.csproj shared/GameVerse.Contracts/GameVerse.Contracts.csproj

# docker compose needs .env (POSTGRES_PASSWORD and friends). Fail early with a clear message.
REQUIRE_ENV = $(if $(wildcard .env),,$(error .env not found. Copy .env.example to .env first))

.DEFAULT_GOAL := help
.PHONY: help build test rules up down logs migrate schema tokens tokens-check install web web-test e2e unity-setup unity-test android device-check errors-up errors-down errors-setup

help: ## List targets
	@echo make build    - build the solution - backend and shared
	@echo make install  - install the pnpm workspace: web, admin, packages
	@echo make test     - run design-token, web and .NET tests, integration tests need Docker
	@echo make rules    - build GameVerse.Rules + Contracts in Release and copy DLLs into Unity
	@echo make up       - build images and start postgres, redis, api, worker and wait until healthy
	@echo make down     - stop the stack, data volumes are kept
	@echo make logs     - follow api and worker logs
	@echo make migrate  - apply EF Core migrations to the compose database
	@echo make schema   - export the OpenAPI document to packages/api-schema/openapi.json
	@echo make tokens   - regenerate CSS, USS and C# design tokens from packages/design-tokens/tokens.json
	@echo make tokens-check - fail if any token output is missing a token or out of date
	@echo make web      - start the web dev server on localhost:5173
	@echo make web-test - typecheck and Vitest for the web app
	@echo make e2e      - Playwright smoke and screenshots at 360x800 and 1440x900
	@echo make unity-setup - apply Unity project settings and generated assets
	@echo make unity-test  - Unity EditMode tests, headless, results in apps/mobile/TestResults
	@echo make android     - IL2CPP ARM64 development APK in apps/mobile/Build/Android
	@echo make device-check - install the APK on the connected phone and print the rules self-check
	@echo make errors-up    - start self-hosted GlitchTip error reporting on localhost:8000
	@echo make errors-setup - create the GlitchTip admin, org and projects and print the DSNs
	@echo make errors-down  - stop GlitchTip, data kept

build: ## Build the whole solution
	dotnet build $(SLN)

test: tokens-check web-test ## design tokens, web, then dotnet test for backend + shared
	node --test $(TOKENS)
	dotnet test --solution $(SLN)

rules: ## Build shared libraries in Release and copy them to apps/mobile/Assets/GameVerse/Plugins/Shared/
	$(foreach p,$(SHARED_LIBS),dotnet build $(p) -c Release -p:CopyToUnity=true &&) echo Shared libraries delivered to Unity.

up: ## Start the local stack (migrations run automatically before api and worker start)
	$(REQUIRE_ENV)
	docker compose up -d --build --wait

down: ## Stop the local stack
	docker compose down

errors-up: ## Start self-hosted GlitchTip (error reporting, ADR-0005) on localhost:8000
	docker compose --profile errors up -d --wait glitchtip

errors-down: ## Stop GlitchTip (data kept)
	docker compose --profile errors stop glitchtip glitchtip-postgres

errors-setup: ## Create the GlitchTip admin, org and api/web/unity projects; print the DSNs for .env
	node infrastructure/scripts/glitchtip-setup.mjs

logs: ## Follow api and worker logs
	docker compose logs -f api worker

migrate: ## Apply migrations (the same one-shot container `make up` runs)
	$(REQUIRE_ENV)
	docker compose run --rm --build migrate

schema: ## Export OpenAPI at build time (no database needed)
	dotnet build $(API) -p:OpenApiGenerateDocuments=true
	pnpm --filter @gameverse/api-schema generate
	@echo Wrote packages/api-schema/openapi.json and schema.d.ts

tokens: ## Regenerate design token outputs (web, admin, Unity USS and C#)
	node $(TOKENS)/build.mjs

tokens-check: ## Fail if a token output is missing a token or is stale
	node $(TOKENS)/build.mjs --check

install: ## Install the pnpm workspace
	pnpm install

web: ## Web dev server
	pnpm --filter @gameverse/web dev

web-test: ## Typecheck and unit tests for the web app
	pnpm --filter @gameverse/web typecheck
	pnpm --filter @gameverse/web test

e2e: ## Playwright smoke + screenshots (first run: pnpm --filter @gameverse/web exec playwright install chromium)
	pnpm --filter @gameverse/web e2e

unity-setup: rules ## Apply Unity project settings (ProjectSetup.Apply)
	node infrastructure/scripts/unity-batch.mjs $(UNITY_PROJECT)/Logs/setup.log "GV_SETUP" "ok" -- "$(UNITY)" -batchmode -nographics -quit -projectPath $(UNITY_PROJECT) -executeMethod GameVerse.Editor.ProjectSetup.Apply

unity-test: rules ## Unity EditMode tests (headless)
	node infrastructure/scripts/unity-batch.mjs $(UNITY_PROJECT)/Logs/editmode.log "Test run completed" "Exiting with code 0" -- "$(UNITY)" -batchmode -nographics -projectPath $(UNITY_PROJECT) -runTests -testPlatform EditMode -testResults TestResults/editmode.xml

android: rules ## IL2CPP Android development build
	node infrastructure/scripts/unity-batch.mjs $(UNITY_PROJECT)/Logs/android.log "GV_BUILD android" "result=Succeeded" -- "$(UNITY)" -batchmode -nographics -quit -projectPath $(UNITY_PROJECT) -buildTarget Android -executeMethod GameVerse.Editor.BuildScript.BuildAndroid

device-check: ## Install the APK on the USB-connected phone, launch it and print the GV_SELFCHECK line
	node infrastructure/scripts/device-check.mjs "$(ADB)" $(UNITY_PROJECT)/Build/Android/GameVerse.apk
