using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace SharpProof.Analyzer.Engine.Rules
{
	internal static class RuleRegistry
	{
		public static ImmutableList<IPurityRule> GetDefaultRules()
		{
			// Group by construct families for clarity of ordering (keep stable behavior)
			return ImmutableList.Create<IPurityRule>(
				// Invocation/Calls (keep primary method invocation rule first)
				new MethodInvocationPurityRule(),
				new DynamicOperationPurityRule(),
				new ConstructorInitializerPurityRule(),
				new DelegateCreationPurityRule(),
				new AwaitPurityRule(),
				new EventReferencePurityRule(),
				new EventAssignmentPurityRule(),
				
				// Assignments/References
				new DeconstructionAssignmentPurityRule(),
				new AssignmentPurityRule(),
				CreateChildOperationsPureRule(OperationKind.ExpressionStatement),
				CreateAlwaysPureRule(OperationKind.ParameterReference, "ParamRefRule", "Parameter reference"),
				CreateAlwaysPureRule(OperationKind.LocalReference, "LocalRefRule", "LocalReference"),
				new FieldReferencePurityRule(),
				CreateAlwaysPureRule(OperationKind.InstanceReference, "InstRefRule", "InstanceReference"),
				
				// Object/Array creation and initialization
				new ObjectCreationPurityRule(),
				new AnonymousObjectCreationPurityRule(),
				new ObjectOrCollectionInitializerPurityRule(),
				new ArrayCreationPurityRule(),
				CreateChildOperationsPureRule(OperationKind.ArrayInitializer),
				new ArrayElementReferencePurityRule(),
				new InlineArrayAccessPurityRule(),
				new CollectionExpressionPurityRule(),
				new SpreadOperationPurityRule(),
				
				// Expressions/Operators
				new BinaryOperationPurityRule(),
				new UnaryOperationPurityRule(),
				new CoalesceOperationPurityRule(),
				new ConditionalAccessPurityRule(),
				new ConditionalOperationPurityRule(),
				CreateChildOperationsPureRule(OperationKind.Range),
				new ImplicitIndexerReferencePurityRule(),
				new ConversionPurityRule(),
				CreateAlwaysPureRule(OperationKind.DefaultValue, "DefaultValueRule", "DefaultValue operation", includeSyntaxInLog: false),
				new InterpolatedStringPurityRule(),
				new PropertyReferencePurityRule(),
				CreateAlwaysPureRule(OperationKind.Literal, "LiteralRule", "Literal operation"),
				CreateChildOperationsPureRule(OperationKind.Tuple),
				CreateAlwaysPureRule(OperationKind.TypeOf, "TypeOfRule", "TypeOf operation"),
				CreateAlwaysPureRule(OperationKind.NameOf, "NameOfRule", "NameOf operation"),
				CreateAlwaysPureRule(OperationKind.Utf8String, "Utf8StringLiteralPurityRule", "Utf8String operation"),
				CreateAlwaysPureRule(OperationKind.SizeOf, "SizeOfRule", "SizeOf operation"),
				
				// Patterns
				CreateChildOperationsPureRule(OperationKind.BinaryPattern),
				CreateAlwaysPureRule(OperationKind.ConstantPattern, "ConstantPatternRule", "Constant pattern"),
				CreateAlwaysPureRule(OperationKind.DeclarationPattern, "DeclarationPatternRule", "Declaration pattern"),
				CreateAlwaysPureRule(OperationKind.DiscardPattern, "DiscardPatternRule", "Discard pattern"),
				CreateChildOperationsPureRule(OperationKind.NegatedPattern),
				new ListPatternPurityRule(),
				new PropertySubpatternPurityRule(),
				CreateChildOperationsPureRule(OperationKind.RelationalPattern),
				new RecursivePatternPurityRule(),
				CreateChildOperationsPureRule(OperationKind.TypePattern, OperationKind.IsType),
				CreateChildOperationsPureRule(OperationKind.IsPattern),
				new IsNullPurityRule(),
				new StructuralPurityRule(),
				
				// Control Flow
				CreateAlwaysPureRule(OperationKind.Branch, "BranchRule", "Branch operation"),
				new SwitchStatementPurityRule(),
				new SwitchCasePurityRule(),
				new SwitchExpressionPurityRule(),
				new LoopPurityRule(),
				new UsingStatementPurityRule(),
				new ThrowOperationPurityRule(),
				new LockStatementPurityRule(),
				new YieldReturnPurityRule(),
				
				// Returns
				new ReturnStatementPurityRule(),
				
				// Misc
				new WithOperationPurityRule()
			);
		}

		private static IPurityRule CreateAlwaysPureRule(
			OperationKind operationKind,
			string ruleName,
			string operationDescription,
			bool includeSyntaxInLog = true)
		{
			return new DeclarativePureOperationRule(new PureOperationRuleDescriptor(
				operationKind,
				ruleName,
				operationDescription,
				includeSyntaxInLog));
		}

		private static IPurityRule CreateChildOperationsPureRule(params OperationKind[] operationKinds)
		{
			return new ChildOperationsPurityRule(operationKinds);
		}
	}
}
