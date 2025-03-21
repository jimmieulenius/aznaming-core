function Test-ProjectAsset {
    param(
        [Parameter(Mandatory = $true)]
        [String]
        $Path,

        [Switch]
        $AllowPrerelease,

        [String[]]
        $Filter
    )

    function Test-Filter {
        param (
            [Parameter(Mandatory = $true)]
            [String]
            $Value
        )

        if ($Filter) {
            $result = $false

            foreach ($filterItem in $Filter) {
                if ($Value -ilike $filterItem) {
                    $result = $true

                    break
                }
            }

            return $result
        }

        return $true
    }

    function Test-PrereleaseVersion {
        param (
            [Parameter(Mandatory = $true)]
            [String]
            $Name,

            [Parameter(Mandatory = $true)]
            [String]
            $Version
        )

        if ($Version -like '*-*') {
            $invalidAsset.Add("$Name/$Version")
        }
    }

    if (-not $AllowPrerelease) {
        $Path = (
            Resolve-Path `
                -Path $Path
        ).Path

        $contentObject = Get-Content `
            -Path "$Path/obj/project.assets.json" `
            -Raw `
        | ConvertFrom-Json `
            -AsHashtable

        $currentErrorActionPreference = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'

        try {
            if ($contentObject.project.version -like '*-*') {
                Write-Error "Invalid project version found in project.assets.json: $($contentObject.project.version)"
            }

            if ($contentObject.targets) {
                $invalidAsset = [System.Collections.Generic.List[String]]::new()

                $contentObject.targets.GetEnumerator() `
                | ForEach-Object {
                    if ($_.Value) {
                        $_.Value.GetEnumerator() `
                        | ForEach-Object {
                            $component = $_.Key -split '/'
                            $name = $component[0]

                            if (
                                Test-Filter `
                                    -Value $name
                            ) {
                                Test-PrereleaseVersion `
                                    -Name $name `
                                    -Version $component[1]
                            }

                            if ($_.Value.dependencies) {
                                $_.Value.dependencies.GetEnumerator() `
                                | ForEach-Object {
                                    if (
                                        Test-Filter `
                                            -Value $_.Key
                                    ) {
                                        Test-PrereleaseVersion `
                                            -Name $_.Key `
                                            -Version $_.Value
                                    }
                                }
                            }
                        }
                    }
                }

                if ($invalidAsset.Count -gt 0) {
                    Write-Error "Invalid asset(s) found in project.assets.json:$([Environment]::NewLine)    $($invalidAsset -join "$([Environment]::NewLine)    ")"
                }
            }
        }
        finally {
            $ErrorActionPreference = $currentErrorActionPreference
        }
    }
}

function Build-Package {
    param (
        [Parameter(Mandatory = $true)]
        [String]
        $Path,

        [String]
        $Version,

        [String]
        $Configuration = 'Release',

        [Parameter(ParameterSetName = 'Publish')]
        [String]
        $NuGetUri,

        [Parameter(ParameterSetName = 'Publish')]
        [String]
        $NuGetApiKey = $Env:NUGET_API_KEY,

        # [Parameter(ParameterSetName = 'Prerelease')]
        # [Switch]
        # $Prerelease,

        # [Parameter(ParameterSetName = 'Prerelease')]
        # [String]
        # $Suffix

        [Parameter(ParameterSetName = 'Publish')]
        [Switch]
        $Publish,

        [Switch]
        $AllowPrerelease,

        [String[]]
        $ProjectAssetFilter,

        [Parameter(ParameterSetName = 'Publish')]
        [ValidateSet(
            'linux-x64',
            'osx-x64',
            'win-x64'
        )]
        [String[]]
        $Runtime,

        [Parameter(ParameterSetName = 'Publish')]
        [ScriptBlock]
        $PublishAction
    )

    # $Prerelease = $true
    # $Suffix = 'aaa'

    # if (-not $Version) {
    #     $Version = (
    #         git tag `
    #             --sort=-v:refname `
    #     | Select-Object `
    #         -First 1
    #     )?.Substring(1) -as [Version]

    #     if ($Version) {
    #         $Version = [Version]::new(
    #             $Version.Major,
    #             $Version.Minor,
    #             $Version.Build + 1
    #         )
    #     } else {
    #         $Version = '0.0.1' -as [Version]
    #     }
    # }

    # $versionString = "$Version$(((-not [String]::IsNullOrEmpty($Suffix)) ? "-preview-$Suffix" : $null))"

    try {
        $currentLocation = (Get-Location).Path

        $Path = (
            Resolve-Path `
                -Path $Path
        ).Path

        Set-Location `
            -Path $Path

        $arguments = @(
            '--configuration',
            $Configuration
        )

        if (-not [String]::IsNullOrEmpty($Version)) {
            $arguments += "/p:Version=$Version"
        }

        dotnet build $arguments

        Test-ProjectAsset `
            -Path $Path `
            -AllowPrerelease:$AllowPrerelease `
            -Filter $ProjectAssetFilter `
            -ErrorVariable 'errorOutput'

        if ($errorOutput) {
            exit 1
        }

        if ($Publish) {
            dotnet pack $arguments

            dotnet nuget push `
                "./bin/$Configuration/$(
                    Split-Path `
                        -Path $Path `
                        -LeafBase
                )$(
                    (-not [String]::IsNullOrEmpty($Version)) `
                        ? ".$Version" `
                        : $null
                ).nupkg" `
                --source $NuGetUri `
                --api-key $NuGetApiKey

            $Runtime `
            | ForEach-Object {
                Set-Variable `
                    -Name 'PublishOutput' `
                    -Value $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath("./bin/$Configuration/publish/$_") `
                    -Scope 'Global' `
                    -Force

                dotnet publish `
                    --runtime $_ `
                    --configuration $Configuration `
                    --output $PublishOutput

                if ($PublishAction) {
                    & $PublishAction
                }
            }
        }

        # dotnet pack `
        #     --configuration $Configuration `
        #     /p:Version=$versionString

        # dotnet nuget push `
        #     "./bin/$Configuration/$(
        #         Split-Path `
        #             -Path $Path `
        #             -LeafBase
        #     ).$versionString.nupkg" `
        #     --source $RegistryUri `
        #     --api-key $RegistryApiKey
    }
    finally {
        if ($currentLocation) {
            Set-Location `
                -Path $currentLocation
        }
    }
}