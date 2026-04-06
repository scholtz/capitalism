param(
    [ValidateSet('add', 'remove', 'update', 'list', 'script', 'pending')]
    [string]$Command = 'list',
    [string]$Name,
    [string]$Migration = '',
    [string]$Output = 'Api\Data\Migrations\migration.sql',
    [string]$Project = 'Api\Api.csproj',
    [string]$StartupProject = 'Api\Api.csproj',
    [string]$Context = 'AppDbContext',
    [string]$OutputDir = 'Data\Migrations',
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'

$solutionRoot = Split-Path -Parent $PSCommandPath
Push-Location $solutionRoot

try {
    $baseArgs = @(
        '--project', $Project,
        '--startup-project', $StartupProject,
        '--context', $Context,
        '--configuration', $Configuration
    )

    switch ($Command) {
        'add' {
            if ([string]::IsNullOrWhiteSpace($Name)) {
                throw 'Specify -Name when using -Command add.'
            }

            & dotnet ef migrations add $Name @baseArgs --output-dir $OutputDir
        }
        'remove' {
            & dotnet ef migrations remove @baseArgs
        }
        'update' {
            if ([string]::IsNullOrWhiteSpace($Migration)) {
                & dotnet ef database update @baseArgs
            }
            else {
                & dotnet ef database update $Migration @baseArgs
            }
        }
        'list' {
            & dotnet ef migrations list @baseArgs
        }
        'script' {
            if ([string]::IsNullOrWhiteSpace($Migration)) {
                & dotnet ef migrations script @baseArgs --output $Output
            }
            else {
                & dotnet ef migrations script $Migration @baseArgs --output $Output
            }
        }
        'pending' {
            & dotnet ef migrations has-pending-model-changes @baseArgs
        }
        default {
            throw "Unsupported command '$Command'."
        }
    }
}
finally {
    Pop-Location
}
