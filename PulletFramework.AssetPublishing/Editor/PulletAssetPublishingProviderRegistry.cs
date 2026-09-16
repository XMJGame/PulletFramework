using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace PulletFramework.AssetPublishing.Editor
{
    internal static class PulletAssetPublishingProviderRegistry
    {
        public static List<TProvider> CreateProviders<TProvider>()
            where TProvider : class, IPulletAssetPublishingProvider
        {
            var providers = new List<TProvider>();
            var providerIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (Type type in TypeCache.GetTypesDerivedFrom<TProvider>()
                         .Where(IsConstructible)
                         .OrderBy(item => item.FullName, StringComparer.Ordinal))
            {
                try
                {
                    if (!(Activator.CreateInstance(type) is TProvider provider)
                        || string.IsNullOrWhiteSpace(provider.Id)
                        || !providerIds.Add(provider.Id))
                        continue;

                    providers.Add(provider);
                }
                catch (Exception exception)
                {
                    PLogger.EditorException(exception,
                        $"资源发布 Provider 创建失败：{type.FullName}");
                }
            }

            return providers;
        }

        private static bool IsConstructible(Type type)
        {
            return type != null
                   && !type.IsAbstract
                   && !type.IsInterface
                   && !type.ContainsGenericParameters
                   && type.GetConstructor(Type.EmptyTypes) != null;
        }
    }
}
