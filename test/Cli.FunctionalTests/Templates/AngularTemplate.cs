// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Cli.FunctionalTests.Util;

namespace Cli.FunctionalTests.Templates
{
    public class AngularTemplate : SpaBaseTemplate
    {
        public AngularTemplate() { }

        public override string Name => "angular";

        protected override IEnumerable<string> NormalizeFilesAfterPublish(IEnumerable<string> filesAfterPublish)
        {
            // Remove generated hashes since they may vary by platform
            return base.NormalizeFilesAfterPublish(filesAfterPublish)
                .Select(f => Regex.Replace(f, @"\.[0-9a-f]{20}\.", ".[HASH]."));
        }



        public override IEnumerable<string> ExpectedFilesAfterPublish =>
            base.ExpectedFilesAfterPublish
            .Concat(new[] {
                Path.Combine("wwwroot", "favicon.ico"),
                Path.Combine("ClientApp", "dist", "3rdpartylicenses.txt"),
                Path.Combine("ClientApp", "dist", "index.html"),
                Path.Combine("ClientApp", "dist", "glyphicons-halflings-regular.[HASH].woff2"),
                Path.Combine("ClientApp", "dist", "glyphicons-halflings-regular.[HASH].svg"),
                Path.Combine("ClientApp", "dist", "glyphicons-halflings-regular.[HASH].ttf"),
                Path.Combine("ClientApp", "dist", "glyphicons-halflings-regular.[HASH].eot"),
                Path.Combine("ClientApp", "dist", "glyphicons-halflings-regular.[HASH].woff"),
                Path.Combine("ClientApp", "dist", $"inline.[HASH].bundle.js"),
                Path.Combine("ClientApp", "dist", $"main.[HASH].bundle.js"),
                Path.Combine("ClientApp", "dist", $"polyfills.[HASH].bundle.js"),
                Path.Combine("ClientApp", "dist", $"styles.[HASH].bundle.css"),
            });

    }
}
