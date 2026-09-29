# Runs the repository's build, test, and code-convention checks. Exits non-zero on the first failure.
# -Full cleans every C# file with ReSharper; by default only the files that differ from HEAD are cleaned.
param([switch] $Full)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root 'Evertorch.sln'
. (Join-Path $PSScriptRoot 'DevDatabase.ps1')

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

# The C# files that differ from HEAD, changed or new, or $null when the whole tree must be cleaned: nothing changed
# (a verify after a commit), a setting that steers the cleanup changed, or too many files for one command line.
function Get-ChangedSourceFiles {
    $settings = git -C $root diff --name-only HEAD -- '.editorconfig' 'Evertorch.sln.DotSettings' `
        '.config/dotnet-tools.json' 'Directory.Build.props'
    if ($settings) {
        return $null
    }

    $files = @(git -C $root diff --name-only --diff-filter=d HEAD -- '*.cs') +
        @(git -C $root ls-files --others --exclude-standard -- '*.cs')
    $files = @($files | Where-Object { $_ } | Sort-Object -Unique)
    if ($files.Count -eq 0 -or $files.Count -gt 200) {
        return $null
    }

    return , $files
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
    # The database tests start PostgreSQL 18 with Testcontainers and are never skipped (Persistence section 11).
    Write-Host '==> Docker engine'
    if (-not (Test-DockerEngine)) {
        Write-Host 'FAILED: Docker engine. The database tests need Docker running; start Docker Desktop and run again.'
        exit 1
    }

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
    # Cleaning only the changed files saves about five minutes a run; a file nobody touched can still drift when
    # another file changes (a using left unused after a type moves), which a -Full run catches.
    $changedFiles = if ($Full) { $null } else { Get-ChangedSourceFiles }
    $clientPrefix = 'Evertorch.Client/'
    $clientFolders = @('Assets/_Project/', 'Assets/_ProjectScripts.Evertorch.Client/')
    if ($null -eq $changedFiles) {
        Write-Host '==> ReSharper cleanup scope: every C# file'
        $serverInclude = '**/*.cs'
        $clientInclude = ($clientFolders | ForEach-Object { "$_**/*.cs" }) -join ';'
    }
    else {
        Write-Host "==> ReSharper cleanup scope: C# files that differ from HEAD ($($changedFiles.Count))"
        $serverInclude = ($changedFiles | Where-Object { -not $_.StartsWith($clientPrefix) }) -join ';'
        $clientInclude = ($changedFiles | Where-Object { $_.StartsWith($clientPrefix) } |
            ForEach-Object { $_.Substring($clientPrefix.Length) } |
            Where-Object { $path = $_; $clientFolders | Where-Object { $path.StartsWith($_) } }) -join ';'
    }

    $before = Get-SourceHashes
    # An empty --include would clean every file, so a side with no changed file is skipped.
    if ($serverInclude) {
        Invoke-Step 'ReSharper cleanup' {
            dotnet jb cleanupcode $solution '--profile=Built-in: Full Cleanup' "--include=$serverInclude" --no-build
        }
    }
    else {
        Write-Host '==> ReSharper cleanup: no changed file'
    }

    # Client code outside the folders linked into Evertorch.sln is reached through the solution Unity generates,
    # which exists only once Unity has opened the project. --no-build is required: building the Unity projects
    # fails, and the tool then exits without cleaning anything.
    $clientSolution = Join-Path $root 'Evertorch.Client/Evertorch.Client.sln'
    $isClientChecked = Test-Path $clientSolution
    if ($isClientChecked -and -not $clientInclude) {
        Write-Host '==> ReSharper cleanup (client): no changed file'
    }
    elseif ($isClientChecked) {
        Invoke-Step 'ReSharper cleanup (client)' {
            dotnet jb cleanupcode $clientSolution '--profile=Built-in: Full Cleanup' "--include=$clientInclude" --no-build
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
