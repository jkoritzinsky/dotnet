﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CodeAnalysis.Analyzers.MetaAnalyzers;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.CodeAnalysis.CSharp.Analyzers.MetaAnalyzers
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class CSharpDiagnosticAnalyzerFieldsAnalyzer : DiagnosticAnalyzerFieldsAnalyzer<ClassDeclarationSyntax, StructDeclarationSyntax, FieldDeclarationSyntax, TypeSyntax, VariableDeclarationSyntax, TypeArgumentListSyntax, GenericNameSyntax>
    {
    }
}
