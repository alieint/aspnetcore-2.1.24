// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal
{
    internal class ConfigurationReader
    {
        private IConfiguration _configuration;
        private IDictionary<string, CertificateConfig> _certificates;
        private IList<EndpointConfig> _endpoints;

        public ConfigurationReader(IConfiguration configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        public IDictionary<string, CertificateConfig> Certificates
        {
            get
            {
                if (_certificates == null)
                {
                    ReadCertificates();
                }

                return _certificates;
            }
        }

        public IEnumerable<EndpointConfig> Endpoints
        {
            get
            {
                if (_endpoints == null)
                {
                    ReadEndpoints();
                }

                return _endpoints;
            }
        }

        private void ReadCertificates()
        {
            _certificates = new Dictionary<string, CertificateConfig>(0);

            var certificatesConfig = _configuration.GetSection("Certificates").GetChildren();
            foreach (var certificateConfig in certificatesConfig)
            {
                _certificates.Add(certificateConfig.Key, new CertificateConfig(certificateConfig));
            }
        }

        private void ReadEndpoints()
        {
            _endpoints = new List<EndpointConfig>();

            var endpointsConfig = _configuration.GetSection("Endpoints").GetChildren();
            foreach (var endpointConfig in endpointsConfig)
            {
                // "EndpointName": {
                //    "Url": "https://*:5463",
                //    "Certificate": {
                //        "Path": "testCert.pfx",
                //        "Password": "testPassword"
                //    }
                // }
                
                var url = endpointConfig["Url"];
                if (string.IsNullOrEmpty(url))
                {
                    throw new InvalidOperationException(CoreStrings.FormatEndpointMissingUrl(endpointConfig.Key));
                }

                var endpoint = new EndpointConfig()
                {
                    Name = endpointConfig.Key,
                    Url = url,
                    ConfigSection = endpointConfig,
                    Certificate = new CertificateConfig(endpointConfig.GetSection("Certificate")),
                };
                _endpoints.Add(endpoint);
            }
        }
    }

    // "EndpointName": {
    //    "Url": "https://*:5463",
    //    "Certificate": {
    //        "Path": "testCert.pfx",
    //        "Password": "testPassword"
    //    }
    // }
    internal class EndpointConfig
    {
        public string Name { get; set; }
        public string Url { get; set; }
        public IConfigurationSection ConfigSection { get; set; }
        public CertificateConfig Certificate { get; set; }
    }

    // "CertificateName": {
    //      "Path": "testCert.pfx",
    //      "Password": "testPassword"
    // }
    internal class CertificateConfig
    {
        public CertificateConfig(IConfigurationSection configSection)
        {
            ConfigSection = configSection;
            ConfigSection.Bind(this);
        }

        public IConfigurationSection ConfigSection { get; }

        // File
        public bool IsFileCert => !string.IsNullOrEmpty(Path);

        public string Path { get; set; }

        public string Password { get; set; }

        // Cert store

        public bool IsStoreCert => !string.IsNullOrEmpty(Subject);

        public string Subject { get; set; }

        public string Store { get; set; }

        public string Location { get; set; }

        public bool? AllowInvalid { get; set; }
    }
}
