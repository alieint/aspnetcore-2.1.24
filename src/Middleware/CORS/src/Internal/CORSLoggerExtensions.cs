// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Cors.Internal
{
    internal static class CORSLoggerExtensions
    {
        private static readonly Action<ILogger, Exception> _isPreflightRequest;
        private static readonly Action<ILogger, string, Exception> _requestHasOriginHeader;
        private static readonly Action<ILogger, Exception> _requestDoesNotHaveOriginHeader;
        private static readonly Action<ILogger, Exception> _policySuccess;
        private static readonly Action<ILogger, Exception> _policyFailure;
        private static readonly Action<ILogger, string, Exception> _originNotAllowed;
        private static readonly Action<ILogger, string, Exception> _accessControlMethodNotAllowed;
        private static readonly Action<ILogger, string, Exception> _requestHeaderNotAllowed;

        static CORSLoggerExtensions()
        {
            _isPreflightRequest = LoggerMessage.Define(
                LogLevel.Debug,
                1,
                "The request is a preflight request.");

            _requestHasOriginHeader = LoggerMessage.Define<string>(
                LogLevel.Debug,
                2,
                "The request has an origin header: '{origin}'.");

            _requestDoesNotHaveOriginHeader = LoggerMessage.Define(
                LogLevel.Debug,
                3,
                "The request does not have an origin header.");

            _policySuccess = LoggerMessage.Define(
                LogLevel.Information,
                4,
                "Policy execution successful.");

            _policyFailure = LoggerMessage.Define(
                LogLevel.Information,
                5,
                "Policy execution failed.");

            _originNotAllowed = LoggerMessage.Define<string>(
                LogLevel.Information,
                6,
                "Request origin {origin} does not have permission to access the resource.");

            _accessControlMethodNotAllowed = LoggerMessage.Define<string>(
                LogLevel.Information,
                7,
                "Request method {accessControlRequestMethod} not allowed in CORS policy.");

            _requestHeaderNotAllowed = LoggerMessage.Define<string>(
                LogLevel.Information,
                8,
                "Request header '{requestHeader}' not allowed in CORS policy.");
        }

        public static void IsPreflightRequest(this ILogger logger)
        {
            _isPreflightRequest(logger, null);
        }

        public static void RequestHasOriginHeader(this ILogger logger, string origin)
        {
            _requestHasOriginHeader(logger, origin, null);
        }

        public static void RequestDoesNotHaveOriginHeader(this ILogger logger)
        {
            _requestDoesNotHaveOriginHeader(logger, null);
        }

        public static void PolicySuccess(this ILogger logger)
        {
            _policySuccess(logger, null);
        }

        public static void PolicyFailure(this ILogger logger)
        {
            _policyFailure(logger, null);
        }

        public static void OriginNotAllowed(this ILogger logger, string origin)
        {
            _originNotAllowed(logger, origin, null);
        }

        public static void AccessControlMethodNotAllowed(this ILogger logger, string accessControlMethod)
        {
            _accessControlMethodNotAllowed(logger, accessControlMethod, null);
        }

        public static void RequestHeaderNotAllowed(this ILogger logger, string requestHeader)
        {
            _requestHeaderNotAllowed(logger, requestHeader, null);
        }
    }
}
