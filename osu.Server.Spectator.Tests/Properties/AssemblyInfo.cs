// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using Xunit;

// Currently StatefulUserHub and hub classes themselves store certain data and states statically.
// This never goes well with test parallelization, especially when two tests touch the same static member.
// Therefore disable test parallelization for the time being.

[assembly: CollectionBehavior(DisableTestParallelization = true)]