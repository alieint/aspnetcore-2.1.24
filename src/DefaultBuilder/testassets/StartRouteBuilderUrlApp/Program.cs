// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace StartRequestDelegateUrlApp
{
    public class Program
    {
        static void Main(string[] args)
        {
            var messageSent = new ManualResetEventSlim(false);

            using (var host = WebHost.Start("http://127.0.0.1:0", router =>
                router.MapGet("route", async (req, res, data) =>
                {
                    var env = req.HttpContext.RequestServices.GetRequiredService<IHostingEnvironment>();
                    await res.WriteAsync(env.ApplicationName);
                    messageSent.Set();
                })))
            {
                // Need these for test deployer to consider host deployment successful
                // The address written here is used by the client to send requests
                var addresses = host.ServerFeatures.Get<IServerAddressesFeature>().Addresses;
                foreach (var address in addresses)
                {
                    Console.WriteLine($"Now listening on: {address}");
                }
                Console.WriteLine("Application started. Press Ctrl+C to shut down.");

                // Shut down after message sent or timeout
                messageSent.Wait(TimeSpan.FromSeconds(30));
            }
        }
    }
}