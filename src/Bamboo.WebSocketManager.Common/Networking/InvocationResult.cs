﻿using MessagePack;
using Newtonsoft.Json;
using System;

namespace Bamboo.WebSocketManager.Common
{
    /// <summary>
    /// Represents the return value of a method that was executed remotely.
    /// </summary>
    [MessagePackObject]
    public class InvocationResult
    {
        /// <summary>
        /// Gets the version of jsonrpc protocol.
        /// </summary>
        /// <value>The version of jsonrpc protocol.</value>
        [JsonProperty("jsonrpc")]
        [Key("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";

        /// <summary>
        /// Gets or sets the unique identifier associated with the invocation.
        /// </summary>
        /// <value>The unique identifier of the invocation.</value>
        [JsonProperty("id")]
        [Key("id")]
        public long Id { get; set; }

        /// <summary>
        /// Gets or sets the name of the remote method.
        /// </summary>
        /// <value>The name of the remote method.</value>
        [JsonProperty("method", NullValueHandling = NullValueHandling.Ignore)]
        [Key("method")]
        public string MethodName { get; set; }

        /// <summary>
        /// Gets or sets the result of the method call.
        /// </summary>
        /// <value>The result of the method call.</value>
        [JsonProperty("result", NullValueHandling = NullValueHandling.Ignore)]
        [Key("result")]
        public object Result { get; set; }

        /// <summary>
        /// Gets or sets the remote exception the method call caused.
        /// </summary>
        /// <value>The remote exception of the method call.</value>
        [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
        [Key("error")]
        public RemoteException Exception { get; set; }
    }
}