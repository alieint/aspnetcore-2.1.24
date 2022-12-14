// Copyright(c) .NET Foundation.All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Internal;

namespace Microsoft.AspNetCore.Razor.Language.Legacy
{
    internal class DirectiveTokenChunkGenerator : SpanChunkGenerator
    {
        private static readonly Type Type = typeof(DirectiveTokenChunkGenerator);

        public DirectiveTokenChunkGenerator(DirectiveTokenDescriptor tokenDescriptor)
        {
            Descriptor = tokenDescriptor;
        }

        public DirectiveTokenDescriptor Descriptor { get; set; }

        public override void Accept(ParserVisitor visitor, Span span)
        {
            visitor.VisitDirectiveToken(this, span);
        }

        public override bool Equals(object obj)
        {
            var other = obj as DirectiveTokenChunkGenerator;
            return base.Equals(other) &&
                DirectiveTokenDescriptorComparer.Default.Equals(Descriptor, other.Descriptor);
        }

        public override int GetHashCode()
        {
            var combiner = HashCodeCombiner.Start();
            combiner.Add(base.GetHashCode());
            combiner.Add(Type);

            return combiner.CombinedHash;
        }
    }
}
