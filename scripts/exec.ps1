function Invoke-NativeApplication
{
    param
    (
        [ScriptBlock] $ScriptBlock,
        [int[]] $AllowedExitCodes = @(0),
        [switch] $IgnoreExitCode
    )

    $backupErrorActionPreference = $ErrorActionPreference

    $ErrorActionPreference = "Continue"
    try
    {
        if (Test-CalledFromPrompt)
        {
            $wrapperScriptBlock = { & $ScriptBlock }
        }
        else
        {
            $wrapperScriptBlock = { & $ScriptBlock 2>&1 }
        }

        & $wrapperScriptBlock | ForEach-Object -Process `
            {
                $isError = $_ -is [System.Management.Automation.ErrorRecord]
                "$_" | Add-Member -Name IsError -MemberType NoteProperty -Value $isError -PassThru
            }
        if ((-not $IgnoreExitCode) -and (Test-Path -Path Variable:LASTEXITCODE) -and ($AllowedExitCodes -notcontains $LASTEXITCODE))
        {
            throw "Execution failed with exit code $LASTEXITCODE"
        }
    }
    finally
    {
        $ErrorActionPreference = $backupErrorActionPreference
    }
}

function Invoke-NativeApplicationSafe
{
    param
    (
        [ScriptBlock] $ScriptBlock
    )

    Invoke-NativeApplication -ScriptBlock $ScriptBlock -IgnoreExitCode | `
        Where-Object -FilterScript { -not $_.IsError }
}

function Test-CalledFromPrompt
{
    (Get-PSCallStack)[-2].Command -eq "prompt"
}

Set-Alias -Name exec -Value Invoke-NativeApplication
Set-Alias -Name safeexec -Value Invoke-NativeApplicationSafe
