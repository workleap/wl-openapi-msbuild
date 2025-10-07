# Workleap.OpenApi.MSBuild

This is a .NET MSBuild task library that validates at build time that OpenAPI specification files extracted from ASP.NET Core Web APIs conform to Workleap API guidelines. It's distributed as a NuGet package that integrates into the build process of consuming projects.

**Always reference these instructions first and fallback to search or bash commands only when you encounter unexpected information that does not match the information here.**

## Working Effectively

### Prerequisites and Installation
- Install .NET 9.0.305 SDK (as specified in global.json): `dotnet-install.ps1 -Version 9.0.305`
- Install .NET 8.0.x runtime for consuming projects
- Verify installation: `dotnet --version` should show 9.0.305

### Build Commands
**CRITICAL TIMING INFORMATION:**
- **NEVER CANCEL** builds or tests - wait for completion

Working directory for all operations: `cd /path/to/repo/src`

**Debug Build Process:**
```powershell
# Individual commands with timing
dotnet clean -c Debug
dotnet restore
dotnet build -c Debug --no-restore
dotnet test -c Debug --no-build --verbosity normal
```

**Release Build Process:**
```powershell
dotnet clean -c Release
dotnet build -c Release --no-restore
dotnet test -c Release --no-build --verbosity normal
```

### Code Quality and Formatting
- The project uses Workleap.DotNet.CodingStandards for code standards enforcement
- .editorconfig enforces comprehensive analyzer rules
- Release configuration treats warnings as errors
- **ALWAYS run `dotnet build -c Release` before committing** to catch warnings that would fail CI

### Running Tests
- Test project: `Workleap.OpenApi.MSBuild.Tests` (targets .NET 8.0)
- System test projects: `WebApi.MsBuild.SystemTest.*` (real Web API projects to test the MSBuild task)
- Command: `dotnet test -c Debug --no-build --verbosity normal`
- Tests validate Spectral rules, OasDiff comparisons, and MSBuild task integration

### PowerShell Build Script (Production)
- For comprehensive builds: `.\Build.ps1`.
- Script runs: clean, build, test, pack, and optional NuGet push.
- This script is meant to be used locally and in CI/CD pipelines.  The built package will only be published if the NUGET_SOURCE and NUGET_API_KEY env variables are set.

## Validation

- ALWAYS manually test MSBuild task scenarios when making core changes to `ValidateOpenApiTask` or process implementations
- ALWAYS run through complete build and test cycle: clean, restore, build, test
- ALWAYS verify no new warnings are introduced (use Release configuration to catch them)
- ALWAYS test with the `WebApiDebugger` project using launch profiles to debug MSBuild task execution
- You can build and test the solution locally in both Debug and Release configurations

## Common Tasks

### Key Projects Structure
```
src/
├── Workleap.OpenApi.MSBuild/                      # Main MSBuild task library (netstandard2.0, NuGet package)
│   ├── ValidateOpenApiTask.cs                     # Main MSBuild task entry point
│   ├── GenerateContractProcess.cs                 # GenerateContract mode implementation
│   ├── ValidateContractProcess.cs                 # ValidateContract mode implementation
│   ├── SwaggerManager.cs                          # SwashbuckleCLI wrapper for spec generation
│   ├── OasdiffManager.cs                          # OasDiff wrapper for spec comparison
│   ├── CancelableAsyncTask.cs                     # Base class for async MSBuild tasks
│   ├── ProcessWrapper.cs                          # Process execution wrapper
│   ├── HttpClientWrapper.cs                       # HTTP client wrapper
│   ├── LoggerWrapper.cs                           # MSBuild logger wrapper
│   ├── msbuild/                                   # MSBuild integration files
│   │   ├── tools/Workleap.OpenApi.MSBuild.targets # MSBuild targets and task registration
│   │   ├── build/                                 # Build-time imports
│   │   └── buildMultiTargeting/                   # Multi-targeting support
│   └── Spectral/                                  # Spectral integration
│       ├── SpectralInstaller.cs                   # Installs Spectral NPM package
│       ├── SpectralRulesetManager.cs              # Manages Workleap Spectral rulesets
│       ├── SpectralRunner.cs                      # Runs Spectral validation
│       ├── DiffCalculator.cs                      # Calculates diff for CI reporting
│       └── CiReportRenderer.cs                    # Renders CI-friendly reports
│
├── WebApiDebugger/                                # Debugging project for MSBuild task
│   ├── Program.cs                                 # Sample ASP.NET Core Web API
│   ├── openapi-v1.yaml                            # Sample OpenAPI spec file
│   ├── custom.spectral.yaml                       # Sample custom Spectral ruleset
│   └── Properties/launchSettings.json             # Preconfigured debug profiles
│
└── tests/
    ├── Workleap.OpenApi.MSBuild.Tests/            # Unit tests for MSBuild task (net8.0)
    ├── WebApi.MsBuild.SystemTest.GenericTest/     # System test: successful validation
    ├── WebApi.MsBuild.SystemTest.OasDiffError/    # System test: OasDiff breaking changes
    └── WebApi.MsBuild.SystemTest.SpectralError/   # System test: Spectral validation errors
```

### Working with MSBuild Task
- Core entry point: `ValidateOpenApiTask.ExecuteAsync()` is called after the referencing project is built
- Two development modes:
  - **GenerateContract** (Code-First): Generate OpenAPI spec from code, validate with Spectral, optionally compare with existing spec
  - **ValidateContract** (Contract-First): Validate provided spec with Spectral, optionally compare code against spec
- Service profiles:
  - **backend** (default): Uses [Workleap backend Spectral rules](https://github.com/workleap/wl-api-guidelines/blob/main/.spectral.backend.yaml)
  - **frontend**: Uses [Workleap frontend Spectral rules](https://github.com/workleap/wl-api-guidelines/blob/main/.spectral.frontend.yaml)
- MSBuild task is executed only if compiled outputs are newer than last execution (uses touch file strategy)

### Key MSBuild Properties
- `OpenApiEnabled` (default: `true`): Enable/disable OpenAPI validation
- `OpenApiDevelopmentMode` (default: `GenerateContract`): Set to `ValidateContract` or `GenerateContract`
- `OpenApiServiceProfile` (default: `backend`): Set to `backend` or `frontend`
- `OpenApiCompareCodeAgainstSpecFile` (default: context-dependent): Compare code against provided spec
- `OpenApiSwaggerDocumentNames` (default: `v1`): Semicolon-separated list of Swagger document names
- `OpenApiSpecificationFiles` (default: `openapi-{documentname}.yaml`): Paths to OpenAPI spec files
- `OpenApiSpectralRulesetUrl` (optional): Custom Spectral ruleset URL or file path
- `OpenApiTreatWarningsAsErrors` (default: inherits `TreatWarningsAsErrors`): Treat warnings as errors
- `OpenApiDebuggingEnabled` (default: `false`): Use task DLL from bin folder for debugging

### External Tools Used
1. **SwashbuckleCLI**: Generates OpenAPI spec from ASP.NET Core Web API
   - Installed as .NET tool in `$(OpenApiToolsDirectoryPath)`
   - Runs the built Web API in a separate process to extract spec
2. **Spectral**: Validates OpenAPI spec against Workleap API guidelines
   - Installed as NPM package in `$(OpenApiToolsDirectoryPath)`
   - Uses Workleap Spectral rulesets from [wl-api-guidelines](https://github.com/workleap/wl-api-guidelines)
3. **OasDiff**: Compares two OpenAPI specs for breaking changes
   - Downloaded as standalone binary to `$(OpenApiToolsDirectoryPath)`
   - Detects breaking changes between provided spec and generated spec

### Key Files to Check After Changes
- MSBuild task entry point: `src/Workleap.OpenApi.MSBuild/ValidateOpenApiTask.cs`
- MSBuild targets and properties: `src/Workleap.OpenApi.MSBuild/msbuild/tools/Workleap.OpenApi.MSBuild.targets`
- Process implementations: `GenerateContractProcess.cs` and `ValidateContractProcess.cs`
- Tool wrappers: `SwaggerManager.cs`, `OasdiffManager.cs`, `SpectralRunner.cs`
- Debug launch profiles: `src/WebApiDebugger/Properties/launchSettings.json`
- System test projects: `src/tests/WebApi.MsBuild.SystemTest.*`

### Testing Scenarios
When making changes, always validate:
1. **GenerateContract Mode**: Generate spec from code, validate with Spectral
2. **ValidateContract Mode**: Validate provided spec with Spectral, compare code against spec
3. **Custom Spectral Ruleset**: Test with custom ruleset URL or file path
4. **Backend vs Frontend Profiles**: Test with both service profiles
5. **Breaking Change Detection**: Verify OasDiff detects breaking changes correctly
6. **Multi-Document Support**: Test with multiple Swagger document names (semicolon-separated)
7. **Debug Mode**: Test with `OpenApiDebuggingEnabled=true` in WebApiDebugger project

### Common Issues and Solutions
- **MSBuild task not executing**: Check `OpenApiEnabled=true` and build outputs are newer than touch file
- **Tool installation failures**: Check network connectivity and NuGet/NPM access
- **SwashbuckleCLI failures**: Check ASP.NET Core Web API starts correctly and exposes Swagger endpoints
- **Spectral validation errors**: Review Workleap API guidelines and fix spec violations
- **OasDiff breaking changes**: Review spec changes and fix breaking changes or update spec file
- **Debug breakpoints not hitting**: Set `OpenApiDebuggingEnabled=true` in consuming project
- **Test failures**: Ensure .NET 8.0 SDK is installed for test projects
- **Build warnings**: Run Release configuration to catch warnings that CI will fail on

### Repository-Specific Notes
- Repository uses semantic versioning and automated NuGet publishing to nuget.org
- Preview packages published to Azure Artifacts `gsoftdev` feed on PRs and main branch
- Stable releases published to nuget.org when tagged with format `x.y.z`
- Uses GitVersion for version calculation in Release builds
- MSBuild task targets netstandard2.0 for broad compatibility
- NuGet package structure: task DLL and dependencies in `tools/task`, MSBuild targets in `tools`
- Tools are installed per-project in `$(OutputPath)/openapi` directory
- Tools use isolated NuGet source (public nuget.org only) to avoid private feed dependencies

## Project Structure

### Solution Layout
- **Workleap.OpenApi.MSBuild** - Main MSBuild task library (netstandard2.0)
  - NuGet package with MSBuild task integration
  - Targets: `lib/netstandard2.0` (empty placeholder), `tools/task` (task DLL), `tools` (MSBuild targets)
  - Dependencies: CliWrap (process execution), YamlDotNet (YAML parsing), Microsoft.Build.Utilities.Core (MSBuild task base)
  
- **WebApiDebugger** - Debugging project for MSBuild task
  - ASP.NET Core Web API with preconfigured launch profiles
  - Sample OpenAPI spec files and custom Spectral ruleset
  - References Workleap.OpenApi.MSBuild to trigger task execution
  
- **Workleap.OpenApi.MSBuild.Tests** - Unit tests (net8.0)
  - Tests for Spectral, OasDiff, and MSBuild task logic
  - Uses Meziantou.Framework for temporary directories and full path helpers
  
- **WebApi.MsBuild.SystemTest.*** - System test projects
  - Real ASP.NET Core Web APIs to test MSBuild task integration
  - Validates success scenarios and error scenarios (Spectral errors, OasDiff errors)

### Key Files and Locations
- Main task entry point: `src/Workleap.OpenApi.MSBuild/ValidateOpenApiTask.cs`
- MSBuild targets: `src/Workleap.OpenApi.MSBuild/msbuild/tools/Workleap.OpenApi.MSBuild.targets`
- Process implementations: `src/Workleap.OpenApi.MSBuild/GenerateContractProcess.cs` and `ValidateContractProcess.cs`
- Tool wrappers: `src/Workleap.OpenApi.MSBuild/SwaggerManager.cs`, `OasdiffManager.cs`, `Spectral/SpectralRunner.cs`
- Debug profiles: `src/WebApiDebugger/Properties/launchSettings.json`
- Build script: `Build.ps1`

## Common Development Tasks

### Adding New Features
1. Start by understanding the MSBuild task flow: `ValidateOpenApiTask` → `GenerateContractProcess`/`ValidateContractProcess` → Tool managers
2. Check existing extension points: MSBuild properties in `.targets` file, tool wrappers
3. Add tests first in `Workleap.OpenApi.MSBuild.Tests` project
4. Follow existing patterns for process execution in `ProcessWrapper.cs`
5. Test with `WebApiDebugger` project using launch profiles

### Debugging MSBuild Task Issues
1. Use `WebApiDebugger` project with preconfigured launch profiles
2. Set `OpenApiDebuggingEnabled=true` to use task DLL from bin folder (allows breakpoints)
3. Check MSBuild binary log with `msbuild /t:ValidateOpenApi /bl:msbuild.binlog` and view with [MSBuild Structured Log Viewer](https://msbuildlog.com/)
4. Review MSBuild task inputs and outputs in `.targets` file
5. Check tool installation paths in `$(OutputPath)/openapi` directory
6. Review tool execution logs in MSBuild output (set verbosity to detailed)

### Working with External Tools
- **SwashbuckleCLI**: Uses `SwaggerManager.cs` to install and run tool
  - Starts ASP.NET Core Web API in separate process
  - Extracts OpenAPI spec via Swagger endpoint
  - Supports multiple Swagger document names
- **Spectral**: Uses `SpectralInstaller.cs` and `SpectralRunner.cs`
  - Installs as NPM package using `npm install`
  - Downloads Workleap Spectral rulesets from GitHub
  - Runs validation and outputs JSON report
- **OasDiff**: Uses `OasdiffManager.cs`
  - Downloads standalone binary from GitHub releases
  - Compares two OpenAPI specs for breaking changes
  - Outputs JSON report with breaking change details

### Understanding MSBuild Integration
- MSBuild task is registered in `Workleap.OpenApi.MSBuild.targets` as `UsingTask`
- Task is executed after `Build` target via `AfterTargets="Build"`
- Task uses incremental build: only runs if inputs are newer than outputs
- Inputs: `$(MSBuildAllProjects)`, `@(DocFileItem)`, `@(IntermediateAssembly)`, etc.
- Outputs: Touch file at `$(IntermediateOutputPath)$(OpenApiTouchFileName)`
- Default property values are defined in `ValidateOpenApi` target
- Supports both single-targeting and multi-targeting projects

## Coding Standards
- C# 12 with nullable reference types enabled
- ImplicitUsings enabled for common namespaces
- Comprehensive analyzer rules enforced (see src/.editorconfig)
- Workleap.DotNet.CodingStandards package applied
- Target framework: netstandard2.0 (MSBuild task), net8.0 (tests)

## Package Information
- Targets: netstandard2.0 (main library), net8.0 (tests)
- NuGet package type: MSBuild task with development dependency
- Package structure:
  - `lib/netstandard2.0/_._` - Empty placeholder (no runtime assemblies)
  - `tools/task/**` - Task DLL and dependencies (CliWrap, YamlDotNet, etc.)
  - `tools/Workleap.OpenApi.MSBuild.targets` - MSBuild targets and task registration
  - `build/` - Build-time imports
  - `buildMultiTargeting/` - Multi-targeting support
- Dependencies: CliWrap, YamlDotNet, Microsoft.Build.Utilities.Core
- Test dependencies: xUnit, Meziantou.Framework, Meziantou.Xunit.ParallelTestFramework

## CI/CD Integration

### GitHub Actions Workflows
- **CI Workflow** (`ci.yaml`): Runs on PRs and renovate branches
  - Authenticates to Azure Artifacts `gsoftdev` feed
  - Runs Build.ps1 (clean, build, test, pack, push preview packages)
  - LinearB deployment tracking for development environment
  
- **Publish Workflow** (`publish.yml`): Runs on main branch and tags
  - Retrieves nuget.org API key from Azure Key Vault
  - Runs Build.ps1 (clean, build, test, pack, push to nuget.org)
  - LinearB deployment tracking for release environment

### Versioning and Releases
- Uses GitVersion for semantic versioning in Release builds
- Preview packages (main branch): `x.y.z-preview.N` → published to Azure Artifacts `gsoftdev` feed
- Stable releases (tags): `x.y.z` → published to nuget.org
- Tag format: `x.y.z` (no prefix)
- Continuous delivery mode for feature branches and PRs

### Authentication
- Uses OpenID Connect for Azure authentication
- Service connections configured in GitHub Actions environments
- NuGet API key retrieved from Azure Key Vault for nuget.org publishing

## Architecture Notes

### MSBuild Task Execution Flow
1. **Trigger**: ASP.NET Core Web API project is built
2. **Conditional Check**: MSBuild determines if task should run (incremental build)
3. **Task Initialization**: `ValidateOpenApiTask.ExecuteAsync()` is called
4. **Tool Installation**: SwashbuckleCLI, Spectral, and OasDiff are installed if needed
5. **Mode Selection**: Task branches based on `OpenApiDevelopmentMode`
6. **Process Execution**: `GenerateContractProcess` or `ValidateContractProcess` runs
7. **Spec Generation**: SwashbuckleCLI extracts OpenAPI spec from Web API (if needed)
8. **Spectral Validation**: OpenAPI spec is validated against Workleap API guidelines
9. **OasDiff Comparison**: Specs are compared for breaking changes (if enabled)
10. **Touch File**: Touch file is updated to mark successful execution

### GenerateContract Mode (Code-First)
1. Generate OpenAPI spec from code using SwashbuckleCLI
2. Validate generated spec with Spectral
3. Write/Update spec file to disk
4. Optionally compare generated spec with existing spec file using OasDiff (CI mode)

### ValidateContract Mode (Contract-First)
1. Validate provided spec file with Spectral
2. Optionally generate spec from code and compare with provided spec using OasDiff

### Extension Points
- **Custom Spectral Ruleset**: Set `OpenApiSpectralRulesetUrl` to use custom ruleset
- **Multiple Swagger Documents**: Set `OpenApiSwaggerDocumentNames` to semicolon-separated list
- **Custom Spec File Paths**: Set `OpenApiSpecificationFiles` to custom paths
- **Service Profiles**: Set `OpenApiServiceProfile` to `backend` or `frontend`
- **Development Modes**: Set `OpenApiDevelopmentMode` to `GenerateContract` or `ValidateContract`

### Performance Considerations
- Incremental build: MSBuild task only runs if inputs are newer than outputs
- Tools are installed once per project in `$(OutputPath)/openapi` directory
- Spectral and OasDiff run in separate processes for isolation
- SwashbuckleCLI starts ASP.NET Core Web API in separate process (may be slow)
- Touch file strategy ensures task doesn't re-run unnecessarily

## Security Considerations
- **Isolated NuGet Source**: Tools use public nuget.org only (no private feed dependencies)
- **Process Isolation**: External tools run in separate processes with limited permissions
- **API Key Handling**: NuGet API keys retrieved from Azure Key Vault (not stored in repo)
- **OpenID Connect**: GitHub Actions use OIDC for Azure authentication (no service principal secrets)
- **Spectral Ruleset**: Workleap API guidelines enforce security best practices (e.g., HTTPS only)

## Historical Notes
- Created to standardize OpenAPI spec validation across Workleap ASP.NET Core Web APIs
- Replaces manual Spectral validation with automated MSBuild task
- Supports both Code-First (GenerateContract) and Contract-First (ValidateContract) workflows
- Uses Workleap API guidelines from [wl-api-guidelines](https://github.com/workleap/wl-api-guidelines) repository
- Integrates SwashbuckleCLI, Spectral, and OasDiff for comprehensive validation
- Published to nuget.org for public consumption (stable releases only)
- Preview packages published to Azure Artifacts for internal testing
- Supports both backend and frontend API validation profiles
- MSBuild task structure inspired by Microsoft's official MSBuild task guidance
- Tools are downloaded at build time to avoid including large binaries in NuGet package

## Environment-Specific Notes

### Local Development
- Use `WebApiDebugger` project for debugging MSBuild task
- Set `OpenApiDebuggingEnabled=true` to use task DLL from bin folder
- Tools are installed in `src/WebApiDebugger/bin/Debug/net8.0/openapi`
- Use preconfigured launch profiles for common scenarios
- Binary log viewer recommended for troubleshooting: `msbuild /bl:msbuild.binlog`

### CI/CD Environment
- Preview packages published to Azure Artifacts `gsoftdev` feed
- Stable releases published to nuget.org
- GitVersion calculates version from Git history
- Build script handles authentication with Azure Artifacts and nuget.org
- LinearB deployment tracking enabled for development and release environments

### Consuming Projects
- Reference `Workleap.OpenApi.MSBuild` NuGet package
- Configure MSBuild properties in `.csproj` or `Directory.Build.props`
- MSBuild task runs automatically after build
- Tools are installed per-project in `$(OutputPath)/openapi`
- OpenAPI spec files typically stored in project root (e.g., `openapi-v1.yaml`)
- Use `OpenApiEnabled=false` to disable validation temporarily
