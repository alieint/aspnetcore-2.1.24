// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Mvc.RazorPages.Internal
{
    public delegate Task PageHandlerBinderDelegate(PageContext pageContext, IDictionary<string, object> arguments);
}
