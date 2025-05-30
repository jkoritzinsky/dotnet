﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See License.txt in the project root for license information.

using System.ComponentModel.Composition;
using Microsoft.CodeAnalysis.Razor.Completion;

namespace Microsoft.VisualStudio.Razor.LanguageClient.Cohost;

[Export(typeof(CompletionListCache))]
internal class CohostCompletionListCache : CompletionListCache;
