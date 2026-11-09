# GameVerse developer commands. Works from Git Bash and PowerShell (GNU make).
# Recipes only call dotnet, so there are no shell-specific commands (cp, copy, rm) to diverge.

SLN := backend/GameVerse.slnx
SHARED_LIBS := shared/GameVerse.Rules/GameVerse.Rules.csproj shared/GameVerse.Contracts/GameVerse.Contracts.csproj

.DEFAULT_GOAL := help
.PHONY: help test rules build

help: ## List targets
	@echo "make build   - build the solution (backend + shared)"
	@echo "make test    - run all .NET tests"
	@echo "make rules   - build GameVerse.Rules + Contracts (Release) and copy DLLs into Unity"

build: ## Build the whole solution
	dotnet build $(SLN)

test: ## dotnet test for backend + shared
	dotnet test $(SLN)

rules: ## Build shared libraries in Release and copy them to apps/mobile/Assets/GameVerse/Plugins/Shared/
	$(foreach p,$(SHARED_LIBS),dotnet build $(p) -c Release -p:CopyToUnity=true &&) echo Shared libraries delivered to Unity.
