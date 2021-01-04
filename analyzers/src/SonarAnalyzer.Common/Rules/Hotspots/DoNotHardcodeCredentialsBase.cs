﻿/*
 * SonarAnalyzer for .NET
 * Copyright (C) 2015-2020 SonarSource SA
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

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using SonarAnalyzer.Common;
using SonarAnalyzer.Helpers;

namespace SonarAnalyzer.Rules
{
    public abstract class DoNotHardcodeCredentialsBase<TSyntaxKind> : ParameterLoadingDiagnosticAnalyzer
        where TSyntaxKind : struct
    {
        protected const string DiagnosticId = "S2068";
        protected const char CredentialSeparator = ';';
        private const string MessageFormat = "{0}";
        private const string MessageHardcodedPassword = "Please review this hard-coded password.";
        private const string MessageFormatCredential = @"""{0}"" detected here, make sure this is not a hard-coded credential.";
        private const string MessageUriUserInfo = "Review this hard-coded URI, which may contain a credential.";
        private const string DefaultCredentialWords = "password, passwd, pwd, passphrase";

        private readonly IAnalyzerConfiguration analyzerConfiguration;
        private string credentialWords;
        private IEnumerable<string> splitCredentialWords;
        private Regex passwordValuePattern;

        protected abstract void InitializeActions(ParameterLoadingAnalysisContext context);

        [RuleParameter("credentialWords", PropertyType.String, "Comma separated list of words identifying potential credentials", DefaultCredentialWords)]
        public string CredentialWords
        {
            get => credentialWords;
            set
            {
                credentialWords = value;
                splitCredentialWords = value.ToUpperInvariant()
                    .Split(',')
                    .Select(x => x.Trim())
                    .Where(x => x.Length != 0)
                    .ToList();
                passwordValuePattern = new Regex(string.Format(@"\b(?<credential>{0})\s*[:=]\s*(?<suffix>.+)$",
                    string.Join("|", splitCredentialWords.Select(Regex.Escape))), RegexOptions.Compiled | RegexOptions.IgnoreCase);
            }
        }

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);
        protected DiagnosticDescriptor Rule { get; }
        protected ObjectCreationTracker<TSyntaxKind> ObjectCreationTracker { get; set; }
        protected PropertyAccessTracker<TSyntaxKind> PropertyAccessTracker { get; set; }

        protected DoNotHardcodeCredentialsBase(System.Resources.ResourceManager rspecResources, IAnalyzerConfiguration analyzerConfiguration)
        {
            Rule = DiagnosticDescriptorBuilder.GetDescriptor(DiagnosticId, MessageFormat, rspecResources).WithNotConfigurable();
            CredentialWords = DefaultCredentialWords;
            this.analyzerConfiguration = analyzerConfiguration;
        }

        protected sealed override void Initialize(ParameterLoadingAnalysisContext context)
        {
            var innerContext = context.GetInnerContext();

            ObjectCreationTracker.Track(innerContext, new object[] { MessageHardcodedPassword },
                ObjectCreationTracker.MatchConstructor(KnownType.System_Net_NetworkCredential),
                ObjectCreationTracker.ArgumentAtIndexIs(1, KnownType.System_String),
                ObjectCreationTracker.ArgumentAtIndexIsConst(1));

            ObjectCreationTracker.Track(innerContext, new object[] { MessageHardcodedPassword },
               ObjectCreationTracker.MatchConstructor(KnownType.System_Security_Cryptography_PasswordDeriveBytes),
               ObjectCreationTracker.ArgumentAtIndexIs(0, KnownType.System_String),
               ObjectCreationTracker.ArgumentAtIndexIsConst(0));

            PropertyAccessTracker.Track(innerContext, new object[] { MessageHardcodedPassword },
               PropertyAccessTracker.MatchSetter(),
               PropertyAccessTracker.AssignedValueIsConstant(),
               PropertyAccessTracker.MatchProperty(new MemberDescriptor(KnownType.System_Net_NetworkCredential, "Password")));

            InitializeActions(context);
        }

        protected bool IsEnabled(AnalyzerOptions options)
        {
            analyzerConfiguration.Initialize(options);
            return analyzerConfiguration.IsEnabled(DiagnosticId);
        }

        protected abstract class CredentialWordsFinderBase<TSyntaxNode>
             where TSyntaxNode : SyntaxNode
        {
            private readonly Regex validCredentialPattern = new Regex(@"^\?|:\w+|\{\d+[^}]*\}|""|'$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            private readonly Regex uriUserInfoPattern;
            private readonly DoNotHardcodeCredentialsBase<TSyntaxKind> analyzer;

            protected abstract bool IsAssignedWithStringLiteral(TSyntaxNode syntaxNode, SemanticModel semanticModel);
            protected abstract string GetVariableName(TSyntaxNode syntaxNode);
            protected abstract string GetAssignedValue(TSyntaxNode syntaxNode, SemanticModel semanticModel);

            protected CredentialWordsFinderBase(DoNotHardcodeCredentialsBase<TSyntaxKind> analyzer)
            {
                // See https://tools.ietf.org/html/rfc3986 Userinfo can contain groups: unreserved | pct-encoded | sub-delims
                const string Rfc3986_Unreserved = "-._~";  // Numbers and letters are embeded in regex itself without escaping
                const string Rfc3986_Pct = "%";
                const string Rfc3986_SubDelims = "!$&'()*+,;=";
                const string UriPasswordSpecialCharacters = Rfc3986_Unreserved + Rfc3986_Pct + Rfc3986_SubDelims;
                var uriUserInfoPart = @"[\w\d" + Regex.Escape(UriPasswordSpecialCharacters) + "]+";

                this.analyzer = analyzer;
                uriUserInfoPattern = new Regex(@"\w+:\/\/(?<Login>" + uriUserInfoPart + "):(?<Password>" + uriUserInfoPart + ")@", RegexOptions.Compiled);
            }

            public Action<SyntaxNodeAnalysisContext> AnalysisAction() =>
                context =>
                {
                    var declarator = (TSyntaxNode)context.Node;
                    if (!IsAssignedWithStringLiteral(declarator, context.SemanticModel))
                    {
                        return;
                    }
                    if (GetAssignedValue(declarator, context.SemanticModel) is { } variableValue && !string.IsNullOrWhiteSpace(variableValue))
                    {
                        var bannedWords = FindCredentialWords(GetVariableName(declarator), variableValue);
                        if (bannedWords.Any())
                        {
                            context.ReportDiagnosticWhenActive(Diagnostic.Create(analyzer.Rule, declarator.GetLocation(), string.Format(MessageFormatCredential, bannedWords.JoinStr(", "))));
                        }
                        else if (ContainsUriUserInfo(variableValue))
                        {
                            context.ReportDiagnosticWhenActive(Diagnostic.Create(analyzer.Rule, declarator.GetLocation(), MessageUriUserInfo));
                        }
                    }
                };

            private IEnumerable<string> FindCredentialWords(string variableName, string variableValue)
            {
                var credentialWordsFound = variableName
                    .SplitCamelCaseToWords()
                    .Intersect(analyzer.splitCredentialWords)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                if (credentialWordsFound.Any(x => variableValue.IndexOf(x, StringComparison.InvariantCultureIgnoreCase) >= 0))
                {
                    // See https://github.com/SonarSource/sonar-dotnet/issues/2868
                    return Enumerable.Empty<string>();
                }

                var match = analyzer.passwordValuePattern.Match(variableValue);
                if (match.Success && !IsValidCredential(match.Groups["suffix"].Value))
                {
                    credentialWordsFound.Add(match.Groups["credential"].Value);
                }

                // Rule was initially implemented with everything lower (which is wrong) so we have to force lower
                // before reporting to avoid new issues to appear on SQ/SC.
                return credentialWordsFound.Select(x => x.ToLowerInvariant());
            }

            private bool IsValidCredential(string suffix)
            {
                var candidateCredential = suffix.Split(CredentialSeparator).First().Trim();
                return string.IsNullOrWhiteSpace(candidateCredential) || validCredentialPattern.IsMatch(candidateCredential);
            }

            private bool ContainsUriUserInfo(string variableValue)
            {
                var match = uriUserInfoPattern.Match(variableValue);
                return match.Success
                    && match.Groups["Password"].Value is { } password
                    && !string.Equals(match.Groups["Login"].Value, password, StringComparison.OrdinalIgnoreCase)
                    && password != CredentialSeparator.ToString()
                    && !validCredentialPattern.IsMatch(password);
            }
        }
    }
}
