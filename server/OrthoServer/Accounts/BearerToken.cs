using System;
using Microsoft.AspNetCore.Http;

namespace OrthoServer.Accounts
{
    public static class BearerToken
    {
        /// <summary>
        /// The bearer token a request carries (a linked game's), if any.
        /// </summary>
        public static string? Of(HttpRequest request)
        {
            string? header = request.Headers.Authorization;
            return header != null && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header["Bearer ".Length..].Trim() : null;
        }
    }
}
