param(
    $Target="",
    [switch]$DryRunSign = $false)

$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

. '.\scripts\checkLastExitCode.ps1'
. '.\scripts\clean.ps1'
. '.\scripts\buildProjects.ps1'
. '.\scripts\getScriptDirectory.ps1'
. '.\scripts\nuget.ps1'
. '.\scripts\sign.ps1'
. '.\scripts\exec.ps1'

$PROJECT_DIR = Get-ScriptDirectory
$RELEASE_DIR = [io.path]::combine($PROJECT_DIR, "artifacts")
$OUT_DIR = [io.path]::combine($PROJECT_DIR, "artifacts")

$LUCENE_SRC_DIR = [io.path]::combine($PROJECT_DIR, "src", "Lucene.Net")
$LUCENE_OUT_DIR = [io.path]::combine($PROJECT_DIR, "src", "Lucene.Net", "bin", "Release")
$LUCENE_DLL_PATH = [io.path]::combine($LUCENE_OUT_DIR, "netstandard2.0", "Lucene.Net.dll");

New-Item -Path $RELEASE_DIR -Type Directory -Force
CleanFiles $RELEASE_DIR

CleanSrcDirs $LUCENE_SRC_DIR

BuildLucene $LUCENE_SRC_DIR
SignFile $PROJECT_DIR $LUCENE_DLL_PATH $DryRunSign
CreateNugetPackage $LUCENE_SRC_DIR $RELEASE_DIR


