$ErrorActionPreference='Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not (Test-Path -LiteralPath (Join-Path $projectRoot 'project.json'))) { throw 'Missing independent project marker' }
$env:DOTNET_CLI_HOME = Join-Path $projectRoot 'runs\dotnet-home'
$env:NUGET_PACKAGES = Join-Path $projectRoot 'runs\nuget\packages'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $projectRoot 'runs\nuget\http-cache'
$env:TEMP = Join-Path $projectRoot 'runs\temp'
$env:TMP = $env:TEMP
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = 'true'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
$env:DOTNET_CLI_UI_LANGUAGE = 'en-us'
$env:DOTNET_CLI_FORCE_UTF8_ENCODING = 'true'
$env:DOTNET_NOLOGO = 'true'
$env:DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER = '1'
dotnet build (Join-Path $PSScriptRoot 'src\Workbench.csproj') --disable-build-servers --configfile (Join-Path $PSScriptRoot 'NuGet.Config') -o (Join-Path $projectRoot 'runs\v5-build') 2>&1 | Tee-Object -FilePath (Join-Path $projectRoot 'runs\v5-build.log')
if ($LASTEXITCODE -ne 0) { throw 'Build failed. See runs/v5-build.log' }
