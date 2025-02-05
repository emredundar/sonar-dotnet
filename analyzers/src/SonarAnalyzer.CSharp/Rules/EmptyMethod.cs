﻿/*
 * SonarAnalyzer for .NET
 * Copyright (C) 2015-2022 SonarSource SA
 * mailto: contact AT sonarsource DOT com
 *
 * This program is free software; you can redistribute it and/or
 * modify it under the terms of the GNU Lesser General Public
 * License as published by the Free Software Foundation; either
 * version 3 of the License, or (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
 * Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public License
 * along with this program; if not, write to the Free Software Foundation,
 * Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.
 */

using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using SonarAnalyzer.Helpers;
using StyleCop.Analyzers.Lightup;

namespace SonarAnalyzer.Rules.CSharp
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class EmptyMethod : EmptyMethodBase<SyntaxKind>
    {
        private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(DiagnosticId, MessageFormat);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);
        protected override GeneratedCodeRecognizer GeneratedCodeRecognizer { get; } = CSharpGeneratedCodeRecognizer.Instance;

        protected override SyntaxKind[] SyntaxKinds { get; } =
            {
                SyntaxKind.MethodDeclaration,
                SyntaxKindEx.LocalFunctionStatement
            };

        protected override void CheckMethod(SyntaxNodeAnalysisContext context, bool isTestProject)
        {
            if (LocalFunctionStatementSyntaxWrapper.IsInstance(context.Node))
            {
                var wrapper = (LocalFunctionStatementSyntaxWrapper)context.Node;
                if (wrapper.Body != null && IsEmpty(wrapper.Body))
                {
                    context.ReportIssue(Diagnostic.Create(Rule, wrapper.Identifier.GetLocation()));
                }
            }
            else
            {
                var methodDeclaration = (MethodDeclarationSyntax)context.Node;

                // No need to check for ExpressionBody as arrowed methods can't be empty
                if (methodDeclaration.Body != null
                    && IsEmpty(methodDeclaration.Body)
                    && !ShouldMethodBeExcluded(methodDeclaration, context.SemanticModel, isTestProject))
                {
                    context.ReportIssue(Diagnostic.Create(Rule, methodDeclaration.Identifier.GetLocation()));
                }
            }
        }

        private static bool ShouldMethodBeExcluded(BaseMethodDeclarationSyntax methodNode, SemanticModel semanticModel, bool isTestProject)
        {
            if (methodNode.Modifiers.Any(SyntaxKind.VirtualKeyword))
            {
                return true;
            }

            var methodSymbol = semanticModel.GetDeclaredSymbol(methodNode);
            if (methodSymbol is { IsOverride: true, OverriddenMethod: { IsAbstract: true } })
            {
                return true;
            }

            return methodNode.Modifiers.Any(SyntaxKind.OverrideKeyword) && isTestProject;
        }

        private static bool IsEmpty(BlockSyntax node) =>
            !node.Statements.Any() && !ContainsComment(node);

        private static bool ContainsComment(BlockSyntax node) =>
            ContainsComment(node.OpenBraceToken.TrailingTrivia)
            || ContainsComment(node.CloseBraceToken.LeadingTrivia);

        private static bool ContainsComment(SyntaxTriviaList triviaList) =>
            triviaList.Any(trivia => trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
                                     || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia));
    }
}
