﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.PooledObjects;
using Microsoft.CodeAnalysis.SourceGeneration;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Utilities;

#pragma warning disable RSEXPERIMENTAL004 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

namespace Microsoft.CodeAnalysis
{
    /// <summary>
    /// Context passed to an incremental generator when <see cref="IIncrementalGenerator.Initialize(IncrementalGeneratorInitializationContext)"/> is called
    /// </summary>
    public readonly partial struct IncrementalGeneratorInitializationContext
    {
        private readonly ArrayBuilder<SyntaxInputNode> _syntaxInputBuilder;
        private readonly ArrayBuilder<IIncrementalGeneratorOutputNode> _outputNodes;
        private readonly string _sourceExtension;
        private readonly string _embeddedAttributeDefinition;
        internal readonly ISyntaxHelper SyntaxHelper;
        internal readonly bool CatchAnalyzerExceptions;

        internal IncrementalGeneratorInitializationContext(
            ArrayBuilder<SyntaxInputNode> syntaxInputBuilder,
            ArrayBuilder<IIncrementalGeneratorOutputNode> outputNodes,
            ISyntaxHelper syntaxHelper,
            string sourceExtension,
            string embeddedAttributeDefinition,
            bool catchAnalyzerExceptions)
        {
            _syntaxInputBuilder = syntaxInputBuilder;
            _outputNodes = outputNodes;
            SyntaxHelper = syntaxHelper;
            _sourceExtension = sourceExtension;
            _embeddedAttributeDefinition = embeddedAttributeDefinition;
            CatchAnalyzerExceptions = catchAnalyzerExceptions;
        }

        public SyntaxValueProvider SyntaxProvider => new(this, _syntaxInputBuilder, RegisterOutput, SyntaxHelper);

        public IncrementalValueProvider<Compilation> CompilationProvider => new IncrementalValueProvider<Compilation>(SharedInputNodes.Compilation.WithRegisterOutput(RegisterOutput).WithTrackingName(WellKnownGeneratorInputs.Compilation), CatchAnalyzerExceptions);

        // Use a ReferenceEqualityComparer as we want to rerun this stage whenever the CompilationOptions changes at all
        // (e.g. we don't care if it has the same conceptual value, we're ok rerunning as long as the actual instance
        // changes).
        internal IncrementalValueProvider<CompilationOptions> CompilationOptionsProvider
            => new(SharedInputNodes.CompilationOptions.WithRegisterOutput(RegisterOutput)
                .WithComparer(ReferenceEqualityComparer.Instance)
                .WithTrackingName(WellKnownGeneratorInputs.CompilationOptions), CatchAnalyzerExceptions);

        public IncrementalValueProvider<ParseOptions> ParseOptionsProvider => new IncrementalValueProvider<ParseOptions>(SharedInputNodes.ParseOptions.WithRegisterOutput(RegisterOutput).WithTrackingName(WellKnownGeneratorInputs.ParseOptions), CatchAnalyzerExceptions);

        public IncrementalValuesProvider<AdditionalText> AdditionalTextsProvider => new IncrementalValuesProvider<AdditionalText>(SharedInputNodes.AdditionalTexts.WithRegisterOutput(RegisterOutput).WithTrackingName(WellKnownGeneratorInputs.AdditionalTexts), CatchAnalyzerExceptions);

        public IncrementalValueProvider<AnalyzerConfigOptionsProvider> AnalyzerConfigOptionsProvider => new IncrementalValueProvider<AnalyzerConfigOptionsProvider>(SharedInputNodes.AnalyzerConfigOptions.WithRegisterOutput(RegisterOutput).WithTrackingName(WellKnownGeneratorInputs.AnalyzerConfigOptions), CatchAnalyzerExceptions);

        public IncrementalValuesProvider<MetadataReference> MetadataReferencesProvider => new IncrementalValuesProvider<MetadataReference>(SharedInputNodes.MetadataReferences.WithRegisterOutput(RegisterOutput).WithTrackingName(WellKnownGeneratorInputs.MetadataReferences), CatchAnalyzerExceptions);

        public void RegisterSourceOutput<TSource>(IncrementalValueProvider<TSource> source, Action<SourceProductionContext, TSource> action) => RegisterSourceOutput(source.Node, action, IncrementalGeneratorOutputKind.Source, _sourceExtension);

        public void RegisterSourceOutput<TSource>(IncrementalValuesProvider<TSource> source, Action<SourceProductionContext, TSource> action) => RegisterSourceOutput(source.Node, action, IncrementalGeneratorOutputKind.Source, _sourceExtension);

        public void RegisterImplementationSourceOutput<TSource>(IncrementalValueProvider<TSource> source, Action<SourceProductionContext, TSource> action) => RegisterSourceOutput(source.Node, action, IncrementalGeneratorOutputKind.Implementation, _sourceExtension);

        public void RegisterImplementationSourceOutput<TSource>(IncrementalValuesProvider<TSource> source, Action<SourceProductionContext, TSource> action) => RegisterSourceOutput(source.Node, action, IncrementalGeneratorOutputKind.Implementation, _sourceExtension);

        public void RegisterPostInitializationOutput(Action<IncrementalGeneratorPostInitializationContext> callback) => _outputNodes.Add(new PostInitOutputNode(callback.WrapUserAction(CatchAnalyzerExceptions), _embeddedAttributeDefinition));

        [Experimental(RoslynExperiments.GeneratorHostOutputs, UrlFormat = RoslynExperiments.GeneratorHostOutputs_Url)]
        public void RegisterHostOutput<TSource>(IncrementalValueProvider<TSource> source, Action<HostOutputProductionContext, TSource> action) => source.Node.RegisterOutput(new HostOutputNode<TSource>(source.Node, action.WrapUserAction(CatchAnalyzerExceptions)));

        [Experimental(RoslynExperiments.GeneratorHostOutputs, UrlFormat = RoslynExperiments.GeneratorHostOutputs_Url)]
        public void RegisterHostOutput<TSource>(IncrementalValuesProvider<TSource> source, Action<HostOutputProductionContext, TSource> action) => source.Node.RegisterOutput(new HostOutputNode<TSource>(source.Node, action.WrapUserAction(CatchAnalyzerExceptions)));

        private void RegisterOutput(IIncrementalGeneratorOutputNode outputNode)
        {
            if (!_outputNodes.Contains(outputNode))
            {
                _outputNodes.Add(outputNode);
            }
        }

        private void RegisterSourceOutput<TSource>(IIncrementalGeneratorNode<TSource> node, Action<SourceProductionContext, TSource> action, IncrementalGeneratorOutputKind kind, string sourceExt)
        {
            node.RegisterOutput(new SourceOutputNode<TSource>(node, action.WrapUserAction(CatchAnalyzerExceptions), kind, sourceExt));
        }
    }

    /// <summary>
    /// Context passed to an incremental generator when it has registered an output via <see cref="IncrementalGeneratorInitializationContext.RegisterPostInitializationOutput(Action{IncrementalGeneratorPostInitializationContext})"/>
    /// </summary>
    public readonly struct IncrementalGeneratorPostInitializationContext
    {
        internal readonly AdditionalSourcesCollection AdditionalSources;
        private readonly string _embeddedAttributeDefinition;

        internal IncrementalGeneratorPostInitializationContext(AdditionalSourcesCollection additionalSources, string embeddedAttributeDefinition, CancellationToken cancellationToken)
        {
            AdditionalSources = additionalSources;
            _embeddedAttributeDefinition = embeddedAttributeDefinition;
            CancellationToken = cancellationToken;
        }

        /// <summary>
        /// A <see cref="System.Threading.CancellationToken"/> that can be checked to see if the PostInitialization should be cancelled.
        /// </summary>
        public CancellationToken CancellationToken { get; }

        /// <summary>
        /// Adds source code in the form of a <see cref="string"/> to the compilation that will be available during subsequent phases
        /// </summary>
        /// <param name="hintName">An identifier that can be used to reference this source text, must be unique within this generator</param>
        /// <param name="source">The source code to add to the compilation</param>
        public void AddSource(string hintName, string source) => AddSource(hintName, SourceText.From(source, Encoding.UTF8));

        /// <summary>
        /// Adds a <see cref="SourceText"/> to the compilation that will be available during subsequent phases
        /// </summary>
        /// <param name="hintName">An identifier that can be used to reference this source text, must be unique within this generator</param>
        /// <param name="sourceText">The <see cref="SourceText"/> to add to the compilation</param>
        /// <remarks>
        /// Directory separators "/" and "\" are allowed in <paramref name="hintName"/>, they are normalized to "/" regardless of host platform.
        /// </remarks>
        public void AddSource(string hintName, SourceText sourceText) => AdditionalSources.Add(hintName, sourceText);

        /// <summary>
        /// Adds a <see cref="SourceText" /> to the compilation containing the definition of <c>Microsoft.CodeAnalysis.EmbeddedAttribute</c>.
        /// The source will have a <c>hintName</c> of Microsoft.CodeAnalysis.EmbeddedAttribute. 
        /// </summary>
        /// <remarks>
        /// This attribute can be used to mark a type as being only visible to the current assembly. Most commonly, any types provided during this <see cref="IncrementalGeneratorPostInitializationContext"/>
        /// should be marked with this attribute to prevent them from being used by other assemblies. The attribute will prevent any downstream assemblies from consuming the type.
        /// </remarks>
        public void AddEmbeddedAttributeDefinition() => AddSource("Microsoft.CodeAnalysis.EmbeddedAttribute", SourceText.From(_embeddedAttributeDefinition, encoding: Encoding.UTF8));
    }

    /// <summary>
    /// Context passed to an incremental generator when it has registered an output via <see cref="IncrementalGeneratorInitializationContext.RegisterSourceOutput{TSource}(IncrementalValueProvider{TSource}, Action{SourceProductionContext, TSource})"/>
    /// </summary>
    public readonly struct SourceProductionContext
    {
        internal readonly AdditionalSourcesCollection Sources;
        internal readonly DiagnosticBag Diagnostics;
        internal readonly Compilation Compilation;

        internal SourceProductionContext(AdditionalSourcesCollection sources, DiagnosticBag diagnostics, Compilation compilation, CancellationToken cancellationToken)
        {
            CancellationToken = cancellationToken;
            Sources = sources;
            Diagnostics = diagnostics;
            Compilation = compilation;
        }

        public CancellationToken CancellationToken { get; }

        /// <summary>
        /// Adds source code in the form of a <see cref="string"/> to the compilation.
        /// </summary>
        /// <param name="hintName">An identifier that can be used to reference this source text, must be unique within this generator</param>
        /// <param name="source">The source code to add to the compilation</param>
        public void AddSource(string hintName, string source) => AddSource(hintName, SourceText.From(source, Encoding.UTF8));

        /// <summary>
        /// Adds a <see cref="SourceText"/> to the compilation
        /// </summary>
        /// <param name="hintName">An identifier that can be used to reference this source text, must be unique within this generator</param>
        /// <param name="sourceText">The <see cref="SourceText"/> to add to the compilation</param>
        /// <remarks>
        /// Directory separators "/" and "\" are allowed in <paramref name="hintName"/>, they are normalized to "/" regardless of host platform.
        /// </remarks>
        public void AddSource(string hintName, SourceText sourceText) => Sources.Add(hintName, sourceText);

        /// <summary>
        /// Adds a <see cref="Diagnostic"/> to the users compilation
        /// </summary>
        /// <param name="diagnostic">The diagnostic that should be added to the compilation</param>
        /// <remarks>
        /// The severity of the diagnostic may cause the compilation to fail, depending on the <see cref="Compilation"/> settings.
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// <paramref name="diagnostic"/> is located in a syntax tree which is not part of the compilation,
        /// its location span is outside of the given file, or its identifier is not valid.
        /// </exception>
        public void ReportDiagnostic(Diagnostic diagnostic)
        {
            DiagnosticAnalysisContextHelpers.VerifyArguments(diagnostic, Compilation, isSupportedDiagnostic: static (_, _) => true, CancellationToken);
            Diagnostics.Add(diagnostic);
        }
    }

    /// <summary>
    /// Context passed to a filter to determine if a generator should be executed or not.
    /// </summary>
    public readonly struct GeneratorFilterContext
    {
        internal GeneratorFilterContext(ISourceGenerator generator, CancellationToken cancellationToken)
        {
            Generator = generator;
            CancellationToken = cancellationToken;
        }

        /// <summary>
        /// The generator instance that is being filtered
        /// </summary>
        public ISourceGenerator Generator { get; }

        /// <summary>
        /// A <see cref="System.Threading.CancellationToken"/> that can be checked to see if the filtering should be cancelled.
        /// </summary>
        public CancellationToken CancellationToken { get; }
    }

    /// <summary>
    /// Context passed to an incremental generator when it has registered an output via <see cref="IncrementalGeneratorInitializationContext.RegisterHostOutput{TSource}(IncrementalValuesProvider{TSource}, Action{HostOutputProductionContext, TSource})"/>
    /// </summary>
    [Experimental(RoslynExperiments.GeneratorHostOutputs, UrlFormat = RoslynExperiments.GeneratorHostOutputs_Url)]
    public readonly struct HostOutputProductionContext
    {
        internal readonly ArrayBuilder<(string, object)> Outputs;

        internal HostOutputProductionContext(ArrayBuilder<(string, object)> outputs, CancellationToken cancellationToken)
        {
            Outputs = outputs;
            CancellationToken = cancellationToken;
        }

        /// <summary>
        /// Adds a host specific output
        /// </summary>
        /// <param name="name">The name of the output to be added.</param>
        /// <param name="value">The output to be added.</param>
        /// <remarks>
        /// A host output has no defined use. It does not contribute to the final compilation in any way. Any outputs registered 
        /// here are made available via the <see cref="GeneratorRunResult.HostOutputs"/> collection, and it is up the host to
        /// decide how to use them. A host may also disable these outputs altogether if they are not needed. The generator driver
        /// otherwise makes no guarantees about how the outputs are used, other than that they will be present if the host has
        /// requested they be produced.
        /// </remarks>
        public void AddOutput(string name, object value) => Outputs.Add((name, value));

        /// <summary>
        /// A <see cref="System.Threading.CancellationToken"/> that can be checked to see if producing the output should be cancelled.
        /// </summary>
        public CancellationToken CancellationToken { get; }
    }

    // https://github.com/dotnet/roslyn/issues/53608 right now we only support generating source + diagnostics, but actively want to support generation of other things
    internal readonly struct IncrementalExecutionContext
    {
        internal readonly DiagnosticBag Diagnostics;

        internal readonly AdditionalSourcesCollection Sources;

        internal readonly DriverStateTable.Builder? TableBuilder;

        internal readonly GeneratorRunStateTable.Builder GeneratorRunStateBuilder;

        internal readonly ImmutableDictionary<string, object>.Builder HostOutputBuilder;

        public IncrementalExecutionContext(DriverStateTable.Builder? tableBuilder, GeneratorRunStateTable.Builder generatorRunStateBuilder, AdditionalSourcesCollection sources)
        {
            TableBuilder = tableBuilder;
            GeneratorRunStateBuilder = generatorRunStateBuilder;
            Sources = sources;
            HostOutputBuilder = ImmutableDictionary.CreateBuilder<string, object>();
            Diagnostics = DiagnosticBag.GetInstance();
        }

        internal (ImmutableArray<GeneratedSourceText> sources, ImmutableArray<Diagnostic> diagnostics, GeneratorRunStateTable executedSteps, ImmutableDictionary<string, object> hostOutputs) ToImmutableAndFree()
                => (Sources.ToImmutableAndFree(), Diagnostics.ToReadOnlyAndFree(), GeneratorRunStateBuilder.ToImmutableAndFree(), HostOutputBuilder.ToImmutable());

        internal void Free()
        {
            Sources.Free();
            Diagnostics.Free();
        }
    }
}
