﻿using MessagePack;
using Newtonsoft.Json;
using System;

namespace Bamboo.WebSocketManager.Common
{
    /// <summary>
    /// Represents a method name with parameters that is to be executed remotely.
    /// </summary>
    [MessagePackObject]
    public class InvocationDescriptor
    {
        /// <summary>
        /// Gets the version of jsonrpc protocol.
        /// </summary>
        /// <value>The version of jsonrpc protocol.</value>
        [JsonProperty("jsonrpc")]
        [Key("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";

        /// <summary>
        /// Gets or sets the unique identifier used to associate return values with this call.
        /// </summary>
        /// <value>The unique identifier of the invocation.</value>
        [JsonProperty("id")]
        [Key("id")]
        public long Id { get; set; } = 0;

        /// <summary>
        /// Gets or sets the name of the remote method.
        /// </summary>
        /// <value>The name of the remote method.</value>
        [JsonProperty("method")]
        [Key("method")]
        public string MethodName { get; set; }

        /// <summary>
        /// Gets or sets the arguments passed to the method.
        /// </summary>
        /// <value>The arguments passed to the method.</value>
        [JsonProperty("params", NullValueHandling = NullValueHandling.Ignore)]
        [Key("params")]
        public object Params { get; set; }

        //[JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
        //public object Error { get; set; }

        //[JsonProperty("result", NullValueHandling = NullValueHandling.Ignore)]
        //public object Result { get; set; }

    }
}