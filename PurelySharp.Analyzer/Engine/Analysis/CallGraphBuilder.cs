using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Operations;

namespace PurelySharp.Analyzer.Engine.Analysis
{
	internal static class CallGraphBuilder
	{
		public static CallGraph Build(Compilation compilation)
		{
			var edges = new Dictionary<IMethodSymbol, ImmutableHashSet<IMethodSymbol>>(SymbolEqualityComparer.Default);

			foreach (var tree in compilation.SyntaxTrees)
			{
				var model = compilation.GetSemanticModel(tree);
				var root = tree.GetRoot();
				var operations = root.DescendantNodes().Select(n => model.GetOperation(n)).OfType<IMethodBodyOperation>();
				foreach (var body in operations)
				{
					var containingMethod = model.GetEnclosingSymbol(body.Syntax.SpanStart) as IMethodSymbol;
					if (containingMethod == null) continue;
					var callees = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
					var delegateTargetsBySymbol = new Dictionary<ISymbol, HashSet<IMethodSymbol>>(SymbolEqualityComparer.Default);
					foreach (var inv in body.Descendants().OfType<IInvocationOperation>())
					{
						if (inv.Syntax != null &&
							ExecutionVisibility.IsInStaticallyUnreachableBranch(
								inv.Syntax,
								model))
						{
							continue;
						}

						if (inv.TargetMethod != null)
						{
							var target = inv.TargetMethod.OriginalDefinition;
							callees.Add(target);

							// Expand potential dynamic targets for interface/virtual dispatch within the current compilation,
							// except when the call is explicitly to base, where dispatch is constrained to the immediate base target.
							var isBaseReference = IsBaseReference(inv.Instance);
							if (!isBaseReference)
							{
								foreach (var impl in ResolvePotentialTargetsForVirtualOrInterfaceCall(target, compilation))
								{
									callees.Add(impl);
								}
							}
						}
					}

					// Include user-defined operator methods and conversion operators
					foreach (var bin in body.Descendants().OfType<IBinaryOperation>())
					{
						if (bin.OperatorMethod != null)
						{
							callees.Add(bin.OperatorMethod.OriginalDefinition);
						}
					}

					foreach (var un in body.Descendants().OfType<IUnaryOperation>())
					{
						if (un.OperatorMethod != null)
						{
							callees.Add(un.OperatorMethod.OriginalDefinition);
						}
					}

					foreach (var compound in body.Descendants().OfType<ICompoundAssignmentOperation>())
					{
						if (compound.OperatorMethod != null)
						{
							callees.Add(compound.OperatorMethod.OriginalDefinition);
						}
					}

					foreach (var conv in body.Descendants().OfType<IConversionOperation>())
					{
						var method = conv.Conversion.MethodSymbol;
						if (conv.Conversion.IsUserDefined && method != null)
						{
							callees.Add(method.OriginalDefinition);
						}
					}

					// Include constructor initializer targets (base()/this())
					foreach (var ctorBody in body.Descendants().OfType<IConstructorBodyOperation>())
					{
						var init = ctorBody.Initializer;
						if (init is IInvocationOperation initInv && initInv.TargetMethod != null)
						{
							callees.Add(initInv.TargetMethod.OriginalDefinition);
						}
					}

					foreach (var methodRef in body.Descendants().OfType<IMethodReferenceOperation>())
					{
						if (methodRef.Method != null)
						{
							callees.Add(methodRef.Method.OriginalDefinition);
						}
					}

					// Include property accessor methods when properties are referenced
					foreach (var propRef in body.Descendants().OfType<IPropertyReferenceOperation>())
					{
						var getter = propRef.Property?.GetMethod;
						if (getter != null)
						{
							callees.Add(getter.OriginalDefinition);
						}
					}

					foreach (var del in body.Descendants().OfType<IDelegateCreationOperation>())
					{
						if (del.Target is IMethodReferenceOperation target && target.Method != null)
						{
							callees.Add(target.Method.OriginalDefinition);
						}
					}

					foreach (var anon in body.Descendants().OfType<IAnonymousFunctionOperation>())
					{
						if (anon.Symbol != null)
						{
							callees.Add(anon.Symbol.OriginalDefinition);
						}
					}

					// Conservatively add edges for awaited invocations
					foreach (var awaitOp in body.Descendants().OfType<IAwaitOperation>())
					{
						foreach (var awaitedInv in awaitOp.Operation.DescendantsAndSelf().OfType<IInvocationOperation>())
						{
							if (awaitedInv.TargetMethod != null)
							{
								callees.Add(awaitedInv.TargetMethod.OriginalDefinition);
							}
						}
					}

					// Capture delegate assignments and initializations to map symbols -> potential target methods
					foreach (var assignment in body.Descendants().OfType<IAssignmentOperation>())
					{
						var targetSymbol = TryResolveSymbol(assignment.Target);
						if (targetSymbol == null) continue;
						IMethodSymbol? targetMethod = null;
						// If target is a property, include its setter in call graph
						if (assignment.Target is IPropertyReferenceOperation propTarget && propTarget.Property?.SetMethod != null)
						{
							callees.Add(propTarget.Property.SetMethod.OriginalDefinition);
						}
						if (assignment.Value is IMethodReferenceOperation mr1)
						{
							targetMethod = mr1.Method?.OriginalDefinition;
						}
						else if (assignment.Value is IDelegateCreationOperation dc1 && dc1.Target is IMethodReferenceOperation mr2)
						{
							targetMethod = mr2.Method?.OriginalDefinition;
						}
						else if (assignment.Value is IAnonymousFunctionOperation anon1 && anon1.Symbol != null)
						{
							callees.Add(anon1.Symbol.OriginalDefinition);
							if (!delegateTargetsBySymbol.TryGetValue(targetSymbol, out var set))
							{
								set = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
								delegateTargetsBySymbol[targetSymbol] = set;
							}
							set.Add(anon1.Symbol.OriginalDefinition);
						}
						if (targetMethod != null)
						{
							if (!delegateTargetsBySymbol.TryGetValue(targetSymbol, out var set))
							{
								set = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
								delegateTargetsBySymbol[targetSymbol] = set;
							}
							set.Add(targetMethod);
						}
					}

					// Capture delegate compound assignments (e.g., "+=") to accumulate targets
					foreach (var compound in body.Descendants().OfType<ICompoundAssignmentOperation>())
					{
						if (compound.Target?.Type?.TypeKind != TypeKind.Delegate) continue;
						var targetSymbol = TryResolveSymbol(compound.Target);
						if (targetSymbol == null) continue;
						IMethodSymbol? targetMethod = null;
						if (compound.Value is IMethodReferenceOperation mr)
						{
							targetMethod = mr.Method?.OriginalDefinition;
						}
						else if (compound.Value is IDelegateCreationOperation dc && dc.Target is IMethodReferenceOperation mrInner)
						{
							targetMethod = mrInner.Method?.OriginalDefinition;
						}
						else if (compound.Value is IAnonymousFunctionOperation anon3 && anon3.Symbol != null)
						{
							callees.Add(anon3.Symbol.OriginalDefinition);
							if (!delegateTargetsBySymbol.TryGetValue(targetSymbol, out var set))
							{
								set = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
								delegateTargetsBySymbol[targetSymbol] = set;
							}
							set.Add(anon3.Symbol.OriginalDefinition);
						}
						if (targetMethod != null)
						{
							if (!delegateTargetsBySymbol.TryGetValue(targetSymbol, out var set))
							{
								set = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
								delegateTargetsBySymbol[targetSymbol] = set;
							}
							set.Add(targetMethod);
						}
					}

					// For compound property assignments and increment/decrement, include property setters
					foreach (var compoundProp in body.Descendants().OfType<ICompoundAssignmentOperation>())
					{
						if (compoundProp.Target is IPropertyReferenceOperation prop && prop.Property?.SetMethod != null)
						{
							callees.Add(prop.Property.SetMethod.OriginalDefinition);
						}
					}

					foreach (var incdec in body.Descendants().OfType<IIncrementOrDecrementOperation>())
					{
						if (incdec.Target is IPropertyReferenceOperation prop && prop.Property != null)
						{
							if (prop.Property.GetMethod != null)
								callees.Add(prop.Property.GetMethod.OriginalDefinition);
							if (prop.Property.SetMethod != null)
								callees.Add(prop.Property.SetMethod.OriginalDefinition);
						}
					}

					// Capture event handler subscriptions (+=) mapping event symbols to potential handler targets
					foreach (var evtAssign in body.Descendants().OfType<IEventAssignmentOperation>())
					{
						var eventSymbol = TryResolveSymbol(evtAssign.EventReference);
						if (eventSymbol == null) continue;
						IMethodSymbol? targetMethod = null;
						if (evtAssign.HandlerValue is IMethodReferenceOperation mr)
						{
							targetMethod = mr.Method?.OriginalDefinition;
						}
						else if (evtAssign.HandlerValue is IDelegateCreationOperation dc && dc.Target is IMethodReferenceOperation mrInner)
						{
							targetMethod = mrInner.Method?.OriginalDefinition;
						}
						else if (evtAssign.HandlerValue is IAnonymousFunctionOperation anon4 && anon4.Symbol != null)
						{
							callees.Add(anon4.Symbol.OriginalDefinition);
							if (!delegateTargetsBySymbol.TryGetValue(eventSymbol, out var set))
							{
								set = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
								delegateTargetsBySymbol[eventSymbol] = set;
							}
							set.Add(anon4.Symbol.OriginalDefinition);
						}
						if (targetMethod != null)
						{
							if (!delegateTargetsBySymbol.TryGetValue(eventSymbol, out var set))
							{
								set = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
								delegateTargetsBySymbol[eventSymbol] = set;
							}
							set.Add(targetMethod);
						}
					}

					foreach (var group in body.Descendants().OfType<IVariableDeclarationGroupOperation>())
					{
						foreach (var decl in group.Declarations)
						{
							foreach (var d in decl.Declarators)
							{
								if (d.Initializer?.Value is IMethodReferenceOperation mr3)
								{
									var sym = d.Symbol;
									if (!delegateTargetsBySymbol.TryGetValue(sym, out var set))
									{
										set = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
										delegateTargetsBySymbol[sym] = set;
									}
									set.Add(mr3.Method.OriginalDefinition);
								}
								else if (d.Initializer?.Value is IDelegateCreationOperation dc2 && dc2.Target is IMethodReferenceOperation mr4)
								{
									var sym = d.Symbol;
									if (!delegateTargetsBySymbol.TryGetValue(sym, out var set))
									{
										set = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
										delegateTargetsBySymbol[sym] = set;
									}
									set.Add(mr4.Method.OriginalDefinition);
								}
							}
						}
					}

					// For delegate invocations, add edges to mapped potential targets
					foreach (var inv in body.Descendants().OfType<IInvocationOperation>())
					{
						if (inv.TargetMethod?.Name == "Invoke" && inv.TargetMethod.ContainingType?.TypeKind == TypeKind.Delegate)
						{
							var sym = TryResolveSymbol(inv.Instance);
							if (sym != null && delegateTargetsBySymbol.TryGetValue(sym, out var targets))
							{
								foreach (var t in targets)
								{
									callees.Add(t.OriginalDefinition);
								}
							}
						}
					}
					edges[containingMethod.OriginalDefinition] = System.Collections.Immutable.ImmutableHashSet.CreateRange<IMethodSymbol>(SymbolEqualityComparer.Default, callees);
				}
			}

			// Optional CFG-guided pass to conservatively add invocation edges discovered per-block
			foreach (var tree in compilation.SyntaxTrees)
			{
				var model = compilation.GetSemanticModel(tree);
				var root = tree.GetRoot();
				var methods = root.DescendantNodes().Select(n => model.GetDeclaredSymbol(n)).OfType<IMethodSymbol>();
				foreach (var method in methods)
				{
					if (method == null) continue;
					var declSyntaxRef = method.DeclaringSyntaxReferences.FirstOrDefault();
					if (declSyntaxRef == null) continue;
					var bodyNode = declSyntaxRef.GetSyntax();
					Microsoft.CodeAnalysis.FlowAnalysis.ControlFlowGraph? cfg = null;
					try
					{
						cfg = Microsoft.CodeAnalysis.FlowAnalysis.ControlFlowGraph.Create(bodyNode, model);
					}
					catch { cfg = null; }
					if (cfg == null) continue;
					if (!edges.TryGetValue(method.OriginalDefinition, out var callerSet))
					{
						callerSet = ImmutableHashSet<IMethodSymbol>.Empty;
					}
					var callerSetBuilder = callerSet.ToBuilder();
					foreach (var block in cfg.Blocks)
					{
						foreach (var op in block.Operations)
						{
							if (op is IInvocationOperation inv && inv.TargetMethod != null)
							{
								var target = inv.TargetMethod.OriginalDefinition;
								callerSetBuilder.Add(target);

								var isBaseReference = IsBaseReference(inv.Instance);
								if (!isBaseReference)
								{
									foreach (var impl in ResolvePotentialTargetsForVirtualOrInterfaceCall(target, compilation))
									{
										callerSetBuilder.Add(impl);
									}
								}
							}
						}
					}
					edges[method.OriginalDefinition] = callerSetBuilder.ToImmutable();
				}
			}
			return new CallGraph(edges.ToImmutableDictionary(SymbolEqualityComparer.Default));
		}

		private static ISymbol? TryResolveSymbol(IOperation? operation)
		{
			return operation switch
			{
				ILocalReferenceOperation localRef => localRef.Local,
				IParameterReferenceOperation paramRef => paramRef.Parameter,
				IFieldReferenceOperation fieldRef => fieldRef.Field,
				IPropertyReferenceOperation propRef => propRef.Property,
				IEventReferenceOperation eventRef => eventRef.Event,
				_ => null
			};
		}

		private static IEnumerable<IMethodSymbol> ResolvePotentialTargetsForVirtualOrInterfaceCall(IMethodSymbol target, Compilation compilation)
		{
			// Interface dispatch: include implementations in types that implement the interface
			if (target.ContainingType?.TypeKind == TypeKind.Interface)
			{
				foreach (var type in EnumerateAllNamedTypes(compilation.Assembly.GlobalNamespace))
				{
					if (!ImplementsInterface(type, target.ContainingType)) continue;
					var impl = type.FindImplementationForInterfaceMember(target) as IMethodSymbol;
					if (impl != null)
					{
						yield return impl.OriginalDefinition;
					}
				}
				if (!target.IsAbstract || HasMethodBody(target))
				{
					yield return target;
				}
				yield break;
			}

			// Virtual/abstract dispatch: include overrides declared in derived types within this compilation
			if (target.IsVirtual || target.IsAbstract || target.IsOverride)
			{
				var baseType = target.ContainingType;
				if (baseType != null)
				{
					foreach (var type in EnumerateAllNamedTypes(compilation.Assembly.GlobalNamespace))
					{
						if (!DerivesFrom(type, baseType)) continue;
						foreach (var member in type.GetMembers())
						{
							if (member is IMethodSymbol m &&
								OverridesTargetMethod(m, target))
							{
								yield return m.OriginalDefinition;
							}
						}
					}
				}
			}
		}

		private static bool ImplementsInterface(INamedTypeSymbol type, INamedTypeSymbol interfaceSymbol)
		{
			if (interfaceSymbol == null)
			{
				return false;
			}

			return type.AllInterfaces.Any(
				i => SymbolEqualityComparer.Default.Equals(
					i.OriginalDefinition,
					interfaceSymbol.OriginalDefinition));
		}

		private static IEnumerable<INamedTypeSymbol> EnumerateAllNamedTypes(INamespaceSymbol root)
		{
			foreach (var member in root.GetMembers())
			{
				if (member is INamespaceSymbol ns)
				{
					foreach (var inner in EnumerateAllNamedTypes(ns))
					{
						yield return inner;
					}
				}
				else if (member is INamedTypeSymbol type)
				{
					yield return type;
					foreach (var nested in EnumerateNestedTypes(type))
					{
						yield return nested;
					}
				}
			}
		}

		private static IEnumerable<INamedTypeSymbol> EnumerateNestedTypes(INamedTypeSymbol type)
		{
			foreach (var member in type.GetTypeMembers())
			{
				yield return member;
				foreach (var nested in EnumerateNestedTypes(member))
				{
					yield return nested;
				}
			}
		}

		private static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol potentialBase)
		{
			for (var t = type.BaseType; t != null; t = t.BaseType)
			{
				if (SymbolEqualityComparer.Default.Equals(t.OriginalDefinition, potentialBase.OriginalDefinition))
				{
					return true;
				}
			}
			return false;
		}

		private static bool OverridesTargetMethod(IMethodSymbol method, IMethodSymbol target)
		{
			var current = method.OverriddenMethod;
			while (current != null)
			{
				if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, target.OriginalDefinition))
				{
					return true;
				}

				current = current.OverriddenMethod;
			}

			return false;
		}

		private static bool HasMethodBody(IMethodSymbol methodSymbol)
		{
			if (methodSymbol.DeclaringSyntaxReferences.Length == 0)
			{
				return false;
			}

			foreach (var syntaxReference in methodSymbol.DeclaringSyntaxReferences)
			{
				var methodSyntax = syntaxReference.GetSyntax();
				if (methodSyntax is Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax methodDeclaration &&
					(methodDeclaration.Body != null || methodDeclaration.ExpressionBody != null))
				{
					return true;
				}
			}

			return false;
		}

		private static bool IsBaseReference(IOperation? operation)
		{
			return operation is IInstanceReferenceOperation instanceReference &&
				instanceReference.ReferenceKind == InstanceReferenceKind.ContainingTypeInstance &&
				operation.Syntax.IsKind(SyntaxKind.BaseExpression);
		}
	}
}
