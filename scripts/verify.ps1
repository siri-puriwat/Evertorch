# Runs the repository's build, test, and code-convention checks. Exits non-zero on the first failure.
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root 'Evertorch.sln'

function Invoke-Step([string] $name, [scriptblock] $action) {
    Write-Host "==> $name"
    & $action
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAILED: $name (exit code $LASTEXITCODE)"
        exit 1
    }
}

function Get-SourceHashes {
    $files = git -C $root ls-files --cached --others --exclude-standard -- '*.cs'
    $hashes = @{}
    foreach ($file in $files) {
        $path = Join-Path $root $file
        if (Test-Path $path) {
            $hashes[$file] = (Get-FileHash -Algorithm SHA256 -Path $path).Hash
        }
    }
    return $hashes
}

function Get-ContentPackageHashes([string] $directory) {
    $hashes = @{}
    foreach ($file in Get-ChildItem -Path $directory -Recurse -File) {
        $relative = $file.FullName.Substring($directory.Length)
        $hashes[$relative] = (Get-FileHash -Algorithm SHA256 -Path $file.FullName).Hash
    }
    return $hashes
}

Push-Location $root
try {
    Invoke-Step 'Restore tools' { dotnet tool restore }
    Invoke-Step 'Restore packages' { dotnet restore $solution }
    Invoke-Step 'Build' { dotnet build $solution -c Release --no-restore }
    Invoke-Step 'Test' { dotnet test $solution -c Release --no-build }

    # Canonical content must validate, and building it twice must give byte-identical packages.
    $tools = Join-Path $root 'artifacts/bin/Evertorch.Tools/release/Evertorch.Tools.dll'
    $contentOutput = Join-Path $root 'artifacts/content'
    Invoke-Step 'Content build' { dotnet $tools content build }
    $firstBuild = Get-ContentPackageHashes $contentOutput
    Invoke-Step 'Content rebuild' { dotnet $tools content build }
    $secondBuild = Get-ContentPackageHashes $contentOutput

    $drifted = @($firstBuild.Keys + $secondBuild.Keys | Sort-Object -Unique |
        Where-Object { $firstBuild[$_] -ne $secondBuild[$_] })
    if ($firstBuild.Count -eq 0 -or $drifted.Count -gt 0) {
        Write-Host 'FAILED: content packages are missing or differ between two builds of the same input:'
        $drifted | ForEach-Object { Write-Host "  $_" }
        exit 1
    }

    Invoke-Step 'Code style' { dotnet format style $solution --no-restore --verify-no-changes }
    Invoke-Step 'Analyzers' { dotnet format analyzers $solution --no-restore --verify-no-changes }

    # ReSharper cleanup has no verify-only mode, so formatting drift is detected by comparing file hashes.
    $before = Get-SourceHashes
    Invoke-Step 'ReSharper reformat' {
        dotnet jb cleanupcode $solution '--profile=Built-in: Reformat Code' '--include=**/*.cs' --no-build
    }
    $after = Get-SourceHashes

    $changed = @($before.Keys | Where-Object { $before[$_] -ne $after[$_] } | Sort-Object)
    if ($changed.Count -gt 0) {
        Write-Host 'FAILED: ReSharper reformat changed these files (now reformatted in place):'
        $changed | ForEach-Object { Write-Host "  $_" }
        exit 1
    }

    Write-Host 'All checks passed.'
}
finally {
    Pop-Location
}
