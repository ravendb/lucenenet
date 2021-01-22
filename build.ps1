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
$LUCENE_DLL_PATH = [io.path]::combine($LUCENE_OUT_DIR, "netstandard2.1", "Lucene.Net.dll")

$LUCENE_SPATIAL_NTS_SRC_DIR = [io.path]::combine($PROJECT_DIR, "src", "contrib", "Lucene.Net.Contrib.Spatial.NTS")
$LUCENE_SPATIAL_NTS_SRC_DIR_CSPROJ = [io.path]::combine($LUCENE_SPATIAL_NTS_SRC_DIR, "Lucene.Net.Contrib.Spatial.NTS.csproj")
$LUCENE_SPATIAL_NTS_OUT_DIR = [io.path]::combine($PROJECT_DIR, "src", "contrib", "Lucene.Net.Contrib.Spatial.NTS", "bin", "Release")
$LUCENE_SPATIAL_NTS_DLL_PATH = [io.path]::combine($LUCENE_SPATIAL_NTS_OUT_DIR, "netstandard2.1", "Lucene.Net.Contrib.Spatial.NTS.dll")
$LUCENE_SPATIAL4N_DLL_PATH = [io.path]::combine($LUCENE_SPATIAL_NTS_OUT_DIR, "netstandard2.1", "Spatial4n.Core.NTS.dll")

New-Item -Path $RELEASE_DIR -Type Directory -Force
CleanFiles $RELEASE_DIR

CleanSrcDirs $LUCENE_SRC_DIR

BuildLucene $LUCENE_SRC_DIR
BuildLuceneSpatialNts $LUCENE_SPATIAL_NTS_SRC_DIR_CSPROJ

SignFile $PROJECT_DIR $LUCENE_DLL_PATH $DryRunSign
SignFile $PROJECT_DIR $LUCENE_SPATIAL_NTS_DLL_PATH $DryRunSign
SignFile $PROJECT_DIR $LUCENE_SPATIAL4N_DLL_PATH $DryRunSign

CreateNugetPackage $LUCENE_SRC_DIR $RELEASE_DIR
CreateNugetPackage $LUCENE_SPATIAL_NTS_SRC_DIR_CSPROJ $RELEASE_DIR

