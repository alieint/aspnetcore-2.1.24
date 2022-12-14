// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Formatters.Json.Internal;
using Newtonsoft.Json;

namespace Microsoft.AspNetCore.Mvc.Formatters
{
    /// <summary>
    /// A <see cref="TextOutputFormatter"/> for JSON content.
    /// </summary>
    public class JsonOutputFormatter : TextOutputFormatter
    {
        private readonly IArrayPool<char> _charPool;
        private JsonSerializerSettings _serializerSettings;

        /// <summary>
        /// Initializes a new <see cref="JsonOutputFormatter"/> instance.
        /// </summary>
        /// <param name="serializerSettings">
        /// The <see cref="JsonSerializerSettings"/>. Should be either the application-wide settings
        /// (<see cref="MvcJsonOptions.SerializerSettings"/>) or an instance
        /// <see cref="JsonSerializerSettingsProvider.CreateSerializerSettings"/> initially returned.
        /// </param>
        /// <param name="charPool">The <see cref="ArrayPool{Char}"/>.</param>
        public JsonOutputFormatter(JsonSerializerSettings serializerSettings, ArrayPool<char> charPool)
        {
            if (serializerSettings == null)
            {
                throw new ArgumentNullException(nameof(serializerSettings));
            }

            if (charPool == null)
            {
                throw new ArgumentNullException(nameof(charPool));
            }

            SerializerSettings = serializerSettings;
            _charPool = new JsonArrayPool<char>(charPool);

            SupportedEncodings.Add(Encoding.UTF8);
            SupportedEncodings.Add(Encoding.Unicode);
            SupportedMediaTypes.Add(MediaTypeHeaderValues.ApplicationJson);
            SupportedMediaTypes.Add(MediaTypeHeaderValues.TextJson);
            SupportedMediaTypes.Add(MediaTypeHeaderValues.ApplicationAnyJsonSyntax);
        }

        /// <summary>
        /// Gets the <see cref="JsonSerializerSettings"/> used to configure the <see cref="JsonSerializer"/>.
        /// </summary>
        /// <remarks>
        /// Any modifications to the <see cref="JsonSerializerSettings"/> object after this
        /// <see cref="JsonOutputFormatter"/> has been used will have no effect.
        /// </remarks>
        protected JsonSerializerSettings SerializerSettings { get; }

        /// <summary>
        /// Gets the <see cref="JsonSerializerSettings"/> used to configure the <see cref="JsonSerializer"/>.
        /// </summary>
        /// <remarks>
        /// Any modifications to the <see cref="JsonSerializerSettings"/> object after this
        /// <see cref="JsonOutputFormatter"/> has been used will have no effect.
        /// </remarks>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public JsonSerializerSettings PublicSerializerSettings => SerializerSettings;

        /// <summary>
        /// Writes the given <paramref name="value"/> as JSON using the given
        /// <paramref name="writer"/>.
        /// </summary>
        /// <param name="writer">The <see cref="TextWriter"/> used to write the <paramref name="value"/></param>
        /// <param name="value">The value to write as JSON.</param>
        public void WriteObject(TextWriter writer, object value)
        {
            if (writer == null)
            {
                throw new ArgumentNullException(nameof(writer));
            }

            using (var jsonWriter = CreateJsonWriter(writer))
            {
                var jsonSerializer = CreateJsonSerializer();
                jsonSerializer.Serialize(jsonWriter, value);
            }
        }

        /// <summary>
        /// Called during serialization to create the <see cref="JsonWriter"/>.
        /// </summary>
        /// <param name="writer">The <see cref="TextWriter"/> used to write.</param>
        /// <returns>The <see cref="JsonWriter"/> used during serialization.</returns>
        protected virtual JsonWriter CreateJsonWriter(TextWriter writer)
        {
            if (writer == null)
            {
                throw new ArgumentNullException(nameof(writer));
            }

            var jsonWriter = new JsonTextWriter(writer)
            {
                ArrayPool = _charPool,
                CloseOutput = false,
                AutoCompleteOnClose = false
            };

            return jsonWriter;
        }

        /// <summary>
        /// Called during serialization to create the <see cref="JsonSerializer"/>.
        /// </summary>
        /// <returns>The <see cref="JsonSerializer"/> used during serialization and deserialization.</returns>
        protected virtual JsonSerializer CreateJsonSerializer()
        {
            if (_serializerSettings == null)
            {
                _serializerSettings = ShallowCopy(SerializerSettings);
            }

            return JsonSerializer.Create(_serializerSettings);
        }

        /// <inheritdoc />
        public override async Task WriteResponseBodyAsync(OutputFormatterWriteContext context, Encoding selectedEncoding)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (selectedEncoding == null)
            {
                throw new ArgumentNullException(nameof(selectedEncoding));
            }

            var response = context.HttpContext.Response;
            using (var writer = context.WriterFactory(response.Body, selectedEncoding))
            {
                WriteObject(writer, context.Object);

                // Perf: call FlushAsync to call WriteAsync on the stream with any content left in the TextWriter's
                // buffers. This is better than just letting dispose handle it (which would result in a synchronous
                // write).
                await writer.FlushAsync();
            }
        }

        private static JsonSerializerSettings ShallowCopy(JsonSerializerSettings settings)
        {
            var copiedSettings = new JsonSerializerSettings
            {
                FloatParseHandling = settings.FloatParseHandling,
                FloatFormatHandling = settings.FloatFormatHandling,
                DateParseHandling = settings.DateParseHandling,
                DateTimeZoneHandling = settings.DateTimeZoneHandling,
                DateFormatHandling = settings.DateFormatHandling,
                Formatting = settings.Formatting,
                MaxDepth = settings.MaxDepth,
                DateFormatString = settings.DateFormatString,
                Context = settings.Context,
                Error = settings.Error,
                SerializationBinder = settings.SerializationBinder,
                TraceWriter = settings.TraceWriter,
                Culture = settings.Culture,
                ReferenceResolverProvider = settings.ReferenceResolverProvider,
                EqualityComparer = settings.EqualityComparer,
                ContractResolver = settings.ContractResolver,
                ConstructorHandling = settings.ConstructorHandling,
                TypeNameAssemblyFormatHandling = settings.TypeNameAssemblyFormatHandling,
                MetadataPropertyHandling = settings.MetadataPropertyHandling,
                TypeNameHandling = settings.TypeNameHandling,
                PreserveReferencesHandling = settings.PreserveReferencesHandling,
                Converters = settings.Converters,
                DefaultValueHandling = settings.DefaultValueHandling,
                NullValueHandling = settings.NullValueHandling,
                ObjectCreationHandling = settings.ObjectCreationHandling,
                MissingMemberHandling = settings.MissingMemberHandling,
                ReferenceLoopHandling = settings.ReferenceLoopHandling,
                CheckAdditionalContent = settings.CheckAdditionalContent,
                StringEscapeHandling = settings.StringEscapeHandling,
            };

            return copiedSettings;
        }
    }
}
