function CreateNugetPackage ( $srcDir, $targetFilename ) {
    dotnet pack --output $targetFilename `
        --configuration "Release" `
        $srcDir

    CheckLastExitCode
}