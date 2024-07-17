// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Build.Construction;
using Microsoft.Build.Shared;

namespace Microsoft.Build.Experimental.BuildCheck.Analyzers;

internal sealed class SharedOutputPathAnalyzer : BuildAnalyzer
{
    public static BuildAnalyzerRule SupportedRule = new BuildAnalyzerRule("BC0101", "ConflictingOutputPath",
        "Two projects should not share their OutputPath nor IntermediateOutputPath locations",
        "Projects {0} and {1} have conflicting output paths: {2}.",
        new BuildAnalyzerConfiguration() { Severity = BuildAnalyzerResultSeverity.Warning });

    public override string FriendlyName => "MSBuild.SharedOutputPathAnalyzer";

    public override IReadOnlyList<BuildAnalyzerRule> SupportedRules { get; } = [SupportedRule];

    public override void Initialize(ConfigurationContext configurationContext)
    {
        /* This is it - no custom configuration */
    }

    public override void RegisterActions(IBuildCheckRegistrationContext registrationContext)
    {
        registrationContext.RegisterEvaluatedPropertiesAction(EvaluatedPropertiesAction);
    }

    private readonly Dictionary<string, string> _projectsPerOutputPath = new(StringComparer.CurrentCultureIgnoreCase);
    private readonly HashSet<string> _projects = new(StringComparer.CurrentCultureIgnoreCase);

    private void EvaluatedPropertiesAction(BuildCheckDataContext<EvaluatedPropertiesAnalysisData> context)
    {
        if (!_projects.Add(context.Data.ProjectFilePath))
        {
            return;
        }

        (string Value, string File, int Line, int Column) binMetadata;
        (string Value, string File, int Line, int Column) objMetadata;
        context.Data.EvaluatedProperties.TryGetPathValue("OutputPath", out binMetadata);
        context.Data.EvaluatedProperties.TryGetPathValue("IntermediateOutputPath", out objMetadata);

        Debugger.Launch();
        string? absoluteBinPath = CheckAndAddFullOutputPath(binMetadata, context);
        // Check objPath only if it is different from binPath
        if (
            !string.IsNullOrEmpty(objMetadata.Value) && !string.IsNullOrEmpty(absoluteBinPath) &&
            !objMetadata.Value.Equals(binMetadata.Value, StringComparison.CurrentCultureIgnoreCase)
            && !objMetadata.Value.Equals(absoluteBinPath, StringComparison.CurrentCultureIgnoreCase)
        )
        {
            CheckAndAddFullOutputPath(objMetadata, context);
        }
    }

    private string? CheckAndAddFullOutputPath((string Value, string File, int Line, int Column) metadata, BuildCheckDataContext<EvaluatedPropertiesAnalysisData> context)
    {
        if (string.IsNullOrEmpty(metadata.Value))
        {
            return metadata.Value;
        }

        string projectPath = context.Data.ProjectFilePath;

        if (!Path.IsPathRooted(metadata.Value))
        {
            metadata.Value = Path.Combine(Path.GetDirectoryName(projectPath)!, metadata.Value);
        }

        // Normalize the path to avoid false negatives due to different path representations.
        metadata.Value = Path.GetFullPath(metadata.Value);

        if (_projectsPerOutputPath.TryGetValue(metadata.Value!, out string? conflictingProject))
        {
            context.ReportResult(BuildCheckResult.Create(
                SupportedRule,
                // Populating precise location tracked via https://github.com/orgs/dotnet/projects/373/views/1?pane=issue&itemId=58661732
                ElementLocation.Create(metadata.File, metadata.Line, metadata.Column),
                Path.GetFileName(projectPath),
                Path.GetFileName(conflictingProject),
                metadata.Value!));
        }
        else
        {
            _projectsPerOutputPath[metadata.Value!] = projectPath;
        }

        return metadata.Value!;
    }
}
