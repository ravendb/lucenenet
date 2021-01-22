function BuildLucene ( $srcDir ) {
    write-host "Building Lucene"
    & dotnet build /p:SourceLinkCreate=true --no-incremental `
        --configuration "Release" $srcDir;
    CheckLastExitCode
}

function BuildLuceneSpatialNts ( $srcDir ) {
    write-host "Building Lucene.Net.Contrib.Spatial.NTS"
    & dotnet build /p:SourceLinkCreate=true --no-incremental `
        --configuration "Release" $srcDir;
    CheckLastExitCode
}