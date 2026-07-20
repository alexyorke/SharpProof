namespace SharpProof.Analyzer.Engine;

internal static partial class ExecutionVisibility {
    private static bool IsInReachableConstantSwitchGotoSection(
        SyntaxNode syntaxNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        foreach (var switchStatement in syntaxNode.Ancestors().OfType<SwitchStatementSyntax>()) {
            var section =
                switchStatement.Sections.FirstOrDefault(candidate => candidate.Span.Contains(syntaxNode.SpanStart));
            if (section != null &&
                IsReachableConstantSwitchGotoTarget(section, switchStatement, semanticModel, cancellationToken))
                return true;
        }

        return false;
    }

    private static bool IsInUnreachableSwitchStatementSection(
        SyntaxNode syntaxNode,
        SwitchStatementSyntax switchStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService? smtAnalysis) {
        var section =
            switchStatement.Sections.FirstOrDefault(candidate => candidate.Span.Contains(syntaxNode.SpanStart));
        if (section == null ||
            !SwitchPathConditionBuilder.TryCreateSwitchStatementSectionSymbolicCondition(
                switchStatement.Expression,
                section,
                semanticModel,
                cancellationToken,
                out var sectionCondition))
            return false;

        if (IsReachableConstantSwitchGotoTarget(section, switchStatement, semanticModel, cancellationToken))
            return false;

        return HasSymbolicConditionStatusAt(
            sectionCondition,
            SymbolicProofStatus.ProvenFalse,
            switchStatement,
            semanticModel,
            cancellationToken,
            smtAnalysis);
    }

    private static bool IsReachableConstantSwitchGotoTarget(
        SwitchSectionSyntax section,
        SwitchStatementSyntax switchStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        var governingValue = semanticModel.GetConstantValue(switchStatement.Expression, cancellationToken);
        if (!governingValue.HasValue) return false;

        var initialSection = ResolveInitialConstantSwitchSection(
            switchStatement,
            semanticModel,
            cancellationToken,
            governingValue.Value);
        if (initialSection == null) return false;

        var reachableSections = new List<SwitchSectionSyntax> { initialSection };
        for (var index = 0; index < reachableSections.Count; index++)
            foreach (var gotoStatement in reachableSections[index]
                         .DescendantNodes()
                         .OfType<GotoStatementSyntax>()) {
                if (!ReferenceEquals(
                        gotoStatement.Ancestors().OfType<SwitchStatementSyntax>().FirstOrDefault(),
                        switchStatement))
                    continue;

                var targetSection = ResolveConstantSwitchGotoTarget(
                    gotoStatement,
                    switchStatement,
                    semanticModel,
                    cancellationToken);
                if (targetSection == null ||
                    reachableSections.Any(reachableSection => ReferenceEquals(reachableSection, targetSection)))
                    continue;

                reachableSections.Add(targetSection);
            }

        return reachableSections.Any(reachableSection => ReferenceEquals(reachableSection, section));
    }

    private static SwitchSectionSyntax? ResolveInitialConstantSwitchSection(
        SwitchStatementSyntax switchStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        object? governingValue) {
        SwitchSectionSyntax? defaultSection = null;

        foreach (var section in switchStatement.Sections)
            foreach (var label in section.Labels) {
                if (label is DefaultSwitchLabelSyntax) {
                    defaultSection ??= section;
                    continue;
                }

                if (label is CaseSwitchLabelSyntax caseLabel) {
                    var labelValue = semanticModel.GetConstantValue(caseLabel.Value, cancellationToken);
                    if (labelValue.HasValue && Equals(labelValue.Value, governingValue)) return section;

                    continue;
                }

                if (label is CasePatternSwitchLabelSyntax patternLabel &&
                    PatternMatchesConstant(patternLabel.Pattern, governingValue, semanticModel, cancellationToken) &&
                    WhenClauseCanMatch(patternLabel.WhenClause, semanticModel, cancellationToken))
                    return section;
            }

        return defaultSection;
    }

    private static SwitchSectionSyntax? ResolveConstantSwitchGotoTarget(
        GotoStatementSyntax gotoStatement,
        SwitchStatementSyntax switchStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        if (gotoStatement.IsKind(SyntaxKind.GotoDefaultStatement))
            return switchStatement.Sections.FirstOrDefault(section =>
                section.Labels.Any(label => label is DefaultSwitchLabelSyntax));

        if (!gotoStatement.IsKind(SyntaxKind.GotoCaseStatement) ||
            gotoStatement.Expression == null)
            return null;

        var gotoValue = semanticModel.GetConstantValue(gotoStatement.Expression, cancellationToken);
        if (!gotoValue.HasValue) return null;

        foreach (var section in switchStatement.Sections)
            if (section.Labels.OfType<CaseSwitchLabelSyntax>().Any(label =>
                    semanticModel.GetConstantValue(label.Value, cancellationToken) is { HasValue: true } labelValue &&
                    Equals(labelValue.Value, gotoValue.Value)))
                return section;

        return null;
    }

    private static bool PatternMatchesConstant(
        PatternSyntax pattern,
        object? governingValue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) => pattern switch {
            DiscardPatternSyntax => true,
            ParenthesizedPatternSyntax parenthesized => PatternMatchesConstant(
                parenthesized.Pattern, governingValue, semanticModel, cancellationToken),
            ConstantPatternSyntax constant =>
                semanticModel.GetConstantValue(constant.Expression, cancellationToken) is { HasValue: true } value && Equals(value.Value, governingValue),
            _ => false
        };

    private static bool WhenClauseCanMatch(
        WhenClauseSyntax? whenClause,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) => whenClause == null ||
               semanticModel.GetConstantValue(whenClause.Condition, cancellationToken) is { HasValue: true, Value: true };
}
