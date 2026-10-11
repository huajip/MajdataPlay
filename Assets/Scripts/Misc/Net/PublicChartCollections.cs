using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine.Scripting;

namespace MajdataPlay.Net
{
    [Preserve]
    [JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    internal sealed class PublicCollectionCapability
    {
        public bool PublicCollections { get; init; }
        public string PublicCollectionPath { get; init; }
    }

    internal static class PublicChartCollections
    {
        public static bool IsApiRoot(Uri url) => url is not null && url.IsAbsoluteUri &&
            (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp) &&
            url.UserInfo.Length == 0 && url.Query.Length == 0 && url.Fragment.Length == 0;

        public static bool TryGetCollectionUri(Uri apiRoot, PublicCollectionCapability capability, out Uri result)
        {
            result = null;
            if (!IsApiRoot(apiRoot) || capability?.PublicCollections != true ||
                string.IsNullOrWhiteSpace(capability.PublicCollectionPath)) return false;
            var path = capability.PublicCollectionPath;
            if (path.StartsWith("//", StringComparison.Ordinal) || path.Contains("\\") ||
                Uri.TryCreate(path, UriKind.Absolute, out _)) return false;
            var basis = new Uri(apiRoot.ToString().TrimEnd('/') + "/");
            if (!Uri.TryCreate(basis, path, out var target) || target.Scheme != apiRoot.Scheme ||
                target.DnsSafeHost != apiRoot.DnsSafeHost || target.Port != apiRoot.Port ||
                target.UserInfo.Length != 0 || target.Query.Length != 0 || target.Fragment.Length != 0) return false;
            result = target;
            return true;
        }

        public static bool IsSystemCollection(string id) => id is not null &&
            (id.StartsWith("system:version:", StringComparison.Ordinal) ||
             id.StartsWith("system:genre:", StringComparison.Ordinal));
    }
}
