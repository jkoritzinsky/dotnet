// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable

namespace Microsoft.DotNet.Cli.NugetSearch.NugetSearchApiSerializable;

internal class NugetSearchApiPackageSerializable
{
    public string Id { get; set; }
    public string Version { get; set; }
    public string Description { get; set; }
    public string Summary { get; set; }
    public string[] Tags { get; set; }
    public NugetSearchApiAuthorsSerializable Authors { get; set; }
    public int TotalDownloads { get; set; }
    public bool Verified { get; set; }
    public NugetSearchApiVersionSerializable[] Versions { get; set; }
}
