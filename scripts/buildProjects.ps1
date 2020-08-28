function BuildLucene ( $srcDir ) {
    write-host "Building Lucene"
    & dotnet build /p:SourceLinkCreate=true --no-incremental `
        --configuration "Release" $srcDir;
    CheckLastExitCode
}

