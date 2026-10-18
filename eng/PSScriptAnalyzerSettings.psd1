@{
    Severity     = @('Error', 'Warning')
    # These are unattended build/test utilities, with existing helper APIs.
    # Host status output must stay off the success pipeline used for data.
    ExcludeRules = @(
        'PSAvoidUsingWriteHost',
        'PSUseSingularNouns',
        'PSUseShouldProcessForStateChangingFunctions'
    )
}
