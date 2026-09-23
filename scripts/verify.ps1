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
    # Results go to trx files so that a failure names its tests even when the console output is not kept.
    $testResults = Join-Path $root 'artifacts/test-results'
    if (Test-Path $testResults) {
        Remove-Item -Recurse -Force $testResults
    }

    Write-Host '==> Test'
    dotnet test $solution -c Release --no-build --logger trx --results-directory $testResults
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAILED: Test (exit code $LASTEXITCODE). Tests that did not pass:"
        foreach ($file in Get-ChildItem -Path $testResults -Filter '*.trx' -ErrorAction SilentlyContinue) {
            [xml] $trx = Get-Content -Raw -LiteralPath $file.FullName
            $classes = @{}
            foreach ($definition in $trx.TestRun.TestDefinitions.UnitTest) {
                $classes[$definition.id] = $definition.TestMethod.className
            }

            foreach ($result in $trx.TestRun.Results.UnitTestResult) {
                if ($result.outcome -ne 'Passed' -and $result.outcome -ne 'NotExecuted') {
                    Write-Host "  $($result.outcome): $($classes[$result.testId]).$($result.testName)"
                }
            }
        }
        Write-Host "Results: $testResults"
        exit 1
    }

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

    # ReSharper cleanup has no verify-only mode, so drift is detected by comparing file hashes. The profile is the
    # one Rider runs, so code cleaned in the IDE passes unchanged.
    $before = Get-SourceHashes
    Invoke-Step 'ReSharper cleanup' {
        dotnet jb cleanupcode $solution '--profile=Built-in: Full Cleanup' '--include=**/*.cs' --no-build
    }

    # Client code outside the folders linked into Evertorch.sln is reached through the solution Unity generates,
    # which exists only once Unity has opened the project. --no-build is required: building the Unity projects
    # fails, and the tool then exits without cleaning anything.
    $clientSolution = Join-Path $root 'Evertorch.Client/Evertorch.Client.sln'
    $isClientChecked = Test-Path $clientSolution
    if ($isClientChecked) {
        Invoke-Step 'ReSharper cleanup (client)' {
            dotnet jb cleanupcode $clientSolution '--profile=Built-in: Full Cleanup' `
                '--include=Assets/_Project/**/*.cs;Assets/_ProjectScripts.Evertorch.Client/**/*.cs' --no-build
        }
    }
    else {
        Write-Host '==> ReSharper cleanup (client): SKIPPED'
        Write-Host "    $clientSolution does not exist. Open Evertorch.Client in Unity once so it generates the"
        Write-Host '    solution, then run this script again.'
    }

    $after = Get-SourceHashes

    $changed = @($before.Keys | Where-Object { $before[$_] -ne $after[$_] } | Sort-Object)
    if ($changed.Count -gt 0) {
        Write-Host 'FAILED: ReSharper cleanup changed these files (now cleaned up in place):'
        $changed | ForEach-Object { Write-Host "  $_" }
        exit 1
    }

    if ($isClientChecked) {
        Write-Host 'All checks passed.'
    }
    else {
        Write-Host 'All checks passed except the client cleanup, which was skipped (see above).'
    }
}
finally {
    Pop-Location
}
