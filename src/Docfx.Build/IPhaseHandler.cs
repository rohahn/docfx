﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Docfx.Plugins;

namespace Docfx.Build.Engine;

internal interface IPhaseHandler
{
    void Handle(List<HostService> hostServices, int maxParallelism);
}
