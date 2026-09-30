# Makes the development web build in artifacts/client/Web with the Unity editor closed (build-web.cmd calls this):
#   1. refreshes both content packages, which the build carries;
#   2. runs WebBuild.BuildFromCommandLine in batch mode, logging to artifacts/client/web-build.log;
#   3. restores the two settings files the build rewrites, unless they were already changed before it.
# -Unity overrides the editor found from ProjectVersion.txt.
param([string] $Unity)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'Evertorch.Client'
$output = Join-Path $root 'artifacts/client/Web'
$log = Join-Path $root 'artifacts/client/web-build.log'
$churn = @('Evertorch.Client/Assets/Settings/Mobile_RPAsset.asset', 'Evertorch.Client/ProjectSettings/ProjectSettings.asset')

# The editor holds its lock file open, and batch mode cannot open a project the editor has open.
function Test-EditorOpen {
    $lock = Join-Path $project 'Temp/UnityLockfile'
    if (-not (Test-Path $lock)) {
        return $false
    }

    try {
        [IO.File]::Open($lock, 'Open', 'ReadWrite', 'None').Dispose()
        return $false
    }
    catch [IO.IOException] {
        return $true
    }
}

Push-Location $root
try {
    if (-not $Unity) {
        $version = (Select-String -Path (Join-Path $project 'ProjectSettings/ProjectVersion.txt') -Pattern '^m_EditorVersion: (.+)$').Matches[0].Groups[1].Value
        $Unity = Join-Path $env:ProgramFiles "Unity\Hub\Editor\$version\Editor\Unity.exe"
    }

    if (-not (Test-Path $Unity)) {
        Write-Host "Unity was not found at $Unity. Install that version with Unity Hub, or pass -Unity <path to Unity.exe>."
        exit 1
    }

    if (Test-EditorOpen) {
        Write-Host 'The Unity editor has the project open. Close it, or use Evertorch > Build Web in the editor.'
        exit 1
    }

    dotnet run --project Evertorch.Tools -- content build --client-out Evertorch.Client/Assets/StreamingAssets/GameData
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'The content build failed; the web build was not started.'
        exit 1
    }

    $clean = @($churn | Where-Object { git diff --quiet -- $_; $LASTEXITCODE -eq 0 })

    Write-Host "Building the web client; this takes several minutes. Log: $log"
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $log) | Out-Null
    $started = Get-Date
    $unityArguments = @('-batchmode', '-projectPath', "`"$project`"", '-executeMethod',
        'Evertorch.Client.Editor.WebBuild.BuildFromCommandLine', '-logFile', "`"$log`"")
    $process = Start-Process -FilePath $Unity -ArgumentList $unityArguments -PassThru
    # Reading the handle keeps it, so ExitCode is still known after the process ends.
    $null = $process.Handle
    $process.WaitForExit()
    $minutes = [math]::Round(((Get-Date) - $started).TotalMinutes, 1)

    foreach ($file in $clean) {
        git restore -- $file
    }

    if ($process.ExitCode -ne 0) {
        Write-Host "The web build failed after $minutes minutes (Unity exit code $($process.ExitCode)). The log ends:"
        if (Test-Path $log) {
            Get-Content $log -Tail 20
        }

        exit 1
    }

    $megabytes = [math]::Round((Get-ChildItem $output -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 1)
    Write-Host "Web build done in $minutes minutes: $megabytes MB in $output."
    Write-Host 'Serve it with scripts\serve-web.cmd.'
}
finally {
    Pop-Location
}
