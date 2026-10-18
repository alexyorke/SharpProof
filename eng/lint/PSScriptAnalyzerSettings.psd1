@{
    # Correctness/security rules; formatting and naming remain repository conventions.
    IncludeRules = @(
        'PSAvoidAssignmentToAutomaticVariable',
        'PSAvoidDefaultValueForMandatoryParameter',
        'PSAvoidDefaultValueSwitchParameter',
        'PSAvoidGlobalAliases',
        'PSAvoidInvokingEmptyMembers',
        'PSAvoidOverwritingBuiltInCmdlets',
        'PSAvoidUsingConvertToSecureStringWithPlainText',
        'PSAvoidUsingInvokeExpression',
        'PSAvoidUsingPlainTextForPassword',
        'PSAvoidUsingUsernameAndPasswordParams',
        'PSMisleadingBacktick',
        'PSUseDeclaredVarsMoreThanAssignments'
    )
}
