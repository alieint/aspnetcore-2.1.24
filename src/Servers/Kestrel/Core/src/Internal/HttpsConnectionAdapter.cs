// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Certificates.Generation;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Core.Adapter.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Server.Kestrel.Https.Internal
{
    public class HttpsConnectionAdapter : IConnectionAdapter
    {
        private static readonly ClosedAdaptedConnection _closedAdaptedConnection = new ClosedAdaptedConnection();

        private readonly HttpsConnectionAdapterOptions _options;
        private readonly X509Certificate2 _serverCertificate;
        private readonly Func<ConnectionContext, string, X509Certificate2> _serverCertificateSelector;

        private readonly ILogger _logger;

        public HttpsConnectionAdapter(HttpsConnectionAdapterOptions options)
            : this(options, loggerFactory: null)
        {
        }

        public HttpsConnectionAdapter(HttpsConnectionAdapterOptions options, ILoggerFactory loggerFactory)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            // capture the certificate now so it can't be switched after validation
            _serverCertificate = options.ServerCertificate;
            _serverCertificateSelector = options.ServerCertificateSelector;
            if (_serverCertificate == null && _serverCertificateSelector == null)
            {
                throw new ArgumentException(CoreStrings.ServerCertificateRequired, nameof(options));
            }

            // If a selector is provided then ignore the cert, it may be a default cert.
            if (_serverCertificateSelector != null)
            {
                // SslStream doesn't allow both.
                _serverCertificate = null;
            }
            else
            {
                EnsureCertificateIsAllowedForServerAuth(_serverCertificate);
            }

            _options = options;
            _logger = loggerFactory?.CreateLogger(nameof(HttpsConnectionAdapter));
        }

        public bool IsHttps => true;

        public Task<IAdaptedConnection> OnConnectionAsync(ConnectionAdapterContext context)
        {
            // Don't trust SslStream not to block.
            return Task.Run(() => InnerOnConnectionAsync(context));
        }

        private async Task<IAdaptedConnection> InnerOnConnectionAsync(ConnectionAdapterContext context)
        {
            SslStream sslStream;
            bool certificateRequired;
            var feature = new TlsConnectionFeature();
            context.Features.Set<ITlsConnectionFeature>(feature);

            if (_options.ClientCertificateMode == ClientCertificateMode.NoCertificate)
            {
                sslStream = new SslStream(context.ConnectionStream);
                certificateRequired = false;
            }
            else
            {
                sslStream = new SslStream(context.ConnectionStream,
                    leaveInnerStreamOpen: false,
                    userCertificateValidationCallback: (sender, certificate, chain, sslPolicyErrors) =>
                    {
                        if (certificate == null)
                        {
                            return _options.ClientCertificateMode != ClientCertificateMode.RequireCertificate;
                        }

                        if (_options.ClientCertificateValidation == null)
                        {
                            if (sslPolicyErrors != SslPolicyErrors.None)
                            {
                                return false;
                            }
                        }

                        var certificate2 = ConvertToX509Certificate2(certificate);
                        if (certificate2 == null)
                        {
                            return false;
                        }

                        if (_options.ClientCertificateValidation != null)
                        {
                            if (!_options.ClientCertificateValidation(certificate2, chain, sslPolicyErrors))
                            {
                                return false;
                            }
                        }

                        return true;
                    });

                certificateRequired = true;
            }

            var timeoutFeature = context.Features.Get<IConnectionTimeoutFeature>();
            timeoutFeature.SetTimeout(_options.HandshakeTimeout);

            try
            {
#if NETCOREAPP2_1
                // Adapt to the SslStream signature
                ServerCertificateSelectionCallback selector = null;
                if (_serverCertificateSelector != null)
                {
                    selector = (sender, name) =>
                    {
                        context.Features.Set(sslStream);
                        var cert = _serverCertificateSelector(context.ConnectionContext, name);
                        if (cert != null)
                        {
                            EnsureCertificateIsAllowedForServerAuth(cert);
                        }
                        return cert;
                    };
                }

                var sslOptions = new SslServerAuthenticationOptions()
                {
                    ServerCertificate = _serverCertificate,
                    ServerCertificateSelectionCallback = selector,
                    ClientCertificateRequired = certificateRequired,
                    EnabledSslProtocols = _options.SslProtocols,
                    CertificateRevocationCheckMode = _options.CheckCertificateRevocation ? X509RevocationMode.Online : X509RevocationMode.NoCheck,
                    ApplicationProtocols = new List<SslApplicationProtocol>()
                };

                // This is order sensitive
                if ((_options.HttpProtocols & HttpProtocols.Http2) != 0)
                {
                    sslOptions.ApplicationProtocols.Add(SslApplicationProtocol.Http2);
                }

                if ((_options.HttpProtocols & HttpProtocols.Http1) != 0)
                {
                    sslOptions.ApplicationProtocols.Add(SslApplicationProtocol.Http11);
                }

                await sslStream.AuthenticateAsServerAsync(sslOptions, CancellationToken.None);
#else
                var serverCert = _serverCertificate;
                if (_serverCertificateSelector != null)
                {
                    context.Features.Set(sslStream);
                    serverCert = _serverCertificateSelector(context.ConnectionContext, null);
                    if (serverCert != null)
                    {
                        EnsureCertificateIsAllowedForServerAuth(serverCert);
                    }
                }

                await sslStream.AuthenticateAsServerAsync(serverCert, certificateRequired,
                    _options.SslProtocols, _options.CheckCertificateRevocation);
#endif
            }
            catch (OperationCanceledException)
            {
                _logger?.LogDebug(2, CoreStrings.AuthenticationTimedOut);
                sslStream.Dispose();
                return _closedAdaptedConnection;
            }
            catch (IOException ex)
            {
                _logger?.LogDebug(1, ex, CoreStrings.AuthenticationFailed);
                sslStream.Dispose();
                return _closedAdaptedConnection;
            }
            catch (AuthenticationException ex)
            {
                if (_serverCertificate != null &&
                    CertificateManager.IsHttpsDevelopmentCertificate(_serverCertificate) &&
                    !CertificateManager.CheckDeveloperCertificateKey(_serverCertificate))
                {
                    _logger?.LogError(3, ex, CoreStrings.BadDeveloperCertificateState);
                }
                else
                {
                    _logger?.LogDebug(1, ex, CoreStrings.AuthenticationFailed);
                }

                sslStream.Dispose();
                return _closedAdaptedConnection;
            }
            finally
            {
                timeoutFeature.CancelTimeout();
            }

#if NETCOREAPP2_1
            feature.ApplicationProtocol = sslStream.NegotiatedApplicationProtocol.Protocol;
            context.Features.Set<ITlsApplicationProtocolFeature>(feature);
#endif
            feature.ClientCertificate = ConvertToX509Certificate2(sslStream.RemoteCertificate);

            return new HttpsAdaptedConnection(sslStream);
        }

        private static void EnsureCertificateIsAllowedForServerAuth(X509Certificate2 certificate)
        {
            if (!CertificateLoader.IsCertificateAllowedForServerAuth(certificate))
            {
                throw new InvalidOperationException(CoreStrings.FormatInvalidServerCertificateEku(certificate.Thumbprint));
            }
        }

        private static X509Certificate2 ConvertToX509Certificate2(X509Certificate certificate)
        {
            if (certificate == null)
            {
                return null;
            }

            if (certificate is X509Certificate2 cert2)
            {
                return cert2;
            }

            return new X509Certificate2(certificate);
        }

        private class HttpsAdaptedConnection : IAdaptedConnection
        {
            private readonly SslStream _sslStream;

            public HttpsAdaptedConnection(SslStream sslStream)
            {
                _sslStream = sslStream;
            }

            public Stream ConnectionStream => _sslStream;

            public void Dispose()
            {
                _sslStream.Dispose();
            }
        }

        private class ClosedAdaptedConnection : IAdaptedConnection
        {
            public Stream ConnectionStream { get; } = new ClosedStream();

            public void Dispose()
            {
            }
        }
    }
}
