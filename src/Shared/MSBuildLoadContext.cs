﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.


using Microsoft.Build.Shared.FileSystem;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

#nullable enable
namespace Microsoft.Build.Shared
{
    /// <summary>
    /// This class is used to isolate the types used by an MSBuild plugin
    /// (SDK resolver, logger, or task).
    /// </summary>
    internal class MSBuildLoadContext : AssemblyLoadContext
    {
        private readonly string _directory;
        private readonly object _guard = new object();

        private static readonly ImmutableHashSet<string> _wellKnownAssemblyNames =
            new[]
            {
                "MSBuild",
                "Microsoft.Build",
                "Microsoft.Build.Framework",
                "Microsoft.Build.Tasks.Core",
                "Microsoft.Build.Utilities.Core",
            }.ToImmutableHashSet();

        private static readonly string[] _extensions = new[] { "ni.dll", "ni.exe", "dll", "exe" };


        public MSBuildLoadContext(string assemblyPath)
        {
            _directory = Directory.GetParent(assemblyPath).FullName;
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (_wellKnownAssemblyNames.Contains(assemblyName.Name!))
            {
                // Force MSBuild assemblies to be loaded in the default ALC
                // and unify to the current version.
                return null;
            }

            foreach (var cultureSubfolder in string.IsNullOrEmpty(assemblyName.CultureName)
                // If no culture is specified, attempt to load directly from
                // the known dependency paths.
                ? new[] { string.Empty }
                // Search for satellite assemblies in culture subdirectories
                // of the assembly search directories, but fall back to the
                // bare search directory if that fails.
                : new[] { assemblyName.CultureName, string.Empty })
            {
                foreach (var extension in _extensions)
                {
                    var candidatePath = Path.Combine(_directory,
                        cultureSubfolder,
                        $"{assemblyName.Name}.{extension}");

                    if (!FileSystems.Default.FileExists(candidatePath))
                    {
                        continue;
                    }

                    AssemblyName candidateAssemblyName = AssemblyLoadContext.GetAssemblyName(candidatePath);
                    if (candidateAssemblyName.Version != assemblyName.Version)
                    {
                        continue;
                    }

                    return LoadFromAssemblyPath(candidatePath);
                }
            }

            // If the Assembly is provided via a file path, the following rules are used to load the assembly:
            // - if the simple name of the assembly exists in the same folder as msbuild.exe, then that assembly gets loaded, indifferent of the user specified path
            // - otherwise, the assembly from the user specified path is loaded, if it exists.

            var assemblyNameInExecutableDirectory = Path.Combine(BuildEnvironmentHelper.Instance.CurrentMSBuildToolsDirectory,
                assemblyName.Name);

            if (FileSystems.Default.FileExists(assemblyNameInExecutableDirectory))
            {
                return LoadFromAssemblyPath(assemblyNameInExecutableDirectory);
            }

            return null;
        }
    }
}
