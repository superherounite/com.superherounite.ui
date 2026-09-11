#requires -Version 7.0
<#
.SYNOPSIS
Creates an isolated test host and runs the complete Editor suite or an opt-in benchmark.
.EXAMPLE
pwsh -File Tools~/Validate-Package.ps1 -EditorPath 'C:/Program Files/Unity/Hub/Editor/6000.0.68f1/Editor/Unity.exe'
.EXAMPLE
pwsh -File Tools~/Validate-Package.ps1 -EditorPath $env:UNITY_EDITOR_PATH -Mode Scale -OwnerCount 1000
.EXAMPLE
pwsh -File Tools~/Validate-Package.ps1 -EditorPath $env:UNITY_EDITOR_PATH -EnableGraphics
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$EditorPath,
    [ValidateSet('Tests', 'Scale', 'Comparison')][string]$Mode = 'Tests',
    [ValidateSet(0, 100, 200, 300, 1000)][int]$OwnerCount = 0,
    [string]$ProjectDirectory,
    [ValidateRange(60, 14400)][int]$TimeoutSeconds = 1800,
    [switch]$EnableGraphics,
    [switch]$PrepareOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$packageRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$templateRoot = Join-Path $packageRoot 'TestProject~'
$editorExecutable = (Resolve-Path -LiteralPath $EditorPath).Path
$baselineRevision = '7ede1ed6229c7b03b4d3adfddc42a283d6ef63b0'
$utf8 = [Text.UTF8Encoding]::new($false)

function Write-JsonFile([string]$Path, $Value) {
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 12), $utf8)
}

function Assert-NoEditor([string]$ProjectPath) {
    if ($IsWindows) {
        foreach ($processInfo in Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'") {
            if ($processInfo.CommandLine -match '-projectPath\s+(?:"([^"]+)"|(\S+))') {
                $openPath = [IO.Path]::GetFullPath($Matches[1] + $Matches[2]).TrimEnd('\', '/')
                if ([string]::Equals($openPath, $ProjectPath, [StringComparison]::OrdinalIgnoreCase)) {
                    throw "Unity already has this validation project open (PID $($processInfo.ProcessId))."
                }
            }
        }
    }

    $lockPath = Join-Path $ProjectPath 'Temp/UnityLockfile'
    if (Test-Path -LiteralPath $lockPath) {
        try {
            $lockHandle = [IO.File]::Open($lockPath, 'Open', 'ReadWrite', 'None')
            $lockHandle.Dispose()
        }
        catch {
            throw "Unity holds the project lock: $ProjectPath"
        }
    }
}

function Copy-TemplateTree([string]$SourcePath, [string]$DestinationPath) {
    [IO.Directory]::CreateDirectory($DestinationPath) | Out-Null
    foreach ($sourceFile in Get-ChildItem -LiteralPath $SourcePath -File -Recurse) {
        $relative = [IO.Path]::GetRelativePath($SourcePath, $sourceFile.FullName)
        $destination = Join-Path $DestinationPath $relative
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
        [IO.File]::Copy($sourceFile.FullName, $destination, $true)
        $managedFiles.Add([IO.Path]::GetRelativePath($projectPath, $destination).Replace('\', '/')) | Out-Null
    }
}

function Remove-ObsoleteTemplateFiles {
    foreach ($relative in $previousFiles) {
        if ($managedFiles.Contains($relative)) { continue }
        $oldPath = [IO.Path]::GetFullPath((Join-Path $projectPath $relative))
        $insideProject = $oldPath.StartsWith($projectPath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
        $assetPrefix = (Join-Path $projectPath 'Assets') + [IO.Path]::DirectorySeparatorChar
        $settingsPrefix = (Join-Path $projectPath 'ProjectSettings') + [IO.Path]::DirectorySeparatorChar
        $insideManagedTree = $oldPath.StartsWith($assetPrefix, [StringComparison]::OrdinalIgnoreCase) -or
            $oldPath.StartsWith($settingsPrefix, [StringComparison]::OrdinalIgnoreCase)
        if (!$insideProject -or !$insideManagedTree) {
            throw "Invalid managed template path: $relative"
        }
        foreach ($candidate in @($oldPath, "$oldPath.meta")) {
            $candidateRelative = [IO.Path]::GetRelativePath($projectPath, $candidate).Replace('\', '/')
            if ($managedFiles.Contains($candidateRelative)) { continue }
            if (Test-Path -LiteralPath $candidate) {
                $file = Get-Item -LiteralPath $candidate -Force
                if ($file.PSIsContainer -or ($file.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                    throw "Refusing to remove a directory or link as a managed file: $candidate"
                }
                $parent = $file.Directory
                while ($parent.FullName -ne $projectPath) {
                    if ($parent.Attributes -band [IO.FileAttributes]::ReparsePoint) {
                        throw "Managed file has a linked parent: $candidate"
                    }
                    $parent = $parent.Parent
                }
                Remove-Item -LiteralPath $candidate
            }
        }
    }
}

function Invoke-Editor([string[]]$Arguments, [string]$LogPath) {
    Assert-NoEditor $projectPath
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $editorExecutable
    $startInfo.WorkingDirectory = $projectPath
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $editorArguments = @('-batchmode')
    if (!$EnableGraphics) {
        $editorArguments += '-nographics'
    }
    $editorArguments += @('-projectPath', $projectPath, '-logFile', $LogPath) + $Arguments
    foreach ($argument in $editorArguments) {
        $startInfo.ArgumentList.Add($argument)
    }

    $editorProcess = [Diagnostics.Process]::Start($startInfo)
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $nextProgress = 30
    try {
        while (!$editorProcess.WaitForExit(1000)) {
            if ($timer.Elapsed.TotalSeconds -ge $TimeoutSeconds) {
                $editorProcess.Kill($true)
                $editorProcess.WaitForExit()
                throw "Validation timed out after $TimeoutSeconds seconds. Log: $LogPath"
            }
            if ($timer.Elapsed.TotalSeconds -ge $nextProgress) {
                Write-Host ("Unity is running ({0:N0}s). Log: {1}" -f $timer.Elapsed.TotalSeconds, $LogPath)
                $nextProgress += 30
            }
        }
        return $editorProcess.ExitCode
    }
    finally {
        if (!$editorProcess.HasExited) {
            $editorProcess.Kill($true)
            $editorProcess.WaitForExit()
        }
        $editorProcess.Dispose()
    }
}

if ($OwnerCount -eq 0) {
    $OwnerCount = if ($Mode -eq 'Scale') { 300 } else { 100 }
}
if ($Mode -eq 'Scale' -and $OwnerCount -notin @(300, 1000)) {
    throw 'Scale supports OwnerCount 300 or 1000.'
}
if ($Mode -eq 'Comparison' -and $OwnerCount -notin @(100, 200, 300)) {
    throw 'Comparison supports OwnerCount 100, 200 or 300.'
}
if ([string]::IsNullOrWhiteSpace($ProjectDirectory)) {
    $rootBytes = [Text.Encoding]::UTF8.GetBytes($packageRoot)
    $rootHasher = [Security.Cryptography.SHA256]::Create()
    try {
        $rootId = [BitConverter]::ToString($rootHasher.ComputeHash($rootBytes)).Replace('-', '').Substring(0, 12)
    }
    finally { $rootHasher.Dispose() }
    $ProjectDirectory = Join-Path ([IO.Path]::GetTempPath()) "SuperHeroUIValidation/$rootId/$Mode"
}
$projectPath = [IO.Path]::GetFullPath($ProjectDirectory).TrimEnd('\', '/')
if ($projectPath.StartsWith($packageRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase) -or $projectPath -eq $packageRoot) {
    throw 'The generated test host must be outside the package source directory.'
}
Assert-NoEditor $projectPath
$markerPath = Join-Path $projectPath '.superhero-ui-validation.json'
if (Test-Path -LiteralPath $markerPath) {
    $marker = Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json
    if ($marker.PackageRoot -ne $packageRoot -or $marker.Mode -ne $Mode) {
        throw 'This directory belongs to another validation source or mode. Choose a different ProjectDirectory.'
    }
}
elseif ((Test-Path -LiteralPath $projectPath) -and @(Get-ChildItem -LiteralPath $projectPath -Force).Count -gt 0) {
    throw 'Refusing to overwrite an existing project without a matching validation marker.'
}
else {
    [IO.Directory]::CreateDirectory($projectPath) | Out-Null
    Write-JsonFile $markerPath ([ordered]@{ PackageRoot=$packageRoot; Mode=$Mode })
}

# Prevent two validation scripts from copying into the same project before Unity locks it.
$hostLockPath = Join-Path $projectPath '.superhero-ui-run.lock'
$hostLock = [IO.File]::Open($hostLockPath, 'OpenOrCreate', 'ReadWrite', 'None')
try {
    Assert-NoEditor $projectPath
    $managedFiles = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $managedManifest = Join-Path $projectPath '.superhero-ui-template-files.json'
    $previousFiles = if (Test-Path -LiteralPath $managedManifest) {
        @(Get-Content -LiteralPath $managedManifest -Raw | ConvertFrom-Json)
    } else { @() }
    Copy-TemplateTree (Join-Path $templateRoot 'Assets') (Join-Path $projectPath 'Assets')
    Copy-TemplateTree (Join-Path $templateRoot 'ProjectSettings') (Join-Path $projectPath 'ProjectSettings')
    [IO.Directory]::CreateDirectory((Join-Path $projectPath 'Packages')) | Out-Null
    $manifest = Get-Content (Join-Path $templateRoot 'Packages/manifest.json') -Raw | ConvertFrom-Json -AsHashtable
    $manifest.dependencies['com.superherounite.ui'] = 'file:' + $packageRoot.Replace('\', '/')
    Write-JsonFile (Join-Path $projectPath 'Packages/manifest.json') $manifest

    if ($Mode -eq 'Comparison') {
        $comparisonPath = Join-Path $projectPath 'Assets/PerformanceComparison'
        Copy-TemplateTree (Join-Path $templateRoot 'Comparison') $comparisonPath
        foreach ($sourceName in @('StyleRecipeProcessor', 'PrefabTargetResolver')) {
            $legacyLines = & git -C $packageRoot show "${baselineRevision}:Editor/Processing/$sourceName.cs"
            if ($LASTEXITCODE -ne 0) {
                throw "Cannot read the benchmark baseline $baselineRevision. Fetch that commit before running Comparison."
            }
            $legacyText = ($legacyLines -join "`n").Replace('StyleRecipeProcessor', 'LegacyStyleRecipeProcessor')
            $legacyText = $legacyText.Replace('PrefabTargetResolver', 'LegacyPrefabTargetResolver') + "`n"
            [IO.File]::WriteAllText((Join-Path $comparisonPath "Legacy$sourceName.cs"), $legacyText, $utf8)
            $managedFiles.Add("Assets/PerformanceComparison/Legacy$sourceName.cs") | Out-Null
        }
    }
    Remove-ObsoleteTemplateFiles
    Write-JsonFile $managedManifest @($managedFiles | Sort-Object)

    if ($PrepareOnly) {
        Write-Output ([pscustomobject]@{ Mode=$Mode; ProjectPath=$projectPath; Prepared=$true })
        return
    }
    $resultDirectory = Join-Path $projectPath 'Results'
    [IO.Directory]::CreateDirectory($resultDirectory) | Out-Null
    $runId = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 6)
    $bootstrapLog = Join-Path $resultDirectory "$runId-bootstrap.log"
    Write-Host "Preparing TMP resources in $projectPath"
    $bootstrapExit = Invoke-Editor @('-executeMethod', 'SuperHeroUnite.UI.Validation.ValidationBootstrap.EnsureResources') $bootstrapLog
    if ($bootstrapExit -ne 0) {
        throw "Resource setup or compilation failed (exit $bootstrapExit). Log: $bootstrapLog"
    }

    $resultPath = Join-Path $resultDirectory "$runId-$Mode-$OwnerCount.xml"
    $testLog = Join-Path $resultDirectory "$runId-tests.log"
    $testArguments = @('-runTests', '-testPlatform', 'EditMode', '-assemblyNames', 'SuperHeroUnite.UI.Editor.Tests', '-testResults', $resultPath)
    switch ($Mode) {
        'Tests' { $testArguments += @('-testCategory', '!Scale;!PerformanceComparison') }
        'Scale' {
            $testArguments += @('-testCategory', 'Scale', '-testFilter', "MixedWorkloadReusesSafeOwnersAndAppliesOnlyChangedDependencyClosure\($OwnerCount\)")
        }
        'Comparison' {
            $testArguments += @('-testCategory', 'PerformanceComparison', '-testFilter', "CompareLegacyAndCurrentOnIdenticalAssets\($OwnerCount\)")
        }
    }
    # Unity's test runner and bootstrap own Editor exit; -quit would terminate asynchronous work early.
    $testExit = Invoke-Editor $testArguments $testLog
    if (!(Test-Path -LiteralPath $resultPath)) {
        throw "Unity did not produce a test report (exit $testExit). Log: $testLog"
    }
    [xml]$report = Get-Content -LiteralPath $resultPath -Raw
    $run = $report.'test-run'
    $summary = [ordered]@{
        Mode=$Mode; OwnerCount=$OwnerCount; ProjectPath=$projectPath; Result=$run.result
        Total=[int]$run.total; Passed=[int]$run.passed; Failed=[int]$run.failed
        ExitCode=$testExit; DurationSeconds=[double]$run.duration; Report=$resultPath; Log=$testLog
    }
    Write-JsonFile (Join-Path $resultDirectory "$runId-summary.json") $summary
    if ($testExit -ne 0 -or $summary.Total -eq 0 -or $summary.Passed -eq 0 -or
        $summary.Failed -ne 0 -or $summary.Result -ne 'Passed') {
        foreach ($failed in $report.SelectNodes('//test-case[@result="Failed"]')) {
            Write-Warning ($failed.fullname + ': ' + $failed.failure.message.InnerText)
        }
        throw "Validation failed or selected no tests. Report: $resultPath"
    }
    Write-Output ([pscustomobject]$summary)
}
finally {
    $hostLock.Dispose()
}
