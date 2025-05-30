﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable

using Microsoft.Extensions.EnvironmentAbstractions;

namespace Microsoft.DotNet.Cli.ShellShim;

internal interface IAppHostShellShimMaker
{
    void CreateApphostShellShim(FilePath entryPoint, FilePath shimPath);
}
